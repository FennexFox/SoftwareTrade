from pathlib import Path
import io
import sys
import textwrap
import unittest
import zipfile
from unittest import mock


SCRIPTS_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS_ROOT))

import perf_telemetry_automation as automation  # noqa: E402


def make_metadata(
    *,
    run_id: str,
    file_kind: str,
    save_name: str = "New Seoul",
    scenario_id: str = "map_a",
    game_version: str = "1.5.xf1",
    mod_version: str = "0.2.0",
    sampling_interval_sec: str = "1",
    stall_threshold_ms: str = "250",
    fix_flags: tuple[bool, bool, bool, bool] = (True, True, True, True),
) -> str:
    return "\n".join(
        [
            "# telemetry_schema_version=1",
            f"# telemetry_file_kind={file_kind}",
            f"# run_id={run_id}",
            "# run_start_utc=2026-03-23T00:00:00.0000000Z",
            f"# game_build_version={game_version}",
            f"# mod_version={mod_version}",
            f"# save_name={save_name}",
            f"# scenario_id={scenario_id}",
            f"# sampling_interval_sec={sampling_interval_sec}",
            f"# stall_threshold_ms={stall_threshold_ms}",
            f"# enable_phantom_vacancy_fix={'true' if fix_flags[0] else 'false'}",
            f"# enable_outside_connection_virtual_seller_fix={'true' if fix_flags[1] else 'false'}",
            f"# enable_virtual_office_resource_buyer_fix={'true' if fix_flags[2] else 'false'}",
            f"# enable_office_demand_direct_patch={'true' if fix_flags[3] else 'false'}",
        ]
    )


BASELINE_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='baseline-run', file_kind='summary')}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    baseline-run,1,100,60,16,18,3,1,0.20,10,1,1,2,false
    baseline-run,2,200,58,17,19,3.2,1.1,0.25,12,0,2,4,false
    baseline-run,3,300,4,350,400,20,25,2.0,50,8,80,120,true
    """
).strip()

BASELINE_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='baseline-run', file_kind='stalls')}
    run_id,stall_id,stall_start_sec,stall_end_sec,stall_duration_sec,stall_peak_render_latency_ms,stall_p95_render_latency_ms,stall_peak_path_queue_len,stall_mod_repath_requested_count,stall_mod_entities_inspected_count
    baseline-run,1,2,6,4,400,380,120,8,50
    """
).strip()

COMPARISON_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary')}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    comparison-run,1,100,55,18,20,3.5,1.3,0.60,10,1,1,3,false
    comparison-run,2,200,54,20,22,3.7,1.6,0.70,12,1,2,6,false
    comparison-run,3,300,3,500,540,25,35,4.0,80,15,120,220,true
    comparison-run,4,400,3,520,560,28,37,4.3,90,20,150,260,true
    """
).strip()

COMPARISON_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='stalls')}
    run_id,stall_id,stall_start_sec,stall_end_sec,stall_duration_sec,stall_peak_render_latency_ms,stall_p95_render_latency_ms,stall_peak_path_queue_len,stall_mod_repath_requested_count,stall_mod_entities_inspected_count
    comparison-run,1,2,8,6,540,520,220,15,80
    comparison-run,2,9,16,7,560,550,260,20,90
    """
).strip()

SAVE_NAME_MISMATCH_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary', save_name='Other City')}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    comparison-run,1,100,55,18,20,3.5,1.3,0.60,10,1,1,3,false
    """
).strip()

SCENARIO_ID_MISMATCH_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary', scenario_id='map_b')}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    comparison-run,1,100,55,18,20,3.5,1.3,0.60,10,1,1,3,false
    """
).strip()

BOTH_IDENTITY_MISMATCH_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary', save_name='Other City', scenario_id='map_b')}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    comparison-run,1,100,55,18,20,3.5,1.3,0.60,10,1,1,3,false
    """
).strip()

MALFORMED_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='baseline-run', file_kind='stalls')}
    run_id,stall_id,stall_start_sec
    baseline-run,1,2
    """
).strip()

INLINE_BASELINE_BUNDLE = (
    "```csv\n" + BASELINE_SUMMARY_CSV + "\n```\n\n```csv\n" + BASELINE_STALLS_CSV + "\n```"
)
INLINE_COMPARISON_BUNDLE = (
    "```csv\n" + COMPARISON_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
)

PERF_ISSUE_BODY = textwrap.dedent(
    f"""
    <!-- performance-telemetry-report -->
    ### Game version
    1.5.xf1

    ### Mod version
    0.2.0

    ### Save or city label
    New Seoul

    ### What changed
    Comparison build adds an experimental buyer-pass optimization.

    ### Platform notes
    Windows release build

    ### Other mods
    none known

    ### Baseline telemetry bundle
    {INLINE_BASELINE_BUNDLE}

    ### Comparison telemetry bundle
    {INLINE_COMPARISON_BUNDLE}
    """
).strip()


