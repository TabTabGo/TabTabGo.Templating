#!/usr/bin/env python3
"""Pack and push the TabTabGo.Templating NuGet packages.

Version scheme (same as TabTabGo.Messaging/tools/packnpush.sh):

    <major>.<minor>.<yyMM>.<dd><patch>

where `patch` is the CI build number (GitHub Actions `github.run_number`).
Example: major=8, minor=0, 2026-08-11, run 123  ->  8.0.2608.1100123

Two deviations from the shell script, both deliberate:

* The run number is zero padded (`--pad`, default 5) so the revision keeps
  increasing when the run number gains a digit -- with the raw scheme,
  day 09 + run 500 gives 09500 while day 10 + run 1 gives 101, which NuGet
  reads as a LOWER version. Pass `--pad 0` for parity with packnpush.sh.
* major/minor default to the highest `<Version>` found in the csproj files
  (8.0 today) instead of being hardcoded, so a manual bump to 9.0 in the
  csproj carries into the published packages automatically.

The version is applied with `-p:PackageVersion=`, which overrides the NuGet
package version only -- AssemblyVersion stays as declared in the csproj,
since its components are capped at 65534 and the revision here exceeds that.

Usage:
    python3 tools/packnpush.py -p 123 -k $NUGET_API_KEY
    python3 tools/packnpush.py -p 1 --local          # pack only, no push
    python3 tools/packnpush.py -p 1 --print-version  # show the version only
"""

import argparse
import datetime
import os
import re
import shutil
import subprocess
import sys

NUGET_SOURCE = "https://api.nuget.org/v3/index.json"

FALLBACK_MAJOR = 8
FALLBACK_MINOR = 0

# (csproj path relative to repo root, package id)
PROJECTS = [
    ("src/TabTabGo.Templating/TabTabGo.Templating.csproj", "TabTabGo.Templating"),
    ("src/TabTabGo.Templating.Dotliquid/TabTabGo.Templating.Dotliquid.csproj", "TabTabGo.Templating.Dotliquid"),
    ("src/TabTabGo.Templating.OpenXml/TabTabGo.Templating.OpenXml.csproj", "TabTabGo.Templating.OpenXml"),
]

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

VERSION_RE = re.compile(r"<Version>\s*(\d+)\.(\d+)", re.IGNORECASE)


def run(cmd, secret=None):
    """Run a command, echoing it with any secret masked. Raises on failure."""
    printable = [("***" if secret and arg == secret else arg) for arg in cmd]
    print(f"$ {' '.join(printable)}", flush=True)
    result = subprocess.run(cmd, cwd=REPO_ROOT)
    if result.returncode != 0:
        raise SystemExit(f"command failed with exit code {result.returncode}: {' '.join(printable)}")


def detect_major_minor():
    """Highest major.minor declared across the packable csproj files."""
    found = []
    for csproj, _ in PROJECTS:
        path = os.path.join(REPO_ROOT, csproj)
        try:
            with open(path, encoding="utf-8-sig") as handle:
                match = VERSION_RE.search(handle.read())
        except OSError:
            continue
        if match:
            found.append((int(match.group(1)), int(match.group(2))))
    return max(found) if found else (FALLBACK_MAJOR, FALLBACK_MINOR)


def compute_version(args):
    if args.version:
        return args.version

    major, minor = detect_major_minor()
    if args.major is not None:
        major = args.major
    if args.minor is not None:
        minor = args.minor

    stamp = args.date or datetime.date.today()
    patch = str(args.patch)
    if args.pad:
        patch = patch.zfill(args.pad)
    return f"{major}.{minor}.{stamp:%y%m}.{stamp:%d}{patch}"


def pack_and_push(csproj, package_id, version, output_dir, nuget_key, local):
    print(f"\n=== {package_id} {version} ===", flush=True)

    run(["dotnet", "build", csproj, "-c", "Release", f"-p:InformationalVersion={version}"])

    # PackageVersion (not Version): AssemblyVersion components are capped at
    # 65534, which the <dd><patch> revision blows past. The assembly keeps the
    # version declared in the csproj; only the NuGet package carries this one.
    run([
        "dotnet", "pack", csproj,
        "-o", output_dir,
        "-c", "Release",
        f"-p:PackageVersion={version}",
        f"-p:InformationalVersion={version}",
        "--include-symbols",
        "--include-source",
    ])

    package = os.path.join(output_dir, f"{package_id}.{version}.nupkg")
    if not os.path.isfile(os.path.join(REPO_ROOT, package)):
        raise SystemExit(f"expected package not produced: {package}")

    if local:
        print(f"--local: skipping push of {package}", flush=True)
        return

    run([
        "dotnet", "nuget", "push", package,
        "-k", nuget_key,
        "-s", NUGET_SOURCE,
        "--skip-duplicate",
    ], secret=nuget_key)


def parse_args(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("-p", "--patch", default="0", help="CI build number (github.run_number)")
    parser.add_argument("-k", "--nuget-key", default=os.environ.get("NUGET_API_KEY", ""), help="NuGet API key (defaults to $NUGET_API_KEY)")
    parser.add_argument("-l", "--local", action="store_true", help="pack only, do not push to NuGet")
    parser.add_argument("-o", "--output", default="./packages", help="output directory for the .nupkg files")
    parser.add_argument("--major", type=int, help="override the major version (default: highest in the csproj files)")
    parser.add_argument("--minor", type=int, help="override the minor version (default: highest in the csproj files)")
    parser.add_argument("--pad", type=int, default=5, help="zero-pad the build number to N digits (0 disables, matching packnpush.sh)")
    parser.add_argument("--version", help="use this exact version instead of computing one")
    parser.add_argument("--date", type=datetime.date.fromisoformat, help="override the build date (YYYY-MM-DD), for testing")
    parser.add_argument("--print-version", action="store_true", help="print the computed version and exit")
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    version = compute_version(args)

    if args.print_version:
        print(version)
        return 0

    if not args.local and not args.nuget_key:
        raise SystemExit("a NuGet API key is required to push (-k / $NUGET_API_KEY); use --local to pack only")

    print(f"Current version {version}")
    print("Start Build, Pack and Push")

    output_dir = args.output
    shutil.rmtree(os.path.join(REPO_ROOT, output_dir), ignore_errors=True)

    for csproj, package_id in PROJECTS:
        pack_and_push(csproj, package_id, version, output_dir, args.nuget_key, args.local)

    print("\nCompleted Build, Pack and Push")
    return 0


if __name__ == "__main__":
    sys.exit(main())
