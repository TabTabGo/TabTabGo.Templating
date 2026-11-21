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


def audit_problems(report) -> list[str]:
    """Reasons the report cannot be trusted as a clean audit (fail closed): a failed audit reports `problems`, or no projects at all.

    A project with no vulnerable package legitimately has no `frameworks` entry (verified against `dotnet list package --vulnerable`),
    so a missing `frameworks` is accepted; an explicitly empty one is not.
    """
    if not isinstance(report, dict):
        return ["the report is not a JSON object"]
    reasons: list[str] = []
    problems = report.get("problems")
    if problems:
        for problem in problems if isinstance(problems, list) else [problems]:
            text = problem.get("text", problem) if isinstance(problem, dict) else problem
            reasons.append(f"the audit reported a problem: {text}")
    projects = report.get("projects")
    if not isinstance(projects, list) or not projects:
        reasons.append("the report lists no projects")
    else:
        for project in projects:
            if not isinstance(project, dict):
                reasons.append("a project entry is not an object")
            elif "frameworks" in project and not project["frameworks"]:
                reasons.append(f"project {project.get('path', '?')} has an empty frameworks list")
    if not report.get("sources"):
        reasons.append("the report names no package sources")
    return reasons


def main() -> int:
    raw = sys.stdin.read()
    start = raw.find("{")
    if start < 0:
        print("::error::no JSON in `dotnet list package` output", file=sys.stderr)
        return 2
    try:
        report = json.loads(raw[start:])
    except json.JSONDecodeError as error:
        print(f"::error::unreadable `dotnet list package` output: {error}", file=sys.stderr)
        return 2
    reasons = audit_problems(report)
    if reasons:
        for reason in reasons:
            print(f"::error::vulnerability audit incomplete: {reason}", file=sys.stderr)
        return 1
    findings = find_vulnerabilities(report)
    if findings:
        for finding in sorted(set(findings)):
            print(f"::error::vulnerable package {finding}", file=sys.stderr)
        return 1
    print("no vulnerable packages")
    return 0


if __name__ == "__main__":
    sys.exit(main())
