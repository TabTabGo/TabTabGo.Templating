"""Self tests for the release scripts. Run: python3 -m unittest discover .github/scripts"""
import contextlib
import http.client
import io
import json
import re
import tempfile
import unittest
import urllib.error
import unittest.mock
import zipfile
from pathlib import Path

import assert_packages
import apicompat
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


class LocalPinTests(unittest.TestCase):
    def _repo(self, packages="", description=""):
        root = Path(tempfile.mkdtemp())
        (root / "src").mkdir()
        (root / "Directory.Packages.props").write_text(f"<Project><PropertyGroup><Description>{description}</Description></PropertyGroup><ItemGroup>{packages}</ItemGroup></Project>")
        (root / "global.json").write_text("{}")
        return root

    def test_a_description_with_the_word_local_does_not_block(self):
        release_guards.check_pins_match_release_kind(self._repo(description="published to a -local feed for tests"), False)

    def test_a_comment_with_the_word_local_does_not_block(self):
        root = self._repo()
        (root / "Directory.Packages.props").write_text("<Project><!-- TabTabGo.Core 10.0.0-local.1 is used by developers --><ItemGroup/></Project>")
        release_guards.check_pins_match_release_kind(root, False)

    def test_a_real_local_core_range_blocks(self):
        for version in ("10.0.0-local.abc1234", "[10.0.0-local.abc1234, 11.0.0)"):
            with self.subTest(version=version):
                with self.assertRaises(release_guards.GuardError):
                    release_guards.check_pins_match_release_kind(self._repo(packages=f'<PackageVersion Include="TabTabGo.Core" Version="{version}" />'), True)

    def test_a_malformed_props_file_fails_closed(self):
        root = self._repo()
        (root / "Directory.Packages.props").write_text("<Project><oops")
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(root, False)


