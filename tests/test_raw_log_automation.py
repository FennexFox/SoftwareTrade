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

    def test_build_deterministic_draft_includes_checklist_confounders(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        draft = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        self.assertIn("patch_state=debug-build", draft["confounders"])
        self.assertIn("trade patch enabled during capture", draft["confounders"])

    def test_managed_comment_round_trip_preserves_override_block(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        reply_fields = {
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
            reply_fields,
            [],
            "skipped",
            "no eligible observation",
        )
        parsed = automation.parse_managed_comment(body)
        self.assertEqual(parsed["reply_template"]["mod_ref"], "track/software-instability @ abc1234")
        self.assertIn("Maintainer note line 2", parsed["reply_template"]["notes"])

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
            "failed",
            "http_403: models access denied",
        )
        self.assertIn("\n## Normalized draft\n", body)
        self.assertIn("\n### Maintainer reply template\n", body)
        self.assertIn("\n```yaml\n", body)
        self.assertNotIn("\n        ## Normalized draft\n", body)
        self.assertNotIn("\n        ```yaml\n", body)
        self.assertIn("- LLM status: `failed`", body)
        self.assertIn("- LLM detail: `http_403: models access denied`", body)
        self.assertIn("/promote-evidence", body)

    def test_render_managed_comment_keeps_preview_short_but_reply_yaml_full(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        long_notes = "A" * 800
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
                "notes": long_notes,
            },
            [],
            "skipped",
            "no eligible observation",
        )
        self.assertIn(f"`{'A' * 597}...`", body)
        self.assertIn(long_notes, body)

    def test_render_managed_comment_shows_full_reasoning_and_plain_yaml_guidance(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        full_reasoning = "B" * 460
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            {
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "summary",
                "confounders": "none known",
                "notes": "note",
                "missing_user_input": ["mod_ref"],
                "reasoning_summary": full_reasoning,
            },
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
            "enabled",
            automation.DEFAULT_GITHUB_MODELS_MODEL,
        )
        self.assertIn(full_reasoning, body)
        self.assertIn("plain YAML is accepted", body)
        self.assertIn("code fences are optional", body)

    def test_render_managed_comment_compact_fallback_truncates_reasoning(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        long_reasoning = "C" * 450
        with mock.patch.object(automation, "COMMENT_BODY_LIMIT", 2500):
            body, _ = automation.render_managed_comment(
                21,
                issue_fields,
                log_source,
                parsed_log,
                deterministic,
                {
                    "symptom_classification": "software_office_propertyless",
                    "evidence_summary": "summary",
                    "confounders": "none known",
                    "notes": "note",
                    "missing_user_input": ["mod_ref"],
                    "reasoning_summary": long_reasoning,
                },
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
                "enabled",
                automation.DEFAULT_GITHUB_MODELS_MODEL,
            )
        self.assertIn("### Draft provenance", body)
        self.assertIn(f"`{'C' * 397}...`", body)

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
            "skipped",
            "no eligible observation",
        )
        parsed = automation.parse_managed_comment(body)
        fields = automation.merge_evidence_fields(
            parsed["payload"],
            parsed["reply_template"],
            "https://github.com/example/repo/issues/21#issuecomment-1",
            "https://github.com/example/repo/issues/21",
            "https://github.com/example/repo/issues/21#issuecomment-2",
        )
        required = automation.extract_required_issue_fields(
            str(REPO_ROOT / ".github" / "ISSUE_TEMPLATE" / "software_evidence.yml")
        )
        missing = automation.find_missing_required_fields(fields, required)
        self.assertEqual(missing, [])
        issue_body = automation.render_evidence_issue_body(21, 1, fields)
        self.assertIn("<!-- source-raw-log-issue:21 -->", issue_body)
        self.assertIn("symptom classification in this issue is provisional", issue_body)
        self.assertIn("### Observation window", issue_body)
        self.assertIn("session_id: 20260310T052953590Z", issue_body)
        self.assertIn("### Settings", issue_body)
        self.assertIn("EnableTradePatch: True", issue_body)
        self.assertIn("### Diagnostic counters", issue_body)
        self.assertIn("softwareProducerOffices:", issue_body)
        self.assertIn("### Log excerpt\nRaw string\n```text\nsoftwareEvidenceDiagnostics detail(", issue_body)
        self.assertIn("Raw string", issue_body)
        self.assertIn("### Mod ref", issue_body)
        self.assertIn("track/software-instability @ abc1234", issue_body)
        self.assertIn("maintainer promote reply: https://github.com/example/repo/issues/21#issuecomment-2", fields["artifacts"])

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
        self.assertIn("lackResourcesZero", payload["messages"][0]["content"])
        self.assertIn("zero resources", payload["messages"][0]["content"])
        self.assertIn("Do not mention the chosen symptom label", payload["messages"][0]["content"])
        self.assertIn("Put label-selection rationale and interpretation only in `reasoning_summary`", payload["messages"][0]["content"])
        self.assertIn("do not speculate about root cause", payload["messages"][0]["content"])

    def test_build_llm_context_excludes_raw_log_and_caps_excerpt(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        issue_fields["raw_log"] = "X" * 5000
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        self.assertNotIn("raw_log", context["raw_issue"])
        self.assertIn("latest_software_office_detail_excerpt", context)
        self.assertLessEqual(
            len(context["latest_software_office_detail_excerpt"]),
            automation.LLM_DETAIL_EXCERPT_LIMIT,
        )
        self.assertIn("semantic_facts", context)
        self.assertTrue(any("lackResourcesZero" in fact for fact in context["semantic_facts"]))

    def test_build_llm_context_size_stays_small_even_with_huge_raw_issue_body(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        issue_fields["raw_log"] = "Y" * 50000
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        serialized = automation.json.dumps(context, ensure_ascii=True)
        self.assertLess(len(serialized), 12000)

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

    def test_generate_llm_suggestions_rewrites_unsupported_zero_resources_wording(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": (
                            '{"symptom_classification":"software_office_propertyless",'
                            '"evidence_summary":"8 producer offices at zero efficiency and zero resources.",'
                            '"confounders":"none","notes":"note","missing_user_input":["mod_ref"],'
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
        ):
            suggestions = automation.generate_llm_suggestions({"foo": "bar"}, "gh-token")
        self.assertNotIn("zero resources", suggestions["evidence_summary"].lower())
        self.assertIn("lackResources=0", suggestions["evidence_summary"])

    def test_parse_reply_comment_and_command(self) -> None:
        comment_body = textwrap.dedent(
            """
            Ready to promote.

            /promote-evidence

            ```yaml
            maintainer_reply:
              scenario_label: "New Seoul"
              scenario_type: "existing save"
              reproduction_conditions: |
                Loaded the same save.
                Waited 3 in-game days.
              mod_ref: "track/software-instability @ abc1234"
              symptom_classification: "software_office_propertyless"
              evidence_summary: "summary"
              confounders: |
                - patch_state=debug-build
                - trade patch enabled during capture
              notes: "note"
            ```
            """
        ).strip()
        parsed = automation.parse_reply_comment(comment_body)
        self.assertTrue(automation.comment_has_promote_command(comment_body))
        self.assertEqual(parsed["mod_ref"], "track/software-instability @ abc1234")
        self.assertIn("trade patch enabled", parsed["confounders"])

    def test_parse_reply_comment_accepts_plain_copied_yaml(self) -> None:
        comment_body = textwrap.dedent(
            """
            maintainer_reply:
              scenario_label: "New Seoul"
              scenario_type: "existing save"
              reproduction_conditions: |
                Loaded the same save.
                Waited 3 in-game days.
              mod_ref: "track/software-instability @ abc1234"
              symptom_classification: "software_office_propertyless"
              evidence_summary: "summary"
              confounders: |
                - patch_state=debug-build
                - trade patch enabled during capture
              notes: "note"

            /promote-evidence
            """
        ).strip()
        parsed = automation.parse_reply_comment(comment_body)
        self.assertTrue(automation.comment_has_promote_command(comment_body))
        self.assertEqual(parsed["mod_ref"], "track/software-instability @ abc1234")
        self.assertIn("trade patch enabled", parsed["confounders"])

    def test_parse_reply_comment_returns_blank_for_missing_or_malformed_yaml(self) -> None:
        missing = automation.parse_reply_comment("/promote-evidence")
        malformed = automation.parse_reply_comment(
            "maintainer_reply:\nmod_ref: \"track/software-instability @ abc1234\"\n/promote-evidence"
        )
        self.assertFalse(automation.has_nonempty_reply_fields(missing))
        self.assertFalse(automation.has_nonempty_reply_fields(malformed))

    def test_find_latest_reply_comment_ignores_bot_and_nonmaintainer(self) -> None:
        comments = [
            {
                "id": 1,
                "body": "```yaml\nmaintainer_reply:\n  mod_ref: \"one\"\n```",
                "user": {"login": "github-actions[bot]"},
                "author_association": "NONE",
                "updated_at": "2026-03-10T09:00:00Z",
                "created_at": "2026-03-10T09:00:00Z",
            },
            {
                "id": 2,
                "body": "```yaml\nmaintainer_reply:\n  mod_ref: \"two\"\n```",
                "user": {"login": "random-user"},
                "author_association": "CONTRIBUTOR",
                "updated_at": "2026-03-10T09:01:00Z",
                "created_at": "2026-03-10T09:01:00Z",
            },
            {
                "id": 3,
                "body": "```yaml\nmaintainer_reply:\n  mod_ref: \"three\"\n```",
                "user": {"login": "repo-owner"},
                "author_association": "OWNER",
                "updated_at": "2026-03-10T09:02:00Z",
                "created_at": "2026-03-10T09:02:00Z",
            },
        ]
        latest = automation.find_latest_reply_comment(comments)
        self.assertIsNotNone(latest)
        self.assertEqual(latest["id"], 3)

    def test_find_latest_reply_comment_accepts_plain_yaml_reply(self) -> None:
        comments = [
            {
                "id": 1,
                "body": "maintainer_reply:\n  mod_ref: \"one\"\n\n/promote-evidence",
                "user": {"login": "repo-owner"},
                "author_association": "OWNER",
                "updated_at": "2026-03-10T09:00:00Z",
                "created_at": "2026-03-10T09:00:00Z",
            }
        ]
        latest = automation.find_latest_reply_comment(comments)
        self.assertIsNotNone(latest)
        self.assertEqual(latest["id"], 1)

    def test_sanitize_llm_detail_maps_common_failures(self) -> None:
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (403): access denied"),
            "http_403: models access denied",
        )
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (429): rate limited"),
            "http_429: rate limited",
        )
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (413): payload too large"),
            "http_413: payload too large",
        )
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (500): unexpected upstream error"),
            "http_500: request failed",
        )


if __name__ == "__main__":
    unittest.main()
