#!/usr/bin/env python3
"""Fails when `dotnet list package --vulnerable --include-transitive --format json` reports a vulnerability.

Usage: dotnet list <sln> package --vulnerable --include-transitive --format json | vuln_gate.py
"""
from __future__ import annotations

import json
import sys


def find_vulnerabilities(node, path: str = "") -> list[str]:
    """Walks the report and returns one line per vulnerable package."""
    findings: list[str] = []
    if isinstance(node, dict):
        vulnerabilities = node.get("vulnerabilities")
        if vulnerabilities:
            advisories = ", ".join(v.get("advisoryurl", "?") + " (" + v.get("severity", "?") + ")" for v in vulnerabilities)
            findings.append(f"{node.get('id', '?')} {node.get('resolvedVersion', '?')}: {advisories}")
        for value in node.values():
            findings.extend(find_vulnerabilities(value, path))
    elif isinstance(node, list):
        for item in node:
            findings.extend(find_vulnerabilities(item, path))
    return findings


def main() -> int:
    raw = sys.stdin.read()
    start = raw.find("{")
    if start < 0:
        print("::error::no JSON in `dotnet list package` output", file=sys.stderr)
        return 2
    report = json.loads(raw[start:])
    findings = find_vulnerabilities(report)
    if findings:
        for finding in sorted(set(findings)):
            print(f"::error::vulnerable package {finding}", file=sys.stderr)
        return 1
    print("no vulnerable packages")
    return 0


if __name__ == "__main__":
    sys.exit(main())
