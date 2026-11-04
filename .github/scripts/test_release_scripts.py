"""Self tests for the release scripts. Run: python3 -m unittest discover .github/scripts"""
import json
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


class VulnGateTests(unittest.TestCase):
    def test_clean_report(self):
        self.assertEqual([], vuln_gate.find_vulnerabilities({"projects": [{"path": "a.csproj"}]}))

    def test_vulnerable_package_found(self):
        report = {"projects": [{"frameworks": [{"transitivePackages": [
            {"id": "X", "resolvedVersion": "1.0.0", "vulnerabilities": [{"severity": "High", "advisoryurl": "https://a"}]}]}]}]}
        self.assertEqual(1, len(vuln_gate.find_vulnerabilities(report)))


class AssertPackagesTests(unittest.TestCase):
    def _write(self, directory, package_id, version, dep_version, tfm="net8.0"):
        dep = "" if package_id == "TabTabGo.Templating" else f'<dependency id="TabTabGo.Templating" version="{dep_version}" />'
        nuspec = f'<package><metadata><id>{package_id}</id><version>{version}</version><dependencies><group targetFramework="{tfm}">{dep}</group></dependencies></metadata></package>'
        with zipfile.ZipFile(directory / f"{package_id}.{version}.nupkg", "w") as z:
            z.writestr(f"{package_id}.nuspec", nuspec)
        (directory / f"{package_id}.{version}.snupkg").write_bytes(b"x")

    def _dir(self, version="8.0.3", dep_version="8.0.3", tfm="net8.0"):
        directory = Path(tempfile.mkdtemp())
        for package_id in assert_packages.IDS:
            self._write(directory, package_id, version, dep_version, tfm)
        return directory

    def test_valid_set_passes(self):
        self.assertEqual([], assert_packages.check(self._dir(), "8.0.3", "net8.0"))

    def test_wrong_tfm_fails(self):
        self.assertTrue(assert_packages.check(self._dir(tfm="net10.0"), "8.0.3", "net8.0"))

    def test_wrong_dependency_version_fails(self):
        self.assertTrue(assert_packages.check(self._dir(dep_version="8.0.2"), "8.0.3", "net8.0"))

    def test_extra_file_fails(self):
        directory = self._dir()
        (directory / "extra.nupkg").write_bytes(b"x")
        self.assertTrue(assert_packages.check(directory, "8.0.3", "net8.0"))


if __name__ == "__main__":
    unittest.main()
