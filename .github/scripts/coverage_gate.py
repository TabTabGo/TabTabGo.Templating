#!/usr/bin/env python3
"""Coverage gate (spec section 11.2): line >= 85 % and branch >= 75 % for every src package.

Usage: coverage_gate.py <report.cobertura.xml> [...] [--line 85] [--branch 75]

The three test projects each exercise several packages, so the reports are merged before the thresholds are checked: a line counts as
covered when any report hit it; for a line with branches the report with the most covered branches wins. Exit code 1 when a package is below a
threshold, with one line per package on stdout.
"""
from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict


def load(path: str, merged: dict) -> None:
    root = ET.parse(path).getroot()
    for package in root.iter("package"):
        name = package.get("name", "")
        for cls in package.iter("class"):
            # reports from different runs may spell the same file with different roots: key on the file name (names are unique inside a package)
            filename = "/".join(cls.get("filename", "").replace("\\", "/").split("/")[-1:])
            for line in cls.iter("line"):
                key = (name, filename, int(line.get("number", "0")))
                hits = int(line.get("hits", "0"))
                covered, total = 0, 0
                condition = line.get("condition-coverage")
                if line.get("branch") == "True" and condition:
                    match = re.search(r"\((\d+)/(\d+)\)", condition)
                    if match:
                        covered, total = int(match.group(1)), int(match.group(2))
                previous = merged.get(key, (0, 0, 0))
                merged[key] = (max(previous[0], hits), max(previous[1], covered), max(previous[2], total))


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("reports", nargs="+")
    parser.add_argument("--line", type=float, default=85.0)
    parser.add_argument("--branch", type=float, default=75.0)
    args = parser.parse_args(argv)

    merged: dict = {}
    for report in args.reports:
        load(report, merged)

    lines = defaultdict(lambda: [0, 0])
    branches = defaultdict(lambda: [0, 0])
    for (package, _file, _number), (hits, covered, total) in merged.items():
        lines[package][1] += 1
        lines[package][0] += 1 if hits > 0 else 0
        branches[package][0] += covered
        branches[package][1] += total

    failed = False
    for package in sorted(lines):
        line_pct = 100.0 * lines[package][0] / lines[package][1] if lines[package][1] else 100.0
        branch_pct = 100.0 * branches[package][0] / branches[package][1] if branches[package][1] else 100.0
        ok = line_pct >= args.line and branch_pct >= args.branch
        failed |= not ok
        print(f"{'OK  ' if ok else 'FAIL'} {package}: line {line_pct:.1f}% ({lines[package][0]}/{lines[package][1]}), "
              f"branch {branch_pct:.1f}% ({branches[package][0]}/{branches[package][1]})")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
