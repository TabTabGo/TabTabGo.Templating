"""Self tests for the release scripts. Run: python3 -m unittest discover .github/scripts"""
import json
import re
import tempfile
import unittest
import zipfile
from pathlib import Path

import assert_packages
import release_guards
import vuln_gate


class ParseTagTests(unittest.TestCase):
    def test_stable_tag(self):
        info = release_guards.parse_tag("v8.0.3")
        self.assertEqual(("8.0.3", "8", False), (info["version"], info["major"], info["prerelease"]))

    def test_rc_tag_is_prerelease(self):
        info = release_guards.parse_tag("v10.0.0-rc.1")
        self.assertTrue(info["prerelease"])
        self.assertEqual("10.0.0", info["core"])

    def test_rejects_bad_tags(self):
        for tag in ("8.0.3", "v8.0", "v8.0.3-foo", "v8.0.3+meta", "v8.0.3-rc"):
            with self.subTest(tag=tag):
                with self.assertRaises(release_guards.GuardError):
                    release_guards.parse_tag(tag)


class TfmAndVersionTests(unittest.TestCase):
    def _repo(self, tfm="net8.0", version="8.0.3"):
        root = Path(tempfile.mkdtemp())
        (root / "src" / "A").mkdir(parents=True)
        (root / "src" / "A" / "A.csproj").write_text(f"<Project><PropertyGroup><TargetFramework>{tfm}</TargetFramework></PropertyGroup></Project>")
        (root / "Directory.Build.props").write_text(f"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>")
        return root

    def test_tag_major_must_match_tfm(self):
        root = self._repo()
        release_guards.check_tfm_major(root, "8")
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_tfm_major(root, "10")

    def test_declared_version_must_equal_tag(self):
        root = self._repo()
        release_guards.check_declared_version(root, "8.0.3")
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_declared_version(root, "8.0.4")


class PinGuardTests(unittest.TestCase):
    def _repo(self, packages="", global_json="{}"):
        root = Path(tempfile.mkdtemp())
        (root / "src").mkdir()
        (root / "Directory.Packages.props").write_text(f"<Project><ItemGroup>{packages}</ItemGroup></Project>")
        (root / "global.json").write_text(global_json)
        return root

    def test_stable_tag_with_stable_pins_passes(self):
        release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="10.0.14" />'), False)

    def test_stable_tag_refuses_prerelease_pin_and_sdk(self):
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="11.0.0-rc.3.26522.148" />'), False)
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(self._repo(global_json='{"sdk":{"allowPrerelease":true}}'), False)

    def test_prerelease_tag_accepts_prerelease_pins(self):
        release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="11.0.0-rc.3.26522.148" />', '{"sdk":{"allowPrerelease":true}}'), True)

    def test_local_build_is_never_accepted(self):
        for prerelease in (False, True):
            with self.assertRaises(release_guards.GuardError):
                release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="TabTabGo.Core" Version="10.0.0-local.abc1234" />'), prerelease)


class VulnGateTests(unittest.TestCase):
    def test_clean_report(self):
        self.assertEqual([], vuln_gate.find_vulnerabilities({"projects": [{"path": "a.csproj"}]}))

    def test_vulnerable_package_found(self):
        report = {"projects": [{"frameworks": [{"transitivePackages": [
            {"id": "X", "resolvedVersion": "1.0.0", "vulnerabilities": [{"severity": "High", "advisoryurl": "https://a"}]}]}]}]}
        self.assertEqual(1, len(vuln_gate.find_vulnerabilities(report)))


