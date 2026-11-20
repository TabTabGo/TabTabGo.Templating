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
from pathlib import Path

PACKAGE_IDS = ("TabTabGo.Templating", "TabTabGo.Templating.Dotliquid", "TabTabGo.Templating.OpenXml")
TAG_VERSION = re.compile(r"^(?P<core>(?P<major>[0-9]+)\.(?P<minor>[0-9]+)\.(?P<patch>[0-9]+))(?P<pre>-(rc|ci|alpha|beta)\.[0-9]+)?$")
TFM_MAJOR = re.compile(r"^net(?P<major>[0-9]+)\.0$")


class GuardError(Exception):
    """A release guard failed."""


def parse_tag(tag: str) -> dict[str, str | bool]:
    """Step 2: version from the tag; must be a stable or rc/ci/alpha/beta pre-release version."""
    if not tag.startswith("v"):
        raise GuardError(f"tag '{tag}' does not start with 'v'")
    version = tag[1:]
    match = TAG_VERSION.match(version)
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


def target_frameworks(root: Path) -> set[str]:
    """Reads <TargetFramework> from Directory.Build.props, else from every src csproj."""
    pattern = re.compile(r"<TargetFramework>([^<]+)</TargetFramework>")
    props = root / "Directory.Build.props"
    if props.exists():
        found = pattern.findall(props.read_text(encoding="utf-8-sig"))
        if found:
            return set(found)
    frameworks: set[str] = set()
    for csproj in sorted((root / "src").glob("*/*.csproj")):
        frameworks.update(pattern.findall(csproj.read_text(encoding="utf-8-sig")))
    return frameworks


def check_tfm_major(root: Path, tag_major: str) -> None:
    """Step 3: tag major == TFM major."""
    frameworks = target_frameworks(root)
    if not frameworks:
        raise GuardError("no <TargetFramework> found in Directory.Build.props or src/*/*.csproj")
    for framework in frameworks:
        match = TFM_MAJOR.match(framework)
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
    match = re.search(r"<Version>([^<]+)</Version>", props.read_text(encoding="utf-8-sig"))
    if not match:
        raise GuardError("no <Version> in Directory.Build.props")
    return match.group(1).strip()


def check_declared_version(root: Path, core: str) -> None:
    """Step 5: <Version> in Directory.Build.props equals the tag without its pre-release suffix."""
    declared = declared_version(root)
    if declared != core:
        raise GuardError(f"Directory.Build.props declares Version {declared} but the tag is for {core}")


def check_pins_match_release_kind(root: Path, prerelease: bool) -> None:
    """A stable tag may not ship with pre-release SDK settings or pre-release or -local package pins (v11 stays an rc until GA)."""
    problems: list[str] = []
    global_json = root / "global.json"
    if not prerelease and global_json.exists() and re.search(r'"allowPrerelease"\s*:\s*true', global_json.read_text(encoding="utf-8-sig")):
        problems.append("global.json allows a pre-release SDK")
    for path in [root / "Directory.Packages.props", root / "Directory.Build.props", *sorted((root / "src").glob("*/*.csproj"))]:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig")
        if "-local" in text:
            problems.append(f"{path.name} references a -local build")
        if not prerelease and re.search(r'Version="[^"]*-(rc|preview|alpha|beta)[^"]*"', text):
            problems.append(f"{path.name} pins a pre-release package")
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
