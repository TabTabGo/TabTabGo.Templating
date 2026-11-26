"""Self tests for the release scripts. Run: python3 -m unittest discover .github/scripts"""
import contextlib
import io
import json
import tempfile
import unittest
import unittest.mock
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
        for tag in ("8.0.3", "v8.0", "v8.0.3-foo", "v8.0.3+meta", "v8.0.3-rc",
                    "v8.0.3\n", "v08.0.3", "v8.00.3", "v8.0.03", "v8.0.3-rc.01", "v\u0668.0.3", "v8.0.3-rc.\u0661"):
            with self.subTest(tag=tag):
                with self.assertRaises(release_guards.GuardError):
                    release_guards.parse_tag(tag)


class TfmAndVersionTests(unittest.TestCase):
    def test_commented_out_version_and_framework_are_not_read(self):
        root = Path(tempfile.mkdtemp())
        (root / "src").mkdir()
        (root / "Directory.Build.props").write_text(
            "<Project><!-- <Version>9.9.9</Version> <TargetFramework>net9.0</TargetFramework> -->"
            "<PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>8.0.3</Version></PropertyGroup></Project>")
        self.assertEqual("8.0.3", release_guards.declared_version(root))
        self.assertEqual({"net8.0"}, release_guards.target_frameworks(root))

    def test_version_inside_another_element_is_not_read(self):
        root = Path(tempfile.mkdtemp())
        (root / "Directory.Build.props").write_text(
            '<Project><ItemGroup><PackageReference Include="X"><Version>1.2.3</Version></PackageReference></ItemGroup></Project>')
        with self.assertRaises(release_guards.GuardError):
            release_guards.declared_version(root)

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


class VulnGateFailClosedTests(unittest.TestCase):
    CLEAN = {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"], "projects": [{"path": "a.csproj"}]}

    def _run(self, report):
        with unittest.mock.patch("sys.stdin", io.StringIO(json.dumps(report))), \
                contextlib.redirect_stderr(io.StringIO()), contextlib.redirect_stdout(io.StringIO()):
            return vuln_gate.main()

    def test_real_clean_report_passes(self):
        self.assertEqual(0, self._run(self.CLEAN))

    def test_service_index_problem_fails(self):
        self.assertNotEqual(0, self._run({"problems": [{"text": "Unable to load the service index"}]}))
        self.assertNotEqual(0, self._run({**self.CLEAN, "problems": [{"text": "Unable to load the service index"}]}))

    def test_nu1900_problem_fails(self):
        report = {**self.CLEAN, "problems": [{"level": "warning", "text": "warning NU1900: Error occurred while getting package vulnerability information"}]}
        self.assertNotEqual(0, self._run(report))

    def test_missing_or_empty_projects_fail(self):
        self.assertNotEqual(0, self._run({"sources": ["s"]}))
        self.assertNotEqual(0, self._run({"sources": ["s"], "projects": []}))

    def test_empty_frameworks_entry_fails(self):
        self.assertNotEqual(0, self._run({"sources": ["s"], "projects": [{"path": "a", "frameworks": []}]}))

    def test_no_package_sources_fail(self):
        self.assertNotEqual(0, self._run({"projects": [{"path": "a"}]}))

    def test_vulnerable_package_still_fails(self):
        report = {"sources": ["s"], "projects": [{"path": "a", "frameworks": [{"framework": "net10.0", "transitivePackages": [
            {"id": "X", "resolvedVersion": "1.0.0", "vulnerabilities": [{"severity": "High", "advisoryurl": "https://a"}]}]}]}]}
        self.assertEqual(1, self._run(report))

    def test_not_json_fails(self):
        with unittest.mock.patch("sys.stdin", io.StringIO("Unable to load")), contextlib.redirect_stderr(io.StringIO()):
            self.assertNotEqual(0, vuln_gate.main())


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