class AssertPackagesTests(unittest.TestCase):
    def _write(self, directory, package_id, version, dep_version, tfm="net8.0", dotliquid="[2.3.260, 2.4.0)"):
        dep = "" if package_id == "TabTabGo.Templating" else f'<dependency id="TabTabGo.Templating" version="{dep_version}" />'
        if package_id == "TabTabGo.Templating.Dotliquid":
            dep += f'<dependency id="DotLiquid" version="{dotliquid}" />'
        nuspec = f'<package><metadata><id>{package_id}</id><version>{version}</version><dependencies><group targetFramework="{tfm}">{dep}</group></dependencies></metadata></package>'
        with zipfile.ZipFile(directory / f"{package_id}.{version}.nupkg", "w") as z:
            z.writestr(f"{package_id}.nuspec", nuspec)
        (directory / f"{package_id}.{version}.snupkg").write_bytes(b"x")

    def _dir(self, version="8.0.3", dep_version="8.0.3", tfm="net8.0", dotliquid="[2.3.260, 2.4.0)"):
        directory = Path(tempfile.mkdtemp())
        for package_id in assert_packages.IDS:
            self._write(directory, package_id, version, dep_version, tfm, dotliquid)
        return directory

    def test_valid_set_passes(self):
        self.assertEqual([], assert_packages.check(self._dir(), "8.0.3", "net8.0"))

    def test_wrong_tfm_fails(self):
        self.assertTrue(assert_packages.check(self._dir(tfm="net10.0"), "8.0.3", "net8.0"))

    def test_dotliquid_must_be_bound_to_the_tested_minor(self):
        # R8: the nesting scan binds to DotLiquid's internal tokenizer, so the dependency is [2.3.260, 2.4.0)
        for loose in ("2.3.260", "[2.3.260, )", "[2.3.260, 3.0.0)", "[2.3.0, 2.4.0)", "2.4.0"):
            with self.subTest(loose=loose):
                errors = assert_packages.check(self._dir(dotliquid=loose), "8.0.3", "net8.0")
                self.assertTrue(any("DotLiquid" in e for e in errors))

    def test_wrong_dependency_version_fails(self):
        self.assertTrue(assert_packages.check(self._dir(dep_version="8.0.2"), "8.0.3", "net8.0"))

    def _with_dependency(self, tfm, dependency):
        directory = self._dir(version="10.0.0", dep_version="10.0.0", tfm=tfm)
        path = directory / "TabTabGo.Templating.Dotliquid.10.0.0.nupkg"
        with zipfile.ZipFile(path, "w") as z:
            z.writestr("x.nuspec", f'<package><metadata><id>TabTabGo.Templating.Dotliquid</id><version>10.0.0</version><dependencies><group targetFramework="{tfm}"><dependency id="TabTabGo.Templating" version="10.0.0" /><dependency id="DotLiquid" version="[2.3.260, 2.4.0)" />{dependency}</group></dependencies></metadata></package>')
        return directory

    def test_any_tabtabgo_dependency_except_the_base_package_is_refused(self):
        for bad in ('<dependency id="TabTabGo.Core" version="[10.0.0, 11.0.0)" />', '<dependency id="TabTabGo.Core" version="10.0.0" />',
                    '<dependency id="TabTabGo.Storage" version="10.0.0" />'):
            with self.subTest(bad=bad):
                errors = assert_packages.check(self._with_dependency("net10.0", bad), "10.0.0", "net10.0")
                self.assertTrue(any("forbidden" in e for e in errors))
        self.assertEqual([], assert_packages.check(self._with_dependency("net10.0", ""), "10.0.0", "net10.0"))

    def test_local_dependency_is_refused(self):
        bad = '<dependency id="TabTabGo.Core" version="[10.0.0-local.abc1234, 11.0.0)" />'
        errors = assert_packages.check(self._with_dependency("net10.0", bad), "10.0.0", "net10.0")
        self.assertTrue(any("local build" in e for e in errors))

    def test_microsoft_extensions_must_match_the_line(self):
        self.assertEqual([], assert_packages.check(self._with_dependency("net10.0", '<dependency id="Microsoft.Extensions.Options" version="10.0.14" />'), "10.0.0", "net10.0"))
        self.assertTrue(assert_packages.check(self._with_dependency("net10.0", '<dependency id="Microsoft.Extensions.Options" version="8.0.2" />'), "10.0.0", "net10.0"))

    def test_extra_file_fails(self):
        directory = self._dir()
        (directory / "extra.nupkg").write_bytes(b"x")
        self.assertTrue(assert_packages.check(directory, "8.0.3", "net8.0"))


class WorkflowHygieneTests(unittest.TestCase):
    """Supply chain: a tool the release job runs must come from the pinned manifest, and a publish run may not skip a missing baseline."""

    WORKFLOWS = Path(__file__).resolve().parents[1] / "workflows"

    def test_no_unpinned_dotnet_tool_install(self):
        for workflow in sorted(self.WORKFLOWS.glob("*.yml")):
            for number, line in enumerate(workflow.read_text(encoding="utf-8").splitlines(), 1):
                if "dotnet tool install" in line and not re.search(r"--version\s+\d+\.\d+\.\d+\S*", line):
                    self.fail(f"{workflow.name}:{number} installs a dotnet tool without an exact --version; use .config/dotnet-tools.json and 'dotnet tool restore'")

    def test_manifest_versions_are_exact(self):
        manifest = Path(__file__).resolve().parents[2] / ".config" / "dotnet-tools.json"
        if manifest.exists():
            for name, tool in json.loads(manifest.read_text(encoding="utf-8"))["tools"].items():
                self.assertRegex(tool["version"], r"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", name)

    def test_publish_does_not_allow_a_missing_baseline(self):
        publish = self.WORKFLOWS / "publish.yml"
        self.assertNotIn("--allow-missing-baseline", publish.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