class PerfTelemetryAutomationTests(unittest.TestCase):
    def test_parse_issue_form_sections(self) -> None:
        parsed = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        self.assertEqual(parsed["game_version"], "1.5.xf1")
        self.assertEqual(parsed["save_or_city_label"], "New Seoul")
        self.assertIn("experimental buyer-pass optimization", parsed["what_changed"])
        self.assertIn("telemetry_schema_version=1", parsed["baseline_bundle"])

    def test_is_performance_telemetry_issue_accepts_issue_form(self) -> None:
        self.assertTrue(automation.is_performance_telemetry_issue(PERF_ISSUE_BODY, "[Performance Telemetry] test"))
        self.assertFalse(automation.is_performance_telemetry_issue("not an intake", "[Performance Telemetry] test"))

    def test_extract_inline_documents_reads_summary_and_stalls(self) -> None:
        documents, warnings = automation.extract_inline_documents(INLINE_BASELINE_BUNDLE)
        self.assertEqual(warnings, [])
        self.assertIn("run_id,elapsed_sec", documents["summary"])
        self.assertIn("run_id,stall_id", documents["stalls"])

    def test_build_triage_analysis_supports_baseline_only(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = ""

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIsNone(triage.comparison)
        self.assertIn("same-save rerun recommended", triage.follow_up_suggestions)
        self.assertIsNotNone(triage.baseline.steady_state)
        self.assertEqual(triage.baseline.stalls.count, 1)

    def test_build_triage_analysis_computes_comparison_and_flags(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIsNotNone(triage.comparison)
        self.assertTrue(triage.comparison_analysis.directly_comparable)
        self.assertGreater(triage.comparison_analysis.steady_state_mod_update_delta_ms, 0.25)
        self.assertIn("steady_state_mod_overhead_elevated", triage.anomaly_flags)
        self.assertIn("stall_frequency_elevated", triage.anomaly_flags)
        self.assertIn("queue_pressure_during_stalls", triage.anomaly_flags)

    def test_build_triage_analysis_allows_same_scenario_when_save_names_differ(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + SAVE_NAME_MISMATCH_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertTrue(triage.comparison_analysis.directly_comparable)
        self.assertIn("save_name mismatch", " ".join(triage.comparison_analysis.warnings))
        self.assertIn("scenario_id matches", " ".join(triage.comparison_analysis.warnings))

    def test_build_triage_analysis_allows_same_save_when_scenario_ids_differ(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + SCENARIO_ID_MISMATCH_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertTrue(triage.comparison_analysis.directly_comparable)
        self.assertIn("scenario_id mismatch", " ".join(triage.comparison_analysis.warnings))
        self.assertIn("save_name matches", " ".join(triage.comparison_analysis.warnings))

    def test_build_triage_analysis_rejects_runs_when_save_and_scenario_both_differ(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + BOTH_IDENTITY_MISMATCH_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertFalse(triage.comparison_analysis.directly_comparable)
        self.assertIn("save_name mismatch", " ".join(triage.comparison_analysis.warnings))
        self.assertIn("scenario_id mismatch", " ".join(triage.comparison_analysis.warnings))
        self.assertIn(
            "matching save or scenario identity could not be verified",
            " ".join(triage.comparison_analysis.warnings),
        )

    def test_missing_stall_file_is_nonfatal_warning(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["baseline_bundle"] = "```csv\n" + BASELINE_SUMMARY_CSV + "\n```"
        issue_fields["comparison_bundle"] = ""

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIn("missing `perf_stalls.csv`", " ".join(triage.baseline.warnings))
        self.assertIn("include perf_stalls.csv in the next capture", triage.follow_up_suggestions)

    def test_malformed_stall_file_is_nonfatal_warning(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["baseline_bundle"] = (
            "```csv\n" + BASELINE_SUMMARY_CSV + "\n```\n\n```csv\n" + MALFORMED_STALLS_CSV + "\n```"
        )
        issue_fields["comparison_bundle"] = ""

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIn("unreadable `perf_stalls.csv`", " ".join(triage.baseline.warnings))
        self.assertIsNotNone(triage.baseline.steady_state)

    def test_load_bundle_documents_supports_csv_attachments(self) -> None:
        field_text = "\n".join(
            [
                "https://github.com/user-attachments/files/baseline/perf_summary.csv",
                "https://github.com/user-attachments/files/baseline/perf_stalls.csv",
            ]
        )

        with mock.patch.object(
            automation,
            "download_attachment_bytes",
            side_effect=[BASELINE_SUMMARY_CSV.encode("utf-8"), BASELINE_STALLS_CSV.encode("utf-8")],
        ):
            source_mode, _, documents, warnings = automation.load_bundle_documents(field_text, "Baseline telemetry bundle")

        self.assertEqual(source_mode, "attachment")
        self.assertEqual(warnings, [])
        self.assertIn("summary", documents)
        self.assertIn("stalls", documents)

    def test_load_bundle_documents_supports_zip_attachment(self) -> None:
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("perf_summary.csv", BASELINE_SUMMARY_CSV)
            archive.writestr("perf_stalls.csv", BASELINE_STALLS_CSV)

        with mock.patch.object(automation, "download_attachment_bytes", return_value=buffer.getvalue()):
            source_mode, source_description, documents, warnings = automation.load_bundle_documents(
                "https://github.com/user-attachments/files/baseline/baseline.zip",
                "Baseline telemetry bundle",
            )

        self.assertEqual(source_mode, "attachment")
        self.assertIn("zip attachment", source_description)
        self.assertEqual(warnings, [])
        self.assertIn("summary", documents)
        self.assertIn("stalls", documents)

    def test_render_managed_comment_keeps_output_observational(self) -> None:
        triage = automation.build_triage_analysis(21, automation.parse_issue_form_sections(PERF_ISSUE_BODY))

        comment = automation.render_managed_comment(triage)

        self.assertIn("## Run summary", comment)
        self.assertIn("## Comparison", comment)
        self.assertIn("## Anomaly flags / follow-up suggestions", comment)
        self.assertIn(automation.PERF_TELEMETRY_PAYLOAD_START_MARKER, comment)
        self.assertNotIn("root cause", comment.lower())
        self.assertNotIn("mod caused the stall", comment.lower())
