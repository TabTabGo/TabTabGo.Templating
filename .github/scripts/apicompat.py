#!/usr/bin/env python3
"""ApiCompat in assembly mode: the packed assemblies of this line against the previous line's published assemblies.

Package validation cannot compare across target frameworks (a dropped TFM is PKV006 and no API comparison), so the surface check
runs here (spec 12.5 V11-5). The tool comes from .config/dotnet-tools.json (exact version): dotnet tool restore. The packages are only read.

Usage: apicompat.py <packed-dir> <left-tfm> <right-tfm> (--baseline-version 10.0.0 | --baseline-dir <dir with *.nupkg>)
Exit code 0 only when no package reports a difference. A baseline that is not on nuget.org yet is an error unless
--allow-missing-baseline (temporary, until the previous line is published).
"""
from __future__ import annotations

import argparse
import glob
import os
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

IDS = ("TabTabGo.Templating", "TabTabGo.Templating.Dotliquid", "TabTabGo.Templating.OpenXml")


def unpack(package: Path, tfm: str, target: Path) -> Path:
    with zipfile.ZipFile(package) as archive:
        for name in archive.namelist():
            if name.startswith(f"lib/{tfm}/") and name.endswith(".dll"):
                archive.extract(name, target)
    return target / "lib" / tfm


def fetch_baseline(package_id: str, version: str, target: Path) -> Path | None:
    url = f"https://api.nuget.org/v3-flatcontainer/{package_id.lower()}/{version}/{package_id.lower()}.{version}.nupkg"
    destination = target / f"{package_id}.{version}.nupkg"
    try:
        urllib.request.urlretrieve(url, destination)  # noqa: S310 (fixed https host)
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise
    return destination


def framework_reference(tfm: str) -> str:
    major = re.fullmatch(r"net(\d+)\.0", tfm).group(1)
    roots = [os.environ.get("DOTNET_ROOT", ""), "/usr/local/share/dotnet", "/usr/share/dotnet", str(Path.home() / ".dotnet")]
    for root in roots:
        found = sorted(glob.glob(f"{root}/packs/Microsoft.NETCore.App.Ref/{major}.*/ref/{tfm}"))
        if found:
            return found[-1]
    raise SystemExit(f"no Microsoft.NETCore.App.Ref pack for {tfm}")


def package_references(tfms: list[str]) -> list[str]:
    """Dependency assemblies from the NuGet cache: the newest version of each package that has one of the target frameworks."""
    cache = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget" / "packages"))
    result: list[str] = []
    for package in sorted(cache.iterdir()):
        versions = sorted((v for v in package.iterdir() if v.is_dir()), key=lambda v: [int(x) if x.isdigit() else 0 for x in re.split(r"[.\-]", v.name)], reverse=True)
        for version in versions:
            match = next((version / "lib" / t for t in tfms if (version / "lib" / t).is_dir()), None)
            if match:
                result.append(str(match))
                break
    return result


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
        failures = 0
        left_dirs: dict[str, Path] = {}
        right_dirs: dict[str, Path] = {}
        for package_id in IDS:
            if args.baseline_dir:
                matches = sorted(Path(args.baseline_dir).glob(f"{package_id}.[0-9]*.nupkg"))
                baseline = matches[-1] if matches else None
            else:
                baseline = fetch_baseline(package_id, args.baseline_version, work)
            if baseline is None:
                print(f"baseline of {package_id} is not available")
                if args.allow_missing_baseline:
                    print("::warning::ApiCompat skipped: the baseline is not published yet")
                    return 0
                return 1
            left_dirs[package_id] = unpack(baseline, args.left_tfm, work / "left" / package_id)
            right = next(Path(args.packed).glob(f"{package_id}.[0-9]*.nupkg"))
            right_dirs[package_id] = unpack(right, args.right_tfm, work / "right" / package_id)

        left_refs = [framework_reference(args.left_tfm), str(left_dirs["TabTabGo.Templating"])] + package_references(["net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"])
        right_refs = [framework_reference(args.right_tfm), str(right_dirs["TabTabGo.Templating"])] + package_references([args.right_tfm, "net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"])
        for package_id in IDS:
            command = ["dotnet", "tool", "run", "apicompat", "-l", str(left_dirs[package_id] / f"{package_id}.dll"), "-r", str(right_dirs[package_id] / f"{package_id}.dll"),
                       "--lref", ",".join(left_refs), "--rref", ",".join(right_refs), "--enable-rule-cannot-change-parameter-name"]
            process = subprocess.run(command, capture_output=True, text=True)
            print(f"== {package_id}: exit {process.returncode}")
            if process.returncode != 0:
                print((process.stdout + process.stderr)[-3000:])
                failures += 1
        return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
