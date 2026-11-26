#!/usr/bin/env python3
"""Release guards for publish.yml (spec section 8.2, steps 2-6).

Every guard exits non-zero with a clear message. Run from the repository root:

    release_guards.py <tag> <commit-sha> [--offline]

``--offline`` skips the nuget.org "already published" query (used by the self tests and local dry runs).
On success it prints ``version=...`` and ``prerelease=...`` lines suitable for ``$GITHUB_OUTPUT``.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

PACKAGE_IDS = ("TabTabGo.Templating", "TabTabGo.Templating.Dotliquid", "TabTabGo.Templating.OpenXml")
_NUMBER = r"(?:0|[1-9][0-9]*)"  # no leading zeros
TAG_VERSION = re.compile(
    rf"(?P<core>(?P<major>{_NUMBER})\.(?P<minor>{_NUMBER})\.(?P<patch>{_NUMBER}))(?P<pre>-(?:rc|ci|alpha|beta)\.{_NUMBER})?",
    re.ASCII,
)
TFM_MAJOR = re.compile(r"net(?P<major>[0-9]+)\.0", re.ASCII)


class GuardError(Exception):
    """A release guard failed."""


def parse_tag(tag: str) -> dict[str, str | bool]:
    """Step 2: version from the tag; must be a stable or rc/ci/alpha/beta pre-release version."""
    if not tag.startswith("v"):
        raise GuardError(f"tag '{tag}' does not start with 'v'")
    version = tag[1:]
    match = TAG_VERSION.fullmatch(version)
    if not match:
        raise GuardError(f"tag '{tag}' is not vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-(rc|ci|alpha|beta).N")
    return {
        "version": version,
        "core": match.group("core"),
        "major": match.group("major"),
        "minor": match.group("minor"),
        "patch": match.group("patch"),
        "prerelease": match.group("pre") is not None,
    }


def msbuild_property(path: Path, name: str) -> list[str]:
    """Values of the MSBuild property `name` declared in a PropertyGroup of `path`.

    The file is parsed as XML, so a commented-out `<Version>` or a text match inside another element is never read.
    """
    try:
        tree = ET.parse(path)
    except ET.ParseError as error:
        raise GuardError(f"{path.name} is not well-formed XML: {error}") from error
    values: list[str] = []
    for group in tree.getroot().iter():
        if group.tag.rpartition("}")[2] != "PropertyGroup":
            continue
        for element in group:
            if element.tag.rpartition("}")[2] == name and element.text and element.text.strip():
                values.append(element.text.strip())
    return values


def target_frameworks(root: Path) -> set[str]:
    """Reads <TargetFramework> from Directory.Build.props, else from every src csproj."""
    props = root / "Directory.Build.props"
    if props.exists():
        found = msbuild_property(props, "TargetFramework")
        if found:
            return set(found)
    frameworks: set[str] = set()
    for csproj in sorted((root / "src").glob("*/*.csproj")):
        frameworks.update(msbuild_property(csproj, "TargetFramework"))
    return frameworks


def check_tfm_major(root: Path, tag_major: str) -> None:
    """Step 3: tag major == TFM major."""
    frameworks = target_frameworks(root)
    if not frameworks:
        raise GuardError("no <TargetFramework> found in Directory.Build.props or src/*/*.csproj")
    for framework in frameworks:
        match = TFM_MAJOR.fullmatch(framework)
        if not match:
            raise GuardError(f"unsupported target framework '{framework}'")
        if match.group("major") != tag_major:
            raise GuardError(f"tag major {tag_major} does not match target framework {framework} of this commit")


def check_tag_points_at_sha(tag: str, sha: str) -> None:
    """The tag ref (not a branch of the same name) must resolve to the commit the run was triggered for."""
    result = subprocess.run(
        ["git", "rev-parse", f"refs/tags/{tag}^{{commit}}"], capture_output=True, text=True
    )
    if result.returncode != 0:
        raise GuardError(f"tag refs/tags/{tag} does not exist in this checkout")
    resolved = result.stdout.strip()
    expected = subprocess.run(["git", "rev-parse", f"{sha}^{{commit}}"], capture_output=True, text=True).stdout.strip()
    if resolved != expected:
        raise GuardError(f"refs/tags/{tag} resolves to {resolved}, not the triggering commit {expected}")


def check_release_branch(sha: str, core: dict[str, str | bool]) -> None:
    """Step 4: the tagged commit is on origin/releases/vM.m.x/vM.m.p."""
    branch = f"origin/releases/v{core['major']}.{core['minor']}.x/v{core['core']}"
    result = subprocess.run(
        ["git", "branch", "-r", "--contains", sha], check=True, capture_output=True, text=True
    )
    branches = {line.strip() for line in result.stdout.splitlines()}
    if branch not in branches:
        raise GuardError(f"commit {sha} is not on release branch {branch}")


def declared_version(root: Path) -> str:
    props = root / "Directory.Build.props"
    if not props.exists():
        raise GuardError("Directory.Build.props not found")
    versions = msbuild_property(props, "Version")
    if not versions:
        raise GuardError("no <Version> in Directory.Build.props")
    return versions[0]


def check_declared_version(root: Path, core: str) -> None:
    """Step 5: <Version> in Directory.Build.props equals the tag without its pre-release suffix."""
    declared = declared_version(root)
    if declared != core:
        raise GuardError(f"Directory.Build.props declares Version {declared} but the tag is for {core}")


_PROPERTY_REFERENCE = re.compile(r"\$\(([A-Za-z_][A-Za-z0-9_.\-]*)\)")
_SEMVER_LABEL = re.compile(r"[0-9]-[0-9A-Za-z]")  # any SemVer pre-release label (-rc, -preview, -local, -anything), whatever its case
MAX_RESOLVED_LENGTH = 4096
MAX_CANDIDATES = 256
PropertyValues = dict[str, list[str]]


def _local_name(element: ET.Element) -> str:
    return element.tag.rpartition("}")[2]


def collect_properties(paths: list[Path]) -> PropertyValues:
    """Every value any PropertyGroup of the given files declares for each property, in order.

    MSBuild keeps the last declaration that applies and a Condition decides what applies, which a text check cannot know; so every declared value counts
    (a later or conditional -rc or -local value must not slip through). Unreadable files are skipped here and reported by the caller."""
    properties: PropertyValues = {}
    for path in paths:
        try:
            root = ET.parse(path).getroot()
        except (ET.ParseError, OSError):
            continue
        for group in root.iter():
            if _local_name(group) != "PropertyGroup":
                continue
            for element in group:
                if element.text and element.text.strip():
                    properties.setdefault(_local_name(element), []).append(element.text.strip())
    return properties


def expand_properties(value: str, properties: PropertyValues, depth: int = 8) -> list[str]:
    """Every string `value` can resolve to when each $(Name) takes each of its declared values (repeatedly, so a property may name another).

    An undeclared name, a cycle, or a result past MAX_RESOLVED_LENGTH / MAX_CANDIDATES leaves a $( in the string, which a stable tag treats as a problem."""
    results = [value]
    for _ in range(depth):
        following: list[str] = []
        changed = False
        for candidate in results:
            reference = next((m for m in _PROPERTY_REFERENCE.finditer(candidate) if m.group(1) in properties), None)
            if reference is None:
                following.append(candidate)
                continue
            changed = True
            for declared in properties[reference.group(1)]:
                expanded = candidate[: reference.start()] + declared + candidate[reference.end():]
                following.append(expanded if len(expanded) <= MAX_RESOLVED_LENGTH else candidate)
        results = list(dict.fromkeys(following))[:MAX_CANDIDATES]
        if not changed:
            break
    return results


def resolved_versions(path: Path, properties: PropertyValues | None = None) -> list[str]:
    """The versions a build can resolve from `path`: Version and VersionOverride attributes (either quote style) and Version, VersionOverride and PackageVersion elements, with
    $(Property) references expanded over every declared value. Comments, descriptions and other text are not versions and are never read (the file is parsed as XML)."""
    properties = properties or {}
    versions: list[str] = []
    for element in ET.parse(path).getroot().iter():
        for name in ("Version", "VersionOverride"):
            if name in element.attrib:
                versions.extend(expand_properties(element.attrib[name], properties))
        if _local_name(element) in ("Version", "VersionOverride", "PackageVersion") and element.text and element.text.strip():
            versions.extend(expand_properties(element.text.strip(), properties))
    return versions


def references_local_build(path: Path, properties: PropertyValues | None = None) -> bool:
    """True when a version a build can resolve (a Version, VersionOverride or Include attribute, or a Version element, after property expansion) names a -local build; descriptions and comments do not count."""
    properties = properties or {}
    if any("-local" in version.lower() for version in resolved_versions(path, properties)):
        return True
    for element in ET.parse(path).getroot().iter():
        for name in ("Include", "Update"):
            if any("-local" in value.lower() for value in expand_properties(element.attrib.get(name, ""), properties)):
                return True
    return False


def check_pins_match_release_kind(root: Path, prerelease: bool) -> None:
    """A stable tag may not ship with pre-release SDK settings or pre-release or -local package pins (v11 stays an rc until GA)."""
    problems: list[str] = []
    global_json = root / "global.json"
    if not prerelease and global_json.exists() and re.search(r'"allowPrerelease"\s*:\s*true', global_json.read_text(encoding="utf-8-sig")):
        problems.append("global.json allows a pre-release SDK")
    paths = [path for path in [root / "Directory.Packages.props", root / "Directory.Build.props", *sorted((root / "src").glob("*/*.csproj"))] if path.exists()]
    properties = collect_properties(paths)
    for path in paths:
        try:
            if references_local_build(path, properties):
                problems.append(f"{path.name} references a -local build")
            versions = resolved_versions(path, properties)
            if not prerelease and any(_SEMVER_LABEL.search(version) for version in versions):
                problems.append(f"{path.name} pins a pre-release package")
            if not prerelease and any("$(" in version for version in versions):
                problems.append(f"{path.name} has a version that does not resolve to a declared value (an unknown, cyclic or oversized property)")
        except ET.ParseError:
            problems.append(f"{path.name} is not well-formed XML")
    if problems:
        raise GuardError("; ".join(problems) + (" (a stable tag needs stable pins; tag an -rc instead)" if not prerelease else ""))


def published_versions(package_id: str) -> set[str]:
    url = f"https://api.nuget.org/v3-flatcontainer/{package_id.lower()}/index.json"
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            return {v.lower() for v in json.load(response)["versions"]}
    except urllib.error.HTTPError as error:
        if error.code == 404:  # package id not on nuget.org yet
            return set()
        raise


def check_not_published(version: str) -> None:
    """Step 6: refuse a version that already exists (replaces --skip-duplicate)."""
    for package_id in PACKAGE_IDS:
        if version.lower() in published_versions(package_id):
            raise GuardError(f"{package_id} {version} is already on nuget.org; bump the version and tag again")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("tag")
    parser.add_argument("sha")
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--root", default=".")
    args = parser.parse_args(argv)
    root = Path(args.root)
    try:
        info = parse_tag(args.tag)
        check_tfm_major(root, str(info["major"]))
        if not args.offline:
            check_tag_points_at_sha(args.tag, args.sha)
        check_release_branch(args.sha, info)
        check_declared_version(root, str(info["core"]))
        check_pins_match_release_kind(root, bool(info["prerelease"]))
        if not args.offline:
            check_not_published(str(info["version"]))
    except GuardError as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1
    print(f"version={info['version']}")
    print(f"prerelease={'true' if info['prerelease'] else 'false'}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