class ApiCompatTests(unittest.TestCase):
    def test_semver_key_orders_versions_numerically_and_prereleases_below_releases(self):
        ordered = ["10.0.0-rc.1", "10.0.0-rc.2", "10.0.0-rc.10", "10.0.0", "10.0.9", "10.0.10", "11.0.0-preview.3.26200.1", "11.0.0-rc.1.2", "11.0.0"]
        self.assertEqual(ordered, sorted(reversed(ordered), key=apicompat.semver_key))
        self.assertGreater(apicompat.semver_key("10.0.10"), apicompat.semver_key("10.0.9"))
        self.assertGreater(apicompat.semver_key("11.0.0"), apicompat.semver_key("11.0.0-rc.9"))

    def test_framework_reference_picks_the_newest_pack_numerically_and_prefers_ga_over_preview(self):
        root = Path(tempfile.mkdtemp())
        for version in ("10.0.9", "10.0.10", "10.0.10-rc.2", "11.0.0-preview.1.1", "11.0.0-rc.1.2", "11.0.0"):
            (root / "packs" / "Microsoft.NETCore.App.Ref" / version / "ref" / ("net11.0" if version.startswith("11") else "net10.0")).mkdir(parents=True)
        (root / "packs" / "Microsoft.NETCore.App.Ref" / "stray-file").write_text("x")
        self.assertTrue(apicompat.framework_reference("net10.0", [str(root)]).endswith("10.0.10/ref/net10.0"))
        self.assertTrue(apicompat.framework_reference("net11.0", [str(root)]).endswith("11.0.0/ref/net11.0"))

    def test_declared_dependencies_come_from_the_lock_files_without_projects_or_tabtabgo_packages(self):
        root = Path(tempfile.mkdtemp())
        for name, entries in {
            "A": {"DotLiquid": {"type": "Direct", "resolved": "2.3.260"}, "TabTabGo.Core": {"type": "Direct", "resolved": "10.0.0"}, "Other": {"type": "Project"}},
            "B": {"DotLiquid": {"type": "Transitive", "resolved": "2.3.9"}, "Newtonsoft.Json": {"type": "Direct", "resolved": "13.0.4"}},
        }.items():
            (root / "src" / name).mkdir(parents=True)
            (root / "src" / name / "packages.lock.json").write_text(json.dumps({"version": 2, "dependencies": {"net11.0": entries}}))
        self.assertEqual({"DotLiquid": "2.3.260", "Newtonsoft.Json": "13.0.4"}, apicompat.declared_dependencies(root))

    def test_package_references_skip_stale_entries_files_and_unknown_packages(self):
        cache = Path(tempfile.mkdtemp())
        (cache / "dotliquid" / "2.3.260" / "lib" / "net10.0").mkdir(parents=True)
        (cache / "dotliquid" / "2.3.999" / "lib" / "net11.0").mkdir(parents=True)  # a stale, undeclared version is never picked
        (cache / "tabtabgo.templating").mkdir()
        (cache / "somefile").write_text("x")
        found = apicompat.package_references({"DotLiquid": "2.3.260", "Missing": "1.0.0", "Newtonsoft.Json": "13.0.4"}, ["net11.0", "net10.0"], cache)
        self.assertEqual([str(cache / "dotliquid" / "2.3.260" / "lib" / "net10.0")], found)

    def test_preference_never_looks_at_a_newer_framework_than_the_side_has(self):
        self.assertEqual(["net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"], apicompat.preference("net10.0"))
        self.assertNotIn("net11.0", apicompat.preference("net10.0"))

    def test_a_missing_baseline_skips_only_that_package(self):
        present = {"TabTabGo.Templating": Path("a"), "TabTabGo.Templating.OpenXml": Path("c")}
        found, missing = apicompat.collect_baselines(lambda package_id: present.get(package_id), allow_missing=True)
        self.assertEqual(["TabTabGo.Templating.Dotliquid"], missing)
        self.assertEqual({"TabTabGo.Templating", "TabTabGo.Templating.OpenXml"}, set(found))

    def test_a_missing_base_baseline_skips_the_packages_that_depend_on_it(self):
        present = {"TabTabGo.Templating.Dotliquid": Path("b"), "TabTabGo.Templating.OpenXml": Path("c")}
        found, missing = apicompat.collect_baselines(lambda package_id: present.get(package_id), allow_missing=True)
        self.assertEqual({}, found)
        self.assertEqual(3, len(set(missing)))

    def test_without_allow_missing_the_caller_sees_the_missing_package(self):
        found, missing = apicompat.collect_baselines(lambda package_id: None if package_id.endswith("OpenXml") else Path("x"), allow_missing=False)
        self.assertEqual(["TabTabGo.Templating.OpenXml"], missing)

    def test_fetch_retries_a_transient_error_three_times_and_then_reports_it(self):
        calls = []
        sleeps = []

        def failing(url, destination):
            calls.append(url)
            raise urllib.error.URLError("temporary failure")

        with unittest.mock.patch("urllib.request.urlretrieve", failing), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as raised:
                apicompat.fetch_baseline("TabTabGo.Templating", "10.0.0", Path(tempfile.mkdtemp()), sleep=sleeps.append)
        self.assertEqual(3, len(calls))
        self.assertEqual([1, 2], sleeps)
        self.assertIn("not a missing baseline", str(raised.exception))

    def test_fetch_retries_a_connection_cut_mid_body_and_other_non_oserror_failures(self):
        for failure in (http.client.IncompleteRead(b"ab", 10), http.client.RemoteDisconnected("closed"), EOFError("short")):
            with self.subTest(failure=type(failure).__name__):
                results = [failure, failure, None]
                sleeps = []

                def flaky(url, destination):
                    outcome = results.pop(0)
                    if outcome is not None:
                        raise outcome
                    Path(destination).write_bytes(b"ok")

                with unittest.mock.patch("urllib.request.urlretrieve", flaky), contextlib.redirect_stdout(io.StringIO()):
                    package = apicompat.fetch_baseline("TabTabGo.Templating", "10.0.0", Path(tempfile.mkdtemp()), sleep=sleeps.append)
                self.assertIsNotNone(package)
                self.assertEqual([1, 2], sleeps)

    def test_fetch_gives_up_after_three_cut_connections_with_a_plain_message(self):
        def cut(url, destination):
            raise http.client.IncompleteRead(b"", 5)

        with unittest.mock.patch("urllib.request.urlretrieve", cut), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as raised:
                apicompat.fetch_baseline("TabTabGo.Templating", "10.0.0", Path(tempfile.mkdtemp()), sleep=lambda s: None)
        self.assertIn("could not download", str(raised.exception))

    def test_unpack_skips_entries_with_dot_dot_segments(self):
        work = Path(tempfile.mkdtemp())
        package = work / "p.nupkg"
        with zipfile.ZipFile(package, "w") as archive:
            archive.writestr("lib/net10.0/A.dll", "good")
            archive.writestr("lib/net10.0/../../../escape.dll", "bad")
            archive.writestr("lib/net10.0/..\\..\\escape2.dll", "bad")
            archive.writestr("lib/net10.0/sub/../B.dll", "bad")
        target = work / "out"

        folder = apicompat.unpack(package, "net10.0", target)

        self.assertEqual(["A.dll"], sorted(p.name for p in folder.rglob("*.dll")))
        self.assertFalse(list(work.rglob("escape*.dll")))

    def test_fetch_treats_404_as_missing_without_retrying(self):
        calls = []

        def not_found(url, destination):
            calls.append(url)
            raise urllib.error.HTTPError(url, 404, "Not Found", None, None)

        with unittest.mock.patch("urllib.request.urlretrieve", not_found):
            self.assertIsNone(apicompat.fetch_baseline("TabTabGo.Templating", "10.0.0", Path(tempfile.mkdtemp()), sleep=lambda s: None))
        self.assertEqual(1, len(calls))


