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

    def test_prerelease_tags_and_build_metadata_are_refused_with_a_clear_message(self):
        for tag in ("v10.0.0-rc.1", "v11.0.0-rc.1", "v8.0.5-beta.1", "v8.0.5-ci.7", "v8.0.5-alpha.1", "v10.0.0+x", "v10.0.0-rc.1+x", "v8.0.3-foo", "v8.0.3-rc"):
            with self.subTest(tag=tag):
                with self.assertRaises(release_guards.GuardError) as raised:
                    release_guards.parse_tag(tag)
                self.assertIn("only stable vMAJOR.MINOR.PATCH tags are released", str(raised.exception))

    def test_stable_tags_pass_and_report_no_prerelease(self):
        for tag, core in (("v10.0.0", "10.0.0"), ("v11.0.0", "11.0.0"), ("v8.0.5", "8.0.5")):
            with self.subTest(tag=tag):
                info = release_guards.parse_tag(tag)
                self.assertEqual((core, False), (info["core"], info["prerelease"]))
                self.assertEqual(core, info["version"])

    def test_main_refuses_a_prerelease_tag_before_any_other_check(self):
        for tag in ("v11.0.0-rc.1", "v8.0.5-beta.1", "v10.0.0+x"):
            with self.subTest(tag=tag):
                with contextlib.redirect_stderr(io.StringIO()) as err, contextlib.redirect_stdout(io.StringIO()) as out:
                    code = release_guards.main([tag, "0" * 40, "--offline", "--root", tempfile.mkdtemp()])
                self.assertEqual(1, code)
                self.assertIn("only stable", err.getvalue())
                self.assertEqual("", out.getvalue())

    def test_rejects_bad_tags(self):
        for tag in ("8.0.3", "v8.0", "v8.0.3-foo", "v8.0.3+meta", "v8.0.3-rc", "v8.0.3-",
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


class VulnGateExtractionTests(unittest.TestCase):
    CLEAN = '{"version": 1, "sources": ["s"], "projects": [{"path": "a"}]}'

    def _run(self, text):
        with unittest.mock.patch("sys.stdin", io.StringIO(text)), contextlib.redirect_stderr(io.StringIO()), contextlib.redirect_stdout(io.StringIO()):
            return vuln_gate.main()

    def test_a_warning_line_with_a_brace_before_the_report_does_not_break_a_clean_audit(self):
        self.assertEqual(0, self._run("warning NU1900: could not read {feed} metadata\n" + self.CLEAN))
        self.assertEqual(0, self._run("{ not json\n" + self.CLEAN))

    def test_a_non_dict_vulnerability_entry_is_a_finding(self):
        report = {"sources": ["s"], "projects": [{"path": "a", "frameworks": [{"framework": "net10.0", "topLevelPackages": [
            {"id": "X", "resolvedVersion": "1.0.0", "vulnerabilities": ["oops", 3]}]}]}]}
        self.assertEqual(1, self._run(json.dumps(report)))
        self.assertEqual(1, len(vuln_gate.find_vulnerabilities(report)))

    def test_no_parseable_report_fails_closed(self):
        for text in ("", "   ", "no json here", "{ broken", "[1, 2]", '{"unrelated": 1}', "warning {x}\nstill nothing"):
            with self.subTest(text=text):
                self.assertEqual(2, self._run(text))


class PublishWorkflowShapeTests(unittest.TestCase):
    """The privileged job runs no repository code, publishes by trusted publishing only, and fails closed."""

    @classmethod
    def setUpClass(cls):
        cls.text = (Path(__file__).resolve().parents[1] / "workflows" / "publish.yml").read_text(encoding="utf-8")
        cls.build, cls.publish = cls.text.split("\n  publish:\n")

    def test_publish_job_checks_out_nothing_and_runs_no_repository_script(self):
        self.assertNotIn("actions/checkout", self.publish)
        self.assertNotIn(".github/scripts", self.publish)
        self.assertNotIn("refs/tags", self.publish)
        self.assertNotIn("github.ref", self.publish.replace("github.run_attempt", ""))

    def test_only_the_publish_job_has_an_id_token_and_it_uses_the_stable_environment(self):
        self.assertNotIn("id-token", self.build)
        self.assertIn("id-token: write", self.publish)
        self.assertIn("environment: nuget-stable", self.publish)
        self.assertNotIn("nuget-prerelease", self.text)
        self.assertNotIn("prerelease", self.text.replace("pre-release", ""))

    def test_trusted_publishing_not_a_stored_api_key(self):
        self.assertIn("NuGet/login@", self.publish)
        self.assertIn("user: ${{ vars.NUGET_USER }}", self.publish)
        self.assertNotIn("secrets.NUGET_USER", self.text)  # the profile name is a repository variable, not a credential
        self.assertIn("NUGET_USER: ${{ vars.NUGET_USER }}", self.publish)
        self.assertIn('[ -n "$NUGET_USER" ]', self.publish)
        self.assertLess(self.publish.index("Require the trusted-publishing user"), self.publish.index("NuGet/login@"))
        self.assertIn("steps.login.outputs.NUGET_API_KEY", self.publish)
        self.assertNotIn("secrets.NUGET_API_KEY", self.text)
        self.assertNotIn("secrets.NUGET_KEY", self.text)

    def test_fail_closed_attestation_and_package_verification_before_login(self):
        self.assertIn("if: vars.STABLE_PUBLISH_MODE == 'environment'", self.publish.split("runs-on:")[0])
        self.assertIn('"$PUBLISH_MODE" != "environment"', self.build)
        self.assertLess(self.build.index("STABLE_PUBLISH_MODE"), self.build.index("actions/checkout"))
        self.assertLess(self.publish.index("Verify the downloaded package set"), self.publish.index("NuGet/login@"))
        self.assertLess(self.publish.index("Download packages"), self.publish.index("Verify the downloaded package set"))


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
