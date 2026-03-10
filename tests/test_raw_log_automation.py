import sys
import textwrap
import unittest
from pathlib import Path
from unittest import mock


REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / ".github" / "scripts"))

import raw_log_automation as automation  # noqa: E402


CURRENT_BRANCH_LOG = textwrap.dedent(
    """
    [2026-03-10 14:28:15,189] [INFO]  Office resource storage patch applied. Outside connections: 6, cargo stations: 28.
    [2026-03-10 14:30:41,511] [INFO]  Signature phantom vacancy guard corrected office property 394316:1 prefab="EE_OfficeSignature02" (36377:1) removed=[PropertyOnMarket]
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T052953590Z, run_id=1, start_day=20, end_day=22, start_sample_index=145, end_sample_index=153, sample_day=22, sample_index=153, sample_slot=1, samples_per_day=2, sample_count=9, trigger=suspicious_state); environment(settings=EnableTradePatch:True,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:2,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=100, company=14196, emptyBuildings=150, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=925211, resourceDemand=411328, companies=27, propertyless=1); electronics(resourceProduction=109125, resourceDemand=351810, companies=11, propertyless=2); softwareProducerOffices(total=27, propertyless=1, efficiencyZero=10, lackResourcesZero=10); softwareConsumerOffices(total=28, propertyless=3, efficiencyZero=0, lackResourcesZero=0, softwareInputZero=0)); diagnostic_context(topFactors=[EmptyBuildings=150, Taxes=100, LocalDemand=58, EducatedWorkforce=30])
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T052953590Z, run_id=1, observation_end_day=22, observation_end_sample_index=153, detail_type=softwareOfficeStates, values=role=producer, company=524397:1, prefab="Office_SoftwareCompany" (364:1), property=276428:1, output=Software, outputStock=0, input1=Electronics(stock=0, buyCost=0.89), efficiency=0, lackResources=0)
    """
).strip()


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

    ### Other mods
    none known

    ### Raw log
    ```text
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(...)
    ```
    """
).strip()

RAW_ISSUE_BODY_WITHOUT_MARKER = textwrap.dedent(
    """
    ### Game version
    1.5.xf1

    ### Mod version
    0.1.1

    ### Save or city label
    New Seoul

    ### What happened
    Loaded the save, enabled diagnostics, and waited 3 in-game days.

    ### Other mods
    none known

    ### Raw log
    ```text
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(...)
    ```
    """
).strip()


class RawLogAutomationTests(unittest.TestCase):
    def test_parse_issue_form_sections(self) -> None:
        parsed = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        self.assertEqual(parsed["game_version"], "1.5.xf1")
        self.assertEqual(parsed["mod_version"], "0.1.1")
        self.assertEqual(parsed["save_or_city_label"], "New Seoul")
        self.assertIn("Loaded the save", parsed["what_happened"])
        self.assertIn("softwareEvidenceDiagnostics", parsed["raw_log"])

    def test_is_raw_log_issue_accepts_github_issue_form_output_without_markdown_marker(self) -> None:
        self.assertTrue(automation.is_raw_log_issue(RAW_ISSUE_BODY_WITHOUT_MARKER, "[Raw Log] test"))
        self.assertFalse(automation.is_raw_log_issue(RAW_ISSUE_BODY_WITHOUT_MARKER, "[Bug] test"))

    def test_select_raw_log_source_prefers_attachment(self) -> None:
        issue_fields = {
            "raw_log": "[NoOfficeDemandFix.Mod.log](https://github.com/user-attachments/files/12345/NoOfficeDemandFix.Mod.log)"
        }
        with mock.patch.object(automation, "download_attachment", return_value="attachment-body") as download_mock:
            source = automation.select_raw_log_source(issue_fields, "token")
        self.assertEqual(source["mode"], "attachment")
        self.assertEqual(source["text"], "attachment-body")
        self.assertEqual(download_mock.call_count, 1)

    def test_redact_log_text_removes_local_paths_and_query_strings(self) -> None:
        redacted, notes = automation.redact_log_text(
            "Current mod asset at C:/Users/techn/AppData/LocalLow/Thing.dll\n"
            "Attachment https://example.com/log.txt?download=1"
        )
        self.assertIn("<redacted-path>", redacted)
        self.assertNotIn("techn", redacted)
        self.assertIn("https://example.com/log.txt", redacted)
        self.assertNotIn("?download=1", redacted)
        self.assertTrue(notes)

    def test_parse_log_uses_current_branch_observation_shape(self) -> None:
        parsed = automation.parse_log(CURRENT_BRANCH_LOG)
        latest = parsed["latest_observation"]
        self.assertIsNotNone(latest)
        self.assertEqual(latest["observation_window"]["sample_slot"], 1)
        self.assertEqual(latest["observation_window"]["samples_per_day"], 2)
        self.assertEqual(latest["settings"]["DiagnosticsSamplesPerDay"], 2)
        self.assertEqual(latest["patch_state"], "debug-build")
        self.assertEqual(parsed["latest_patch_summary"], "Office resource storage patch applied. Outside connections: 6, cargo stations: 28.")
        self.assertEqual(
            automation.derive_symptom_classification(latest["diagnostic_counters"]),
            "software_office_propertyless",
        )
        self.assertIn("role=producer", parsed["latest_software_office_detail"]["values"])

    def test_managed_comment_round_trip_preserves_override_block(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        overrides = {
            "scenario_label": "New Seoul",
            "scenario_type": "existing save",
            "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
            "mod_ref": "track/software-instability @ abc1234",
            "symptom_classification": "software_office_propertyless",
            "evidence_summary": "Maintainer-edited summary.",
            "confounders": "none known",
            "notes": "Maintainer note line 1\nMaintainer note line 2",
        }
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            overrides,
            [],
        )
        parsed = automation.parse_managed_comment(body)
        self.assertEqual(parsed["overrides"]["mod_ref"], "track/software-instability @ abc1234")
        self.assertIn("Maintainer note line 2", parsed["overrides"]["notes"])

    def test_render_managed_comment_formats_markdown_at_column_zero(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            {
                "scenario_label": "New Seoul",
                "scenario_type": "existing save",
                "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                "mod_ref": "",
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "summary",
                "confounders": "none known",
                "notes": "note",
            },
            [],
        )
        self.assertIn("\n## Normalized draft\n", body)
        self.assertIn("\n### Maintainer overrides\n", body)
        self.assertIn("\n```yaml\n", body)
        self.assertNotIn("\n        ## Normalized draft\n", body)
        self.assertNotIn("\n        ```yaml\n", body)

    def test_merge_evidence_fields_and_required_gate(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        body, payload = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            {
                "scenario_label": "New Seoul",
                "scenario_type": "existing save",
                "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                "mod_ref": "track/software-instability @ abc1234",
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "Maintainer-edited summary.",
                "confounders": "none known",
                "notes": "Maintainer note.",
            },
            [],
        )
        parsed = automation.parse_managed_comment(body)
        fields = automation.merge_evidence_fields(
            parsed["payload"],
            parsed["overrides"],
            "https://github.com/example/repo/issues/21#issuecomment-1",
            "https://github.com/example/repo/issues/21",
        )
        required = automation.extract_required_issue_fields(
            str(REPO_ROOT / ".github" / "ISSUE_TEMPLATE" / "software_evidence.yml")
        )
        missing = automation.find_missing_required_fields(fields, required)
        self.assertEqual(missing, [])
        issue_body = automation.render_evidence_issue_body(21, 1, fields)
        self.assertIn("<!-- source-raw-log-issue:21 -->", issue_body)
        self.assertIn("### Mod ref", issue_body)
        self.assertIn("track/software-instability @ abc1234", issue_body)

    def test_generate_llm_suggestions_returns_none_without_token(self) -> None:
        self.assertIsNone(automation.generate_llm_suggestions({"foo": "bar"}, None))

    def test_build_llm_request_payload_uses_github_models_chat_completions_shape(self) -> None:
        payload = automation.build_llm_request_payload({"foo": "bar"})
        self.assertEqual(payload["model"], automation.DEFAULT_GITHUB_MODELS_MODEL)
        self.assertEqual(payload["messages"][0]["role"], "system")
        self.assertEqual(payload["messages"][1]["role"], "user")
        self.assertEqual(payload["response_format"]["type"], "json_schema")
        self.assertEqual(
            payload["response_format"]["json_schema"]["name"],
            "raw_log_triage_suggestions",
        )

    def test_generate_llm_suggestions_parses_github_models_response(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": (
                            '{"symptom_classification":"software_track_unclear",'
                            '"evidence_summary":"summary","confounders":"none",'
                            '"notes":"note","missing_user_input":["mod_ref"],'
                            '"reasoning_summary":"reason"}'
                        )
                    }
                }
            ]
        }
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, response_payload, ""),
        ) as request_mock:
            suggestions = automation.generate_llm_suggestions({"foo": "bar"}, "gh-token")
        self.assertEqual(suggestions["symptom_classification"], "software_track_unclear")
        self.assertEqual(request_mock.call_args.args[1], automation.GITHUB_MODELS_CHAT_COMPLETIONS_URL)


if __name__ == "__main__":
    unittest.main()
