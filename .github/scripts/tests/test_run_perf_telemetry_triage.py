from pathlib import Path
import sys
import textwrap
import unittest
from unittest import mock


SCRIPTS_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS_ROOT))

import run_perf_telemetry_triage as triage_script  # noqa: E402
import run_retriage_perf_telemetry_comment as retriage_script  # noqa: E402


PERF_ISSUE_BODY = textwrap.dedent(
    """
    <!-- performance-telemetry-report -->
    ### Game version
    1.5.xf1

    ### Mod version
    0.2.0

    ### Save or city label
    New Seoul

    ### What changed
    Comparison build changes buyer cadence.

    ### Platform notes
    Windows release build

    ### Other mods
    none known

    ### Baseline telemetry bundle
    ```csv
    # telemetry_schema_version=1
    # telemetry_file_kind=summary
    # run_id=baseline-run
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    baseline-run,1,100,60,16,18,3,1,0.2,10,1,1,2,false
    ```

    ### Comparison telemetry bundle
    _No response_
    """
).strip()


class RunPerfTelemetryTriageTests(unittest.TestCase):
    def test_try_post_issue_comment_ignores_automation_error(self) -> None:
        with mock.patch.object(
            triage_script,
            "create_issue_comment",
            side_effect=triage_script.AutomationError("boom"),
        ) as comment_mock:
            triage_script.try_post_issue_comment("FennexFox/NoOfficeDemandFix", 21, "body", "token")

        comment_mock.assert_called_once_with("FennexFox/NoOfficeDemandFix", 21, "body", "token")

    def test_run_triage_for_issue_raises_for_non_perf_issue(self) -> None:
        with mock.patch.object(triage_script, "is_performance_telemetry_issue", return_value=False):
            with self.assertRaisesRegex(
                triage_script.AutomationError,
                r"Issue #21 is not a performance telemetry intake issue\.",
            ):
                triage_script.run_triage_for_issue(
                    "FennexFox/NoOfficeDemandFix",
                    21,
                    "[Bug] not perf",
                    "Not a perf issue",
                    "token",
                )

    def test_main_posts_attachment_failure_comment_when_download_fails(self) -> None:
        event = {"issue": {"number": 21, "title": "[Performance Telemetry] test", "body": PERF_ISSUE_BODY}}

        with mock.patch.dict(
            triage_script.os.environ,
            {
                "GITHUB_EVENT_PATH": "event.json",
                "GITHUB_REPOSITORY": "FennexFox/NoOfficeDemandFix",
                "GITHUB_TOKEN": "token",
            },
            clear=False,
        ):
            with mock.patch.object(triage_script, "load_event_payload", return_value=event):
                with mock.patch.object(triage_script, "is_performance_telemetry_issue", return_value=True):
                    with mock.patch.object(
                        triage_script,
                        "run_triage_for_issue",
                        side_effect=triage_script.AttachmentDownloadError("attachment missing"),
                    ):
                        with mock.patch.object(
                            triage_script,
                            "build_attachment_failure_comment",
                            return_value="attachment failed",
                        ):
                            with mock.patch.object(triage_script, "create_issue_comment") as comment_mock:
                                triage_script.main()

        comment_mock.assert_called_once_with(
            "FennexFox/NoOfficeDemandFix",
            21,
            "attachment failed",
            "token",
        )

    def test_run_triage_for_issue_upserts_managed_comment(self) -> None:
        issue_fields = {
            "game_version": "1.5.xf1",
            "mod_version": "0.2.0",
            "save_or_city_label": "New Seoul",
            "what_changed": "Comparison build changes buyer cadence.",
            "platform_notes": "Windows release build",
            "other_mods": "none known",
            "baseline_bundle": "bundle",
            "comparison_bundle": "",
        }
        triage = mock.Mock()
        updated_comment = {"html_url": "https://example.invalid/comment"}

        with mock.patch.object(triage_script, "parse_issue_form_sections", return_value=issue_fields):
            with mock.patch.object(triage_script, "build_triage_analysis", return_value=triage):
                with mock.patch.object(triage_script, "render_managed_comment", return_value="managed-body"):
                    with mock.patch.object(
                        triage_script,
                        "upsert_perf_telemetry_managed_comment",
                        return_value=updated_comment,
                    ) as upsert_mock:
                        result = triage_script.run_triage_for_issue(
                            "FennexFox/NoOfficeDemandFix",
                            21,
                            "[Performance Telemetry] test",
                            PERF_ISSUE_BODY,
                            "token",
                        )

        self.assertEqual(result, updated_comment)
        upsert_mock.assert_called_once_with(
            "FennexFox/NoOfficeDemandFix",
            21,
            "managed-body",
            "token",
        )


class RetriagePerfTelemetryCommentTests(unittest.TestCase):
    def test_main_skips_non_maintainer_comment(self) -> None:
        event = {
            "issue": {"number": 21},
            "comment": {"id": 55, "body": "/retriage", "author_association": "NONE", "user": {"login": "someone"}},
        }
        refreshed_issue = {"number": 21, "state": "open", "title": "[Performance Telemetry] test", "body": PERF_ISSUE_BODY}

        with mock.patch.dict(
            retriage_script.os.environ,
            {
                "GITHUB_EVENT_PATH": "event.json",
                "GITHUB_REPOSITORY": "FennexFox/NoOfficeDemandFix",
                "GITHUB_TOKEN": "token",
            },
            clear=False,
        ):
            with mock.patch.object(retriage_script, "load_event_payload", return_value=event):
                with mock.patch.object(retriage_script, "get_issue", return_value=refreshed_issue):
                    with mock.patch.object(retriage_script, "run_triage_for_issue") as triage_mock:
                        retriage_script.main()

        triage_mock.assert_not_called()

    def test_main_retriages_issue_on_maintainer_command(self) -> None:
        event = {
            "issue": {"number": 21},
            "comment": {
                "id": 55,
                "body": "/retriage",
                "author_association": "MEMBER",
                "user": {"login": "maintainer"},
            },
        }
        refreshed_issue = {"number": 21, "state": "open", "title": "[Performance Telemetry] test", "body": PERF_ISSUE_BODY}
        updated_comment = {"html_url": "https://example.invalid/comment"}

        with mock.patch.dict(
            retriage_script.os.environ,
            {
                "GITHUB_EVENT_PATH": "event.json",
                "GITHUB_REPOSITORY": "FennexFox/NoOfficeDemandFix",
                "GITHUB_TOKEN": "token",
            },
            clear=False,
        ):
            with mock.patch.object(retriage_script, "load_event_payload", return_value=event):
                with mock.patch.object(retriage_script, "get_issue", return_value=refreshed_issue):
                    with mock.patch.object(retriage_script, "run_triage_for_issue", return_value=updated_comment) as triage_mock:
                        with mock.patch.object(retriage_script, "create_issue_comment") as comment_mock:
                            retriage_script.main()

        triage_mock.assert_called_once()
        comment_mock.assert_called_once()