class PinFormTests(unittest.TestCase):
    """Round 4: single-quoted attributes, the element form, comments and a version hidden behind a property."""

    def _repo(self, packages="", properties="", build_props=""):
        root = Path(tempfile.mkdtemp())
        (root / "src").mkdir()
        (root / "Directory.Packages.props").write_text(f"<Project><PropertyGroup>{properties}</PropertyGroup><ItemGroup>{packages}</ItemGroup></Project>")
        (root / "Directory.Build.props").write_text(f"<Project>{build_props}</Project>")
        (root / "global.json").write_text("{}")
        return root

    def test_single_quoted_prerelease_pin_blocks_a_stable_tag(self):
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(self._repo("<PackageVersion Include='A' Version='11.0.0-rc.3' />"), False)

    def test_element_form_prerelease_pin_blocks_a_stable_tag(self):
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(self._repo("<PackageReference Include='A'><Version>11.0.0-preview.1</Version></PackageReference>"), False)

    def test_element_form_stable_pin_passes(self):
        release_guards.check_pins_match_release_kind(self._repo("<PackageReference Include='A'><Version>11.0.0</Version></PackageReference>"), False)

    def test_commented_out_prerelease_pin_is_ignored(self):
        release_guards.check_pins_match_release_kind(self._repo("<!-- <PackageVersion Include='A' Version='11.0.0-rc.3' /> --><PackageVersion Include='A' Version='10.0.1' />"), False)

    def test_prerelease_pin_behind_a_property_blocks_a_stable_tag(self):
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="$(CoreVersion)" />', "<CoreVersion>11.0.0-rc.3</CoreVersion>"), False)

    def test_local_pin_behind_a_property_blocks_every_tag(self):
        for prerelease in (False, True):
            with self.subTest(prerelease=prerelease):
                with self.assertRaises(release_guards.GuardError):
                    release_guards.check_pins_match_release_kind(
                        self._repo('<PackageVersion Include="TabTabGo.Core" Version="$(CoreVersion)" />', "<CoreVersion>[$(Base)-local.abc1234, 11.0.0)</CoreVersion><Base>10.0.0</Base>"),
                        prerelease,
                    )

    def test_property_declared_in_the_build_props_is_resolved(self):
        repo = self._repo('<PackageVersion Include="A" Version="$(CoreVersion)" />', build_props="<PropertyGroup><CoreVersion>10.0.0-local.1</CoreVersion></PropertyGroup>")
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(repo, True)

    def test_stable_property_pin_passes(self):
        release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="$(CoreVersion)" />', "<CoreVersion>10.0.1</CoreVersion>"), False)

    def test_an_unresolved_property_blocks_a_stable_tag_but_not_a_prerelease_tag(self):
        repo = self._repo('<PackageVersion Include="B" Version="$(Unknown)" />')
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(repo, False)
        release_guards.check_pins_match_release_kind(repo, True)

    def test_a_later_or_conditional_declaration_is_checked_too(self):
        properties = "<CoreVersion>10.0.1</CoreVersion><CoreVersion Condition=\"'$(Configuration)' == 'Release'\">11.0.0-rc.3</CoreVersion>"
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="$(CoreVersion)" />', properties), False)
        local = "<CoreVersion>10.0.1</CoreVersion><CoreVersion Condition=\"'$(CI)' == 'true'\">10.0.1-LOCAL.7</CoreVersion>"
        for prerelease in (False, True):
            with self.assertRaises(release_guards.GuardError):
                release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="$(CoreVersion)" />', local), prerelease)

    def test_any_semver_label_is_a_prerelease_in_any_case(self):
        for version in ("10.0.1-Beta.2", "10.0.1-PREVIEW.1", "10.0.1-nightly.20261126", "10.0.1-x"):
            with self.subTest(version=version):
                with self.assertRaises(release_guards.GuardError):
                    release_guards.check_pins_match_release_kind(self._repo(f'<PackageVersion Include="A" Version="{version}" />'), False)
        release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="[10.0.1, 11.0.0)" />'), False)

    def test_an_oversized_resolution_is_not_silently_accepted(self):
        big = "x" * 3000
        properties = f"<A>{big}$(B)</A><B>{big}</B>"
        with self.assertRaises(release_guards.GuardError):
            release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="$(A)" />', properties), False)

    def test_dropped_candidates_leave_a_marker_so_a_pre_release_value_cannot_hide(self):
        # 300 declared values plus the rc one: the cap drops candidates, which must block (stable and pre-release tags alike)
        properties = "".join(f"<P>10.0.{i}</P>" for i in range(300)) + "<P>11.0.0-rc.1</P>"
        repo = self._repo('<PackageVersion Include="A" Version="$(P)" />', properties)
        for prerelease in (False, True):
            with self.assertRaises(release_guards.GuardError) as raised:
                release_guards.check_pins_match_release_kind(repo, prerelease)
            self.assertIn("too many", str(raised.exception))
        self.assertIn(release_guards.TRUNCATED, release_guards.expand_properties("$(P)", {"P": [str(i) for i in range(300)]}))

    def test_floating_versions_are_prerelease_and_build_metadata_is_not(self):
        for version in ("2.3.260-*", "10.*-*", "10.0.1-*"):
            with self.subTest(version=version):
                self.assertTrue(release_guards.has_prerelease_label(version))
                with self.assertRaises(release_guards.GuardError):
                    release_guards.check_pins_match_release_kind(self._repo(f'<PackageVersion Include="A" Version="{version}" />'), False)
        for version in ("1.0.0+2026-abc", "10.0.1", "[10.0.1, 11.0.0)", "10.*"):
            with self.subTest(version=version):
                self.assertFalse(release_guards.has_prerelease_label(version))
        release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="A" Version="1.0.0+2026-abc" />'), False)

    def test_every_reference_is_expanded_per_pass_so_many_references_resolve(self):
        text = ".".join(f"$(P{i})" for i in range(12))
        properties = {f"P{i}": ["1"] for i in range(12)}
        self.assertEqual([".".join(["1"] * 12)], release_guards.expand_properties(text, properties))
        names = "".join(f"<P{i}>1</P{i}>" for i in range(12))
        release_guards.check_pins_match_release_kind(self._repo(f'<PackageVersion Include="A" Version="{text}" />', names), False)

    def test_a_self_appending_property_reports_that_it_does_not_resolve(self):
        repo = self._repo('<PackageVersion Include="A" Version="$(S)" />', "<S>$(S)-ci</S>")
        with self.assertRaises(release_guards.GuardError) as raised:
            release_guards.check_pins_match_release_kind(repo, False)
        self.assertIn("does not resolve within", str(raised.exception))
        self.assertNotIn("undeclared", str(raised.exception))

    def test_an_undeclared_property_has_its_own_message(self):
        with self.assertRaises(release_guards.GuardError) as raised:
            release_guards.check_pins_match_release_kind(self._repo('<PackageVersion Include="B" Version="$(Unknown)" />'), False)
        self.assertIn("undeclared", str(raised.exception))

    def test_expand_properties_covers_every_declared_value_and_caps_the_candidates(self):
        self.assertEqual({"1", "2"}, set(release_guards.expand_properties("$(P)", {"P": ["1", "2"]})))
        many = {"P": [str(i) for i in range(50)]}
        self.assertLessEqual(len(release_guards.expand_properties("$(P)-$(P)-$(P)", many)), release_guards.MAX_CANDIDATES + 1)

    def test_prerelease_tag_accepts_prerelease_pins_in_every_form(self):
        release_guards.check_pins_match_release_kind(self._repo("<PackageVersion Include='A' Version='11.0.0-rc.3' />"), True)

    def test_resolve_properties_stops_on_a_cycle(self):
        self.assertTrue(all("$(" in value for value in release_guards.expand_properties("$(A)", {"A": ["$(B)"], "B": ["$(A)"]})))


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
        self.assertIn("user: ${{ secrets.NUGET_USER }}", self.publish)
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
