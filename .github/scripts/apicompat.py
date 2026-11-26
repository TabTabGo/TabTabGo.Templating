#!/usr/bin/env python3
"""ApiCompat in assembly mode: the packed assemblies of this line against the previous line's published assemblies.

Package validation cannot compare across target frameworks (a dropped TFM is PKV006 and no API comparison), so the surface check
runs here (spec 12.5 V11-5). The tool comes from .config/dotnet-tools.json (exact version): dotnet tool restore. The packages are only read.

Usage: apicompat.py <packed-dir> <left-tfm> <right-tfm> (--baseline-version 10.0.0 | --baseline-dir <dir with *.nupkg>)
Exit code 0 only when no package reports a difference. A baseline that is not on nuget.org yet is an error unless
--allow-missing-baseline (temporary, until the previous line is published); then only the packages whose baseline is missing are
skipped (with a warning), and every other package is still compared.

Reference assemblies: the framework reference pack (the newest by a real version comparison, GA before any preview of the same version)
and the DECLARED third-party dependencies of this repository (every `packages.lock.json` under src/, minus project references and
TabTabGo.* packages), never the whole NuGet cache.

Order of releases: the baseline version is the previous line's published version (publish.yml names it, currently 10.0.0). A v11 tag cannot
be released before that version is on nuget.org: the fetch fails with a clear message then.
"""
from __future__ import annotations

import argparse
import http.client
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from typing import Callable

IDS = ("TabTabGo.Templating", "TabTabGo.Templating.Dotliquid", "TabTabGo.Templating.OpenXml")
BASE_ID = IDS[0]
TFM_PREFERENCE = ["net11.0", "net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"]
FETCH_ATTEMPTS = 3


def semver_key(version: str) -> tuple:
    """A sort key for a SemVer 2 version: numeric core parts, then a release sorts above any prerelease of the same core, prerelease identifiers numerically."""
    version = version.split("+", 1)[0]
    core, _, pre = version.partition("-")
    numbers = tuple(int(part) if part.isdigit() else 0 for part in core.split("."))
    numbers += (0,) * (4 - len(numbers))
    if not pre:
        return (numbers, 1, ())
    identifiers = tuple((0, int(part), "") if part.isdigit() else (1, 0, part) for part in pre.split("."))
    return (numbers, 0, identifiers)


def unpack(package: Path, tfm: str, target: Path) -> Path:
    with zipfile.ZipFile(package) as archive:
        for name in archive.namelist():
            if ".." in re.split(r"[/\\]", name):
                continue  # a package entry that climbs out of the target is never extracted (a published package is trusted, a damaged or planted one is not)
            if name.startswith(f"lib/{tfm}/") and name.endswith(".dll"):
                archive.extract(name, target)
    return target / "lib" / tfm


def fetch_baseline(package_id: str, version: str, target: Path, sleep: Callable[[float], None] = time.sleep) -> Path | None:
    """Downloads a published package; None when it is not on nuget.org (404). Other errors are retried (3 tries, 1 s then 2 s apart; a cut connection (IncompleteRead) counts) and then reported plainly."""
    url = f"https://api.nuget.org/v3-flatcontainer/{package_id.lower()}/{version}/{package_id.lower()}.{version}.nupkg"
    destination = target / f"{package_id}.{version}.nupkg"
    last: Exception | None = None
    for attempt in range(1, FETCH_ATTEMPTS + 1):
        try:
            urllib.request.urlretrieve(url, destination)  # noqa: S310 (fixed https host)
            return destination
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            last = error
        except (urllib.error.URLError, http.client.HTTPException, OSError, EOFError) as error:
            # http.client.IncompleteRead (a connection cut mid-body) is an HTTPException, not an OSError; retried like any other transient download failure
            last = error
        if attempt < FETCH_ATTEMPTS:
            print(f"::warning::nuget.org answered {last} for {package_id} {version}; retrying ({attempt}/{FETCH_ATTEMPTS - 1})")
            sleep(2 ** (attempt - 1))
    raise SystemExit(f"could not download {package_id} {version} from nuget.org after {FETCH_ATTEMPTS} tries ({last}); this is not a missing baseline, so --allow-missing-baseline does not apply")


def framework_reference(tfm: str, roots: list[str] | None = None) -> str:
    """The reference assemblies of the newest installed pack for the target framework's major version (compared as versions, not as text)."""
    major = re.fullmatch(r"net(\d+)\.0", tfm).group(1)
    candidates_roots = roots if roots is not None else [os.environ.get("DOTNET_ROOT", ""), "/usr/local/share/dotnet", "/usr/share/dotnet", str(Path.home() / ".dotnet")]
    best: tuple | None = None
    best_path = ""
    for root in candidates_roots:
        packs = Path(root) / "packs" / "Microsoft.NETCore.App.Ref"
        if not root or not packs.is_dir():
            continue
        for pack in packs.iterdir():
            reference = pack / "ref" / tfm
            if not pack.is_dir() or not re.match(rf"{major}\.", pack.name) or not reference.is_dir():
                continue
            key = semver_key(pack.name)
            if best is None or key > best:
                best, best_path = key, str(reference)
    if best is None:
        raise SystemExit(f"no Microsoft.NETCore.App.Ref pack for {tfm}")
    return best_path


