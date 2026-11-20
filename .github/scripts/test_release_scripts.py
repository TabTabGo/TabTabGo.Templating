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


class ReleaseBranchTests(unittest.TestCase):
    def _clone_with_branch(self, branch):
        import os
        import subprocess
        base = Path(tempfile.mkdtemp())
        git = lambda *args, cwd=None: subprocess.run(["git", *args], cwd=cwd or base / "work", check=True, capture_output=True, text=True,
                                                     env={**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t", "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t"})
        git("init", "--bare", str(base / "origin.git"), cwd=base)
        git("clone", str(base / "origin.git"), str(base / "work"), cwd=base)
        (base / "work" / "f").write_text("x")
        git("add", "f")
        git("commit", "-m", "c")
        git("push", "origin", f"HEAD:refs/heads/{branch}")
        git("fetch", "origin")
        return base / "work", git("rev-parse", "HEAD").stdout.strip()

    def _check(self, branch, version):
        import os
        work, sha = self._clone_with_branch(branch)
        previous = os.getcwd()
        os.chdir(work)
        try:
            release_guards.check_release_branch(sha, release_guards.parse_tag("v" + version))
        finally:
            os.chdir(previous)

    def test_release_branch_without_suffix_accepts_stable_and_prerelease_tags(self):
        self._check("releases/v10.0.x/v10.0.0", "10.0.0")
        self._check("releases/v10.0.x/v10.0.0", "10.0.0-rc.1")

    def test_prerelease_tag_may_use_the_full_version_branch(self):
        self._check("releases/v11.0.x/v11.0.0-rc.1", "11.0.0-rc.1")

    def test_other_branch_is_refused(self):
        with self.assertRaises(release_guards.GuardError):
            self._check("releases/v10.0.x/v10.0.1", "10.0.0")
        with self.assertRaises(release_guards.GuardError):
            self._check("feature/x", "10.0.0")


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

    def _with_dependency(self, tfm, dependency):
        directory = self._dir(version="10.0.0", dep_version="10.0.0", tfm=tfm)
        path = directory / "TabTabGo.Templating.Dotliquid.10.0.0.nupkg"
        with zipfile.ZipFile(path, "w") as z:
            z.writestr("x.nuspec", f'<package><metadata><id>TabTabGo.Templating.Dotliquid</id><version>10.0.0</version><dependencies><group targetFramework="{tfm}"><dependency id="TabTabGo.Templating" version="10.0.0" />{dependency}</group></dependencies></metadata></package>')
        return directory

    def test_core_dependency_must_be_the_bounded_range_of_the_line(self):
        ok = '<dependency id="TabTabGo.Core" version="[10.0.0, 11.0.0)" />'
        self.assertEqual([], assert_packages.check(self._with_dependency("net10.0", ok), "10.0.0", "net10.0"))
        for bad in ('<dependency id="TabTabGo.Core" version="10.0.0" />', '<dependency id="TabTabGo.Core" version="[10.0.0, 12.0.0)" />',
                    '<dependency id="TabTabGo.Core" version="[11.0.0, 12.0.0)" />'):
            with self.subTest(bad=bad):
                self.assertTrue(assert_packages.check(self._with_dependency("net10.0", bad), "10.0.0", "net10.0"))

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


if __name__ == "__main__":
    unittest.main()
