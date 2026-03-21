from pathlib import Path
import sys
import textwrap
import unittest
from unittest import mock


SCRIPTS_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS_ROOT))

import run_raw_log_triage as triage_script  # noqa: E402


RAW_ISSUE_BODY = textwrap.dedent(
    """
    <!-- raw-log-report -->
    ### Game version
    1.5.xf1

    ### Mod version
    0.1.1

    ### Save or city label
    New Seoul

    ### What happened
    Loaded the save, enabled diagnostics, and waited 3 in-game days.

    ### Platform notes
    Windows release build

    ### Other mods
    none known

    ### Raw log
    ```text
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(...)
    ```
    """
).strip()


NON_RAW_ISSUE_BODY = "Not a raw-log intake issue."


class RunRawLogTriageTests(unittest.TestCase):
    def test_build_default_reply_fields_prefers_llm_values(self) -> None:
        issue_fields = {
            "save_or_city_label": "Issue label",
            "what_happened": "Issue reproduction",
            "platform_notes": "Issue platform notes",
        }
        deterministic_draft = {
            "title": "Deterministic title",
            "scenario_label": "Deterministic label",
            "scenario_type": "existing save",
            "platform_notes": "Deterministic platform notes",
            "comparison_baseline": "Deterministic baseline",
            "symptom_classification": "software_track_unclear",
            "custom_symptom_classification": "",
            "evidence_summary": "Deterministic summary",
            "confidence": "medium",
            "confounders": "Deterministic confounders",
            "analysis_basis": "Deterministic analysis",
            "log_excerpt": "Deterministic excerpt",
            "notes": "Deterministic notes",
        }
        llm_draft = {
            "title": "LLM title",
            "comparison_baseline": "LLM baseline",
            "symptom_classification": "software_demand_mismatch",
            "custom_symptom_classification": "",
            "evidence_summary": "LLM summary",
            "confidence": "high",
            "confounders": "LLM confounders",
            "analysis_basis": "LLM analysis",
            "log_excerpt": "LLM excerpt",
            "notes": "LLM notes",
        }

        reply_fields = triage_script.build_default_reply_fields(issue_fields, deterministic_draft, llm_draft)

        self.assertEqual(reply_fields["title"], "LLM title")
        self.assertEqual(reply_fields["scenario_label"], "Issue label")
        self.assertEqual(reply_fields["reproduction_conditions"], "Issue reproduction")
        self.assertEqual(reply_fields["platform_notes"], "Issue platform notes")
        self.assertEqual(reply_fields["comparison_baseline"], "LLM baseline")
        self.assertEqual(reply_fields["symptom_classification"], "software_demand_mismatch")
        self.assertEqual(reply_fields["evidence_summary"], "LLM summary")
        self.assertEqual(reply_fields["confidence"], "high")
        self.assertEqual(reply_fields["confounders"], "- Deterministic confounders")
        self.assertEqual(reply_fields["analysis_basis"], "LLM analysis")
        self.assertEqual(reply_fields["log_excerpt"], "LLM excerpt")
        self.assertEqual(reply_fields["notes"], "LLM notes")

    def test_try_post_issue_comment_ignores_automation_error(self) -> None:
        with mock.patch.object(
            triage_script,
            "create_issue_comment",
            side_effect=triage_script.AutomationError("boom"),
        ) as comment_mock:
            triage_script.try_post_issue_comment("FennexFox/NoOfficeDemandFix", 21, "body", "token")

        comment_mock.assert_called_once_with("FennexFox/NoOfficeDemandFix", 21, "body", "token")

    def test_run_triage_for_issue_raises_for_non_raw_log_issue(self) -> None:
        with mock.patch.object(triage_script, "is_raw_log_issue", return_value=False):
            with self.assertRaisesRegex(
                triage_script.AutomationError,
                r"Issue #21 is not a raw-log intake issue\.",
            ):
                triage_script.run_triage_for_issue(
                    "FennexFox/NoOfficeDemandFix",
                    21,
                    "[Bug] not raw",
                    NON_RAW_ISSUE_BODY,
                    "token",
                )

    def test_main_skips_non_raw_log_issue(self) -> None:
        event = {"issue": {"number": 21, "title": "[Bug] not raw", "body": NON_RAW_ISSUE_BODY}}

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
                with mock.patch.object(triage_script, "is_raw_log_issue", return_value=False):
                    with mock.patch.object(triage_script, "run_triage_for_issue") as triage_mock:
                        triage_script.main()

        triage_mock.assert_not_called()

    def test_main_posts_attachment_failure_comment_when_download_fails(self) -> None:
        event = {"issue": {"number": 21, "title": "[Raw Log] test", "body": RAW_ISSUE_BODY}}

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
                with mock.patch.object(triage_script, "is_raw_log_issue", return_value=True):
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

    def test_main_upserts_managed_comment_on_success(self) -> None:
        issue_fields = {
            "save_or_city_label": "New Seoul",
            "what_happened": "Loaded the save and waited.",
            "platform_notes": "Windows release build",
        }
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": "raw-log"}
        parsed_log = {"latest_observation": {"diagnostic_counters": {}}, "observation_count": 1}
        deterministic_draft = {
            "title": "Deterministic title",
            "scenario_label": "New Seoul",
            "scenario_type": "existing save",
            "platform_notes": "Windows release build",
            "comparison_baseline": "",
            "symptom_classification": "software_track_unclear",
            "custom_symptom_classification": "",
            "evidence_summary": "Deterministic summary",
            "confidence": "medium",
            "confounders": "Deterministic confounders",
            "analysis_basis": "",
            "log_excerpt": "Deterministic excerpt",
            "notes": "Deterministic notes",
        }
        llm_result = {
            "draft": {
                "title": "LLM title",
                "comparison_baseline": "LLM baseline",
                "symptom_classification": "software_demand_mismatch",
                "custom_symptom_classification": "",
                "evidence_summary": "LLM summary",
                "confidence": "high",
                "confounders": "LLM confounders",
                "analysis_basis": "LLM analysis",
                "log_excerpt": "LLM excerpt",
                "notes": "LLM notes",
            },
            "status": "enabled",
            "detail": "openai/gpt-4.1-mini",
        }
        updated_comment = {"html_url": "https://example.invalid/comment"}

        with mock.patch.object(triage_script, "parse_issue_form_sections", return_value=issue_fields):
            with mock.patch.object(triage_script, "select_raw_log_source", return_value=log_source):
                with mock.patch.object(
                    triage_script,
                    "redact_log_text",
                    return_value=("redacted-log", ["Redacted local filesystem path."]),
                ):
                    with mock.patch.object(triage_script, "parse_log", return_value=parsed_log):
                        with mock.patch.object(
                            triage_script,
                            "build_deterministic_draft",
                            return_value=deterministic_draft,
                        ):
                            with mock.patch.object(
                                triage_script,
                                "build_llm_context",
                                return_value={"context": "value"},
                            ):
                                with mock.patch.object(
                                    triage_script,
                                    "generate_validated_llm_draft",
                                    return_value=llm_result,
                                ):
                                    with mock.patch.object(
                                        triage_script,
                                        "render_managed_comment",
                                        return_value=("managed-body", {}),
                                    ) as render_mock:
                                        with mock.patch.object(
                                            triage_script,
                                            "upsert_managed_comment",
                                            return_value=updated_comment,
                                        ) as upsert_mock:
                                            result = triage_script.run_triage_for_issue(
                                                "FennexFox/NoOfficeDemandFix",
                                                21,
                                                "[Raw Log] test",
                                                RAW_ISSUE_BODY,
                                                "token",
                                            )

        self.assertEqual(result, updated_comment)
        upsert_mock.assert_called_once_with(
            "FennexFox/NoOfficeDemandFix",
            21,
            "managed-body",
            "token",
        )
        reply_fields = render_mock.call_args.args[6]
        self.assertEqual(reply_fields["title"], "LLM title")
        self.assertEqual(reply_fields["evidence_summary"], "LLM summary")
        self.assertEqual(reply_fields["confidence"], "high")
        self.assertEqual(reply_fields["scenario_label"], "New Seoul")

    def test_main_posts_failure_comment_on_automation_error(self) -> None:
        event = {"issue": {"number": 21, "title": "[Raw Log] test", "body": RAW_ISSUE_BODY}}

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
                with mock.patch.object(triage_script, "is_raw_log_issue", return_value=True):
                    with mock.patch.object(
                        triage_script,
                        "run_triage_for_issue",
                        side_effect=triage_script.AutomationError("parse failed"),
                    ):
                        with mock.patch.object(
                            triage_script,
                            "build_raw_log_triage_failure_comment",
                            return_value="triage failed",
                        ):
                            with mock.patch.object(triage_script, "create_issue_comment") as comment_mock:
                                triage_script.main()

        comment_mock.assert_called_once_with(
            "FennexFox/NoOfficeDemandFix",
            21,
            "triage failed",
            "token",
        )


if __name__ == "__main__":
    unittest.main()