def preference(tfm: str) -> list[str]:
    """The target frameworks to look for in a package, best first: the given one, then every older one (never a newer one: its assemblies may not load on the pack)."""
    return TFM_PREFERENCE[TFM_PREFERENCE.index(tfm):] if tfm in TFM_PREFERENCE else [tfm] + TFM_PREFERENCE


def declared_dependencies(source_root: Path) -> dict[str, str]:
    """Package id -> resolved version, from every packages.lock.json under src/: direct and transitive third-party packages only (no projects, no TabTabGo.*)."""
    result: dict[str, str] = {}
    for lock in sorted((source_root / "src").glob("*/packages.lock.json")):
        document = json.loads(lock.read_text(encoding="utf-8-sig"))
        for dependencies in document.get("dependencies", {}).values():
            for package_id, entry in dependencies.items():
                if entry.get("type") == "Project" or package_id.lower().startswith("tabtabgo.") or "resolved" not in entry:
                    continue
                if package_id not in result or semver_key(entry["resolved"]) > semver_key(result[package_id]):
                    result[package_id] = entry["resolved"]
    return result


def package_references(dependencies: dict[str, str], tfms: list[str], cache: Path | None = None) -> list[str]:
    """The lib folder of each declared dependency in the NuGet cache, for the first target framework in `tfms` that the package has."""
    root = cache or Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget" / "packages"))
    result: list[str] = []
    for package_id, version in sorted(dependencies.items()):
        folder = root / package_id.lower() / version / "lib"
        if not folder.is_dir():
            continue
        match = next((folder / t for t in tfms if (folder / t).is_dir()), None)
        if match is not None:
            result.append(str(match))
    return result


def collect_baselines(fetch: Callable[[str], Path | None], allow_missing: bool) -> tuple[dict[str, Path], list[str]]:
    """The baseline package of each id and the ids that have none; without allow_missing the first missing baseline is an error for the caller."""
    found: dict[str, Path] = {}
    missing: list[str] = []
    for package_id in IDS:
        baseline = fetch(package_id)
        if baseline is None:
            missing.append(package_id)
            print(f"baseline of {package_id} is not available")
        else:
            found[package_id] = baseline
    if missing and not allow_missing:
        return found, missing
    if BASE_ID in missing:
        # the other packages are compared against the base package's baseline assemblies: without it they cannot be resolved, so they are skipped too
        for package_id in IDS:
            if package_id != BASE_ID and package_id in found:
                missing.append(package_id)
                del found[package_id]
    return found, missing


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("packed")
    parser.add_argument("left_tfm")
    parser.add_argument("right_tfm")
    parser.add_argument("--baseline-version")
    parser.add_argument("--baseline-dir")
    parser.add_argument("--allow-missing-baseline", action="store_true")
    args = parser.parse_args(argv)

    with tempfile.TemporaryDirectory() as temp:
        work = Path(temp)

        def fetch(package_id: str) -> Path | None:
            if args.baseline_dir:
                matches = sorted(Path(args.baseline_dir).glob(f"{package_id}.[0-9]*.nupkg"), key=lambda p: semver_key(p.name[len(package_id) + 1:-len(".nupkg")]))
                return matches[-1] if matches else None
            return fetch_baseline(package_id, args.baseline_version, work)

        baselines, missing = collect_baselines(fetch, args.allow_missing_baseline)
        if missing and not args.allow_missing_baseline:
            return 1
        for package_id in missing:
            print(f"::warning::ApiCompat skipped for {package_id}: its baseline is not published yet")
        if not baselines:
            return 0

        left_dirs = {package_id: unpack(path, args.left_tfm, work / "left" / package_id) for package_id, path in baselines.items()}
        right_dirs: dict[str, Path] = {}
        for package_id in baselines:
            right = next(Path(args.packed).glob(f"{package_id}.[0-9]*.nupkg"))
            right_dirs[package_id] = unpack(right, args.right_tfm, work / "right" / package_id)

        right_base = unpack(next(Path(args.packed).glob(f"{BASE_ID}.[0-9]*.nupkg")), args.right_tfm, work / "right" / BASE_ID)
        dependencies = declared_dependencies(Path(__file__).resolve().parents[2])
        left_refs = [framework_reference(args.left_tfm), str(left_dirs.get(BASE_ID, ""))] + package_references(dependencies, preference(args.left_tfm))
        right_refs = [framework_reference(args.right_tfm), str(right_base)] + package_references(dependencies, preference(args.right_tfm))
        failures = 0
        for package_id in baselines:
            command = ["dotnet", "tool", "run", "apicompat", "-l", str(left_dirs[package_id] / f"{package_id}.dll"), "-r", str(right_dirs[package_id] / f"{package_id}.dll"),
                       "--lref", ",".join(r for r in left_refs if r), "--rref", ",".join(right_refs), "--enable-rule-cannot-change-parameter-name"]
            process = subprocess.run(command, capture_output=True, text=True)
            print(f"== {package_id}: exit {process.returncode}")
            if process.returncode != 0:
                print((process.stdout + process.stderr)[-3000:])
                failures += 1
        return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
