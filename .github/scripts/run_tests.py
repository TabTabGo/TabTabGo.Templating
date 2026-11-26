#!/usr/bin/env python3
"""Runs every test project with coverage and applies the coverage gate (spec section 11.2).

Usage: run_tests.py [--configuration Release] [--no-build]

The test projects run on Microsoft Testing Platform (xunit.v3 + coverlet.MTP, ADR 0005). Each one writes a cobertura report below
``coverage/<project>``; ``coverage_gate.py`` merges them and fails when a src package is under 85 % lines or 75 % branches.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = (
    "TabTabGo.Templating.Tests",
    "TabTabGo.Templating.DotLiquid.Tests",
    "TabTabGo.Templating.OpenXml.Tests",
)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args(argv)

    shutil.rmtree(ROOT / "coverage", ignore_errors=True)  # coverlet.MTP refuses a results folder that already holds a report
    reports: list[str] = []
    for project in PROJECTS:
        results = ROOT / "coverage" / project
        command = [
            "dotnet", "test", "--project", f"test/{project}", "-c", args.configuration,
            *(["--no-build"] if args.no_build else []),
            "--", "--coverlet",
            "--coverlet-include", "[TabTabGo.Templating*]*",
            "--coverlet-exclude", "[*.Tests]*",
            "--coverlet-exclude", "[*TestKit]*",
            "--coverlet-output-format", "cobertura",
            "--results-directory", str(results),
        ]
        print("+", " ".join(command), flush=True)
        if subprocess.run(command, cwd=ROOT).returncode != 0:
            print(f"::error::tests failed in {project}", file=sys.stderr)
            return 1
        reports += [str(p) for p in results.rglob("*.cobertura.xml")] or [str(p) for p in results.rglob("*.xml")]

    gate = [sys.executable, str(ROOT / ".github/scripts/coverage_gate.py"), *reports]
    print("+", " ".join(gate), flush=True)
    return subprocess.run(gate, cwd=ROOT).returncode


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
