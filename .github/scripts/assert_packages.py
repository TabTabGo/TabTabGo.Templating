#!/usr/bin/env python3
"""Asserts the pack output (spec section 8.1 step 7 and 8.2 step 9).

Usage: assert_packages.py <dir> <version> [--tfm net8.0]

Exactly the three packages with their symbol packages must exist; each nuspec must carry the version, and the two
engine packages must depend on TabTabGo.Templating at the same version. With --tfm every dependency group must target it.
"""
from __future__ import annotations

import argparse
import re
import sys
import zipfile
from pathlib import Path

IDS = ("TabTabGo.Templating", "TabTabGo.Templating.Dotliquid", "TabTabGo.Templating.OpenXml")


def nuspec_text(package: Path) -> str:
    with zipfile.ZipFile(package) as archive:
        name = next(n for n in archive.namelist() if n.endswith(".nuspec"))
        return archive.read(name).decode("utf-8-sig")


def check(directory: Path, version: str, tfm: str | None) -> list[str]:
    errors: list[str] = []
    expected = {f"{package_id}.{version}.{ext}" for package_id in IDS for ext in ("nupkg", "snupkg")}
    actual = {p.name for p in directory.iterdir() if p.is_file()}
    if actual != expected:
        errors.append(f"unexpected package set. missing={sorted(expected - actual)} extra={sorted(actual - expected)}")
        return errors
    for package_id in IDS:
        text = nuspec_text(directory / f"{package_id}.{version}.nupkg")
        if not re.search(rf"<version>{re.escape(version)}</version>", text):
            errors.append(f"{package_id}: nuspec version is not {version}")
        if tfm is not None:
            groups = re.findall(r'<group targetFramework="([^"]+)"', text)
            if not groups or set(groups) != {tfm}:
                errors.append(f"{package_id}: dependency groups {groups} are not exactly {tfm}")
        if package_id != "TabTabGo.Templating":
            dependency = re.search(r'<dependency id="TabTabGo\.Templating" version="([^"]+)"', text)
            minimum = dependency.group(1).lstrip("[").split(",")[0] if dependency else None
            if minimum != version:
                errors.append(f"{package_id}: depends on TabTabGo.Templating {minimum}, expected {version}")
    return errors


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("directory")
    parser.add_argument("version")
    parser.add_argument("--tfm")
    args = parser.parse_args(argv)
    errors = check(Path(args.directory), args.version, args.tfm)
    for error in errors:
        print(f"::error::{error}", file=sys.stderr)
    if not errors:
        print(f"package set OK for {args.version}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
