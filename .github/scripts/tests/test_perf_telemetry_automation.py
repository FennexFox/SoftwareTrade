from pathlib import Path
import io
import sys
import textwrap
from typing import TypeVar
import unittest
import zipfile
from unittest import mock


SCRIPTS_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS_ROOT))

import perf_telemetry_automation as automation  # noqa: E402


T = TypeVar("T")


def require_not_none(value: T | None) -> T:
    if value is None:
        raise AssertionError("Expected value to be present")
    return value


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
    fix_flags: tuple[bool | None, bool | None, bool | None, bool | None] = (True, True, True, True),
) -> str:
    lines = [
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
    ]
    for field_name, value in (
        ("enable_phantom_vacancy_fix", fix_flags[0]),
        ("enable_outside_connection_virtual_seller_fix", fix_flags[1]),
        ("enable_virtual_office_resource_buyer_fix", fix_flags[2]),
        ("enable_office_demand_direct_patch", fix_flags[3]),
    ):
        if value is None:
            continue
        lines.append(f"# {field_name}={'true' if value else 'false'}")
    return "\n".join(lines)


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

SINGLE_TOGGLE_COMPARISON_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary', fix_flags=(True, True, False, True))}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    comparison-run,1,100,55,18,20,3.5,1.3,0.60,10,1,1,3,false
    comparison-run,2,200,54,20,22,3.7,1.6,0.70,12,1,2,6,false
    comparison-run,3,300,3,500,540,25,35,4.0,80,15,120,220,true
    comparison-run,4,400,3,520,560,28,37,4.3,90,20,150,260,true
    """
).strip()

SINGLE_TOGGLE_COMPARISON_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='stalls', fix_flags=(True, True, False, True))}
    run_id,stall_id,stall_start_sec,stall_end_sec,stall_duration_sec,stall_peak_render_latency_ms,stall_p95_render_latency_ms,stall_peak_path_queue_len,stall_mod_repath_requested_count,stall_mod_entities_inspected_count
    comparison-run,1,2,8,6,540,520,220,15,80
    comparison-run,2,9,16,7,560,550,260,20,90
    """
).strip()

MULTI_TOGGLE_COMPARISON_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary', fix_flags=(False, True, False, True))}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    comparison-run,1,100,55,18,20,3.5,1.3,0.60,10,1,1,3,false
    comparison-run,2,200,54,20,22,3.7,1.6,0.70,12,1,2,6,false
    comparison-run,3,300,3,500,540,25,35,4.0,80,15,120,220,true
    comparison-run,4,400,3,520,560,28,37,4.3,90,20,150,260,true
    """
).strip()

MULTI_TOGGLE_COMPARISON_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='stalls', fix_flags=(False, True, False, True))}
    run_id,stall_id,stall_start_sec,stall_end_sec,stall_duration_sec,stall_peak_render_latency_ms,stall_p95_render_latency_ms,stall_peak_path_queue_len,stall_mod_repath_requested_count,stall_mod_entities_inspected_count
    comparison-run,1,2,8,6,540,520,220,15,80
    comparison-run,2,9,16,7,560,550,260,20,90
    """
).strip()

UNKNOWN_TOGGLE_COMPARISON_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary', fix_flags=(True, True, None, True))}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    comparison-run,1,100,55,18,20,3.5,1.3,0.60,10,1,1,3,false
    comparison-run,2,200,54,20,22,3.7,1.6,0.70,12,1,2,6,false
    comparison-run,3,300,3,500,540,25,35,4.0,80,15,120,220,true
    comparison-run,4,400,3,520,560,28,37,4.3,90,20,150,260,true
    """
).strip()

UNKNOWN_TOGGLE_COMPARISON_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='stalls', fix_flags=(True, True, None, True))}
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

TERMINAL_ARTIFACT_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='baseline-run', file_kind='summary')}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    baseline-run,1.000000,100,60,16,18,3,1,0.20,10,1,1,2,false
    baseline-run,2.000000,200,58,17,19,3.2,1.1,0.25,12,0,2,4,false
    baseline-run,2.000500,200,4000,0.25,0.30,0,0,0.05,3,0,2,4,false
    """
).strip()

MALFORMED_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='baseline-run', file_kind='stalls')}
    run_id,stall_id,stall_start_sec
    baseline-run,1,2
    """
).strip()

MALFORMED_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='comparison-run', file_kind='summary')}
    run_id,elapsed_sec,simulation_tick
    comparison-run,1,100
    """
).strip()

MISMATCHED_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='wrong-run', file_kind='stalls', save_name='Wrong City', scenario_id='map_z')}
    run_id,stall_id,stall_start_sec,stall_end_sec,stall_duration_sec,stall_peak_render_latency_ms,stall_p95_render_latency_ms,stall_peak_path_queue_len,stall_mod_repath_requested_count,stall_mod_entities_inspected_count
    wrong-run,1,2,8,6,540,520,220,15,80
    """
).strip()

ZERO_QUEUE_PRESSURE_SUMMARY_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='baseline-run', file_kind='summary')}
    run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window
    baseline-run,1,100,60,16,18,3,0.20,0.05,10,1,120,0,false
    baseline-run,2,200,58,17,19,3.2,0.25,0.06,12,0,180,0,false
    baseline-run,3,300,4,350,400,20,5.0,0.50,20,2,250,0,true
    """
).strip()

ZERO_QUEUE_PRESSURE_STALLS_CSV = textwrap.dedent(
    f"""
    {make_metadata(run_id='baseline-run', file_kind='stalls')}
    run_id,stall_id,stall_start_sec,stall_end_sec,stall_duration_sec,stall_peak_render_latency_ms,stall_p95_render_latency_ms,stall_peak_path_queue_len,stall_mod_repath_requested_count,stall_mod_entities_inspected_count
    baseline-run,1,2,6,4,400,380,0,2,20
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
        baseline_stalls = require_not_none(triage.baseline.stalls)

        self.assertIsNone(triage.comparison)
        self.assertFalse(triage.comparison_bundle_provided)
        self.assertIsNone(triage.comparison_load_error)
        self.assertIn("same-save rerun recommended", triage.follow_up_suggestions)
        self.assertIsNotNone(triage.baseline.steady_state)
        self.assertEqual(baseline_stalls.count, 1)

    def test_build_triage_analysis_warns_when_queue_metrics_look_unbound(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["baseline_bundle"] = (
            "```csv\n" + ZERO_QUEUE_PRESSURE_SUMMARY_CSV + "\n```\n\n```csv\n" + ZERO_QUEUE_PRESSURE_STALLS_CSV + "\n```"
        )
        issue_fields["comparison_bundle"] = ""

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIn("path queue metrics are all zero", " ".join(triage.baseline.warnings))
        self.assertIn("telemetry bind errors", " ".join(triage.warnings))

    def test_build_triage_analysis_computes_comparison_and_flags(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison_analysis = require_not_none(triage.comparison_analysis)
        mod_update_delta_ms = require_not_none(comparison_analysis.steady_state_mod_update_delta_ms)

        self.assertIsNotNone(triage.comparison)
        self.assertTrue(comparison_analysis.directly_comparable)
        self.assertGreater(mod_update_delta_ms, 0.25)
        self.assertIn("steady_state_mod_overhead_elevated", triage.anomaly_flags)
        self.assertIn("stall_frequency_elevated", triage.anomaly_flags)
        self.assertIn("queue_pressure_during_stalls", triage.anomaly_flags)

    def test_build_triage_analysis_allows_same_scenario_when_save_names_differ(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + SAVE_NAME_MISMATCH_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertTrue(comparison_analysis.directly_comparable)
        self.assertIn("save_name mismatch", " ".join(comparison_analysis.warnings))
        self.assertIn("scenario_id matches", " ".join(comparison_analysis.warnings))

    def test_build_triage_analysis_allows_same_save_when_scenario_ids_differ(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + SCENARIO_ID_MISMATCH_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertTrue(comparison_analysis.directly_comparable)
        self.assertIn("scenario_id mismatch", " ".join(comparison_analysis.warnings))
        self.assertIn("save_name matches", " ".join(comparison_analysis.warnings))

    def test_build_triage_analysis_allows_single_fix_toggle_delta(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n"
            + SINGLE_TOGGLE_COMPARISON_SUMMARY_CSV
            + "\n```\n\n```csv\n"
            + SINGLE_TOGGLE_COMPARISON_STALLS_CSV
            + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertTrue(comparison_analysis.directly_comparable)
        self.assertEqual(comparison_analysis.comparability_basis, "single_fix_toggle_delta")
        self.assertEqual(comparison_analysis.fix_toggle_differences, ["EnableVirtualOfficeResourceBuyerFix"])
        self.assertIsNotNone(comparison_analysis.steady_state_mod_update_delta_ms)

        comment = automation.render_managed_comment(triage)
        payload = automation.build_machine_payload(triage)

        self.assertIn("Direct comparison status: comparable (single fix-toggle delta)", comment)
        self.assertIn("Fix-toggle delta: `EnableVirtualOfficeResourceBuyerFix`", comment)
        self.assertEqual(payload["comparison_analysis"]["comparability_basis"], "single_fix_toggle_delta")

    def test_build_triage_analysis_rejects_multiple_fix_toggle_deltas(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n"
            + MULTI_TOGGLE_COMPARISON_SUMMARY_CSV
            + "\n```\n\n```csv\n"
            + MULTI_TOGGLE_COMPARISON_STALLS_CSV
            + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertFalse(comparison_analysis.directly_comparable)
        self.assertIn("spans multiple fix toggles", " ".join(comparison_analysis.warnings))
        self.assertIn("EnablePhantomVacancyFix", " ".join(comparison_analysis.warnings))
        self.assertIn("EnableVirtualOfficeResourceBuyerFix", " ".join(comparison_analysis.warnings))

    def test_build_triage_analysis_rejects_unknown_fix_toggle_state(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n"
            + UNKNOWN_TOGGLE_COMPARISON_SUMMARY_CSV
            + "\n```\n\n```csv\n"
            + UNKNOWN_TOGGLE_COMPARISON_STALLS_CSV
            + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertFalse(comparison_analysis.directly_comparable)
        self.assertIn("enabled-fix state could not be fully verified", " ".join(comparison_analysis.warnings))
        self.assertIn("EnableVirtualOfficeResourceBuyerFix", " ".join(comparison_analysis.warnings))

    def test_build_triage_analysis_rejects_runs_when_save_and_scenario_both_differ(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + BOTH_IDENTITY_MISMATCH_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertFalse(comparison_analysis.directly_comparable)
        self.assertIn("save_name mismatch", " ".join(comparison_analysis.warnings))
        self.assertIn("scenario_id mismatch", " ".join(comparison_analysis.warnings))
        self.assertIn(
            "matching save or scenario identity could not be verified",
            " ".join(comparison_analysis.warnings),
        )

    def test_missing_stall_file_is_nonfatal_warning(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["baseline_bundle"] = "```csv\n" + BASELINE_SUMMARY_CSV + "\n```"
        issue_fields["comparison_bundle"] = ""

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIsNone(triage.baseline.stalls)
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
        self.assertIsNone(triage.baseline.stalls)

    def test_terminal_flush_artifact_row_is_ignored_in_summary_analysis(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["baseline_bundle"] = "```csv\n" + TERMINAL_ARTIFACT_SUMMARY_CSV + "\n```"
        issue_fields["comparison_bundle"] = ""

        triage = automation.build_triage_analysis(21, issue_fields)
        steady_state = require_not_none(triage.baseline.steady_state)

        self.assertEqual(triage.baseline.summary_row_count, 2)
        self.assertEqual(steady_state.window_count, 2)
        self.assertLess(steady_state.fps_mean, 100.0)
        self.assertIn("ignored 1 likely terminal flush artifact row", " ".join(triage.baseline.warnings))

    def test_mismatched_stall_file_is_ignored_for_run_summary(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + COMPARISON_SUMMARY_CSV + "\n```\n\n```csv\n" + MISMATCHED_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison = require_not_none(triage.comparison)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertTrue(comparison_analysis.directly_comparable)
        self.assertIsNone(comparison.stalls)
        self.assertIsNone(comparison_analysis.stall_count_delta)
        self.assertIn("do not share the same `run_id`", " ".join(comparison.warnings))
        self.assertIn("was ignored because its telemetry metadata does not match", " ".join(comparison.warnings))

    def test_missing_comparison_stall_file_suppresses_stall_deltas(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = "```csv\n" + COMPARISON_SUMMARY_CSV + "\n```"

        triage = automation.build_triage_analysis(21, issue_fields)
        comparison = require_not_none(triage.comparison)
        comparison_analysis = require_not_none(triage.comparison_analysis)

        self.assertTrue(comparison_analysis.directly_comparable)
        self.assertIsNone(comparison.stalls)
        self.assertIsNone(comparison_analysis.stall_count_delta)
        self.assertIn("stall deltas are unavailable", " ".join(comparison_analysis.warnings))
        self.assertNotIn("stall_frequency_elevated", triage.anomaly_flags)

    def test_invalid_optional_comparison_bundle_falls_back_to_baseline_only(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = "```csv\n" + MALFORMED_SUMMARY_CSV + "\n```"

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIsNone(triage.comparison)
        self.assertIsNone(triage.comparison_analysis)
        self.assertTrue(triage.comparison_bundle_provided)
        self.assertIn("Unexpected telemetry CSV header", require_not_none(triage.comparison_load_error))
        self.assertIsNotNone(triage.baseline.steady_state)
        self.assertIn("Comparison telemetry bundle was ignored", " ".join(triage.warnings))
        self.assertIn("same-save rerun recommended", triage.follow_up_suggestions)

    def test_invalid_required_baseline_summary_bundle_raises(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["baseline_bundle"] = "```csv\n" + MALFORMED_SUMMARY_CSV + "\n```"
        issue_fields["comparison_bundle"] = ""

        with self.assertRaisesRegex(automation.AutomationError, "Unexpected telemetry CSV header"):
            automation.build_triage_analysis(21, issue_fields)

    def test_invalid_optional_comparison_bundle_with_companion_stalls_falls_back_to_baseline_only(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = (
            "```csv\n" + MALFORMED_SUMMARY_CSV + "\n```\n\n```csv\n" + COMPARISON_STALLS_CSV + "\n```"
        )

        triage = automation.build_triage_analysis(21, issue_fields)

        self.assertIsNone(triage.comparison)
        self.assertIsNone(triage.comparison_analysis)
        self.assertTrue(triage.comparison_bundle_provided)
        self.assertIn("Unexpected telemetry CSV header", require_not_none(triage.comparison_load_error))
        self.assertIsNotNone(triage.baseline.steady_state)
        self.assertIn("Comparison telemetry bundle was ignored", " ".join(triage.warnings))
        self.assertIn("same-save rerun recommended", triage.follow_up_suggestions)

    def test_invalid_optional_comparison_bundle_truncates_error_detail(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = "supplied but unreadable"
        original_load_bundle_analysis = automation.load_bundle_analysis

        def fake_load_bundle_analysis(
            raw_field_text: str,
            *,
            label: str,
            field_label: str,
            required: bool,
        ) -> tuple[automation.RunAnalysis | None, list[str]]:
            if label == "Baseline":
                return original_load_bundle_analysis(
                    raw_field_text,
                    label=label,
                    field_label=field_label,
                    required=required,
                )
            raise automation.AutomationError("X" * 2000)

        with mock.patch.object(automation, "load_bundle_analysis", side_effect=fake_load_bundle_analysis):
            triage = automation.build_triage_analysis(21, issue_fields)

        comparison_load_error = require_not_none(triage.comparison_load_error)
        payload = automation.build_machine_payload(triage)

        self.assertEqual(len(comparison_load_error), automation.COMPARISON_LOAD_ERROR_LIMIT)
        self.assertTrue(comparison_load_error.endswith("..."))
        self.assertIn(comparison_load_error, " ".join(triage.warnings))
        self.assertEqual(payload["comparison_bundle"]["load_error"], comparison_load_error)

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

    def test_load_bundle_documents_skips_non_telemetry_attachments(self) -> None:
        field_text = "\n".join(
            [
                "https://github.com/user-attachments/files/baseline/perf_summary.csv",
                "https://github.com/user-attachments/files/baseline/screenshot.txt",
                "https://github.com/user-attachments/files/baseline/perf_stalls.csv",
            ]
        )

        with mock.patch.object(
            automation,
            "download_attachment_bytes",
            side_effect=[
                BASELINE_SUMMARY_CSV.encode("utf-8"),
                b"this is not telemetry csv content",
                BASELINE_STALLS_CSV.encode("utf-8"),
            ],
        ):
            source_mode, _, documents, warnings = automation.load_bundle_documents(field_text, "Baseline telemetry bundle")

        self.assertEqual(source_mode, "attachment")
        self.assertIn("summary", documents)
        self.assertIn("stalls", documents)
        self.assertIn("skipped non-telemetry attachment", " ".join(warnings))

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

    def test_load_bundle_documents_falls_back_after_non_telemetry_zip(self) -> None:
        telemetry_free_zip = io.BytesIO()
        with zipfile.ZipFile(telemetry_free_zip, "w") as archive:
            archive.writestr("notes.txt", "not telemetry")

        field_text = "\n".join(
            [
                "https://github.com/user-attachments/files/baseline/unrelated.zip",
                "https://github.com/user-attachments/files/baseline/perf_summary.csv",
                "https://github.com/user-attachments/files/baseline/perf_stalls.csv",
            ]
        )

        with mock.patch.object(
            automation,
            "download_attachment_bytes",
            side_effect=[
                telemetry_free_zip.getvalue(),
                BASELINE_SUMMARY_CSV.encode("utf-8"),
                BASELINE_STALLS_CSV.encode("utf-8"),
            ],
        ):
            source_mode, source_description, documents, warnings = automation.load_bundle_documents(
                field_text,
                "Baseline telemetry bundle",
            )

        self.assertEqual(source_mode, "attachment")
        self.assertEqual(source_description, "attachment CSV pair")
        self.assertIn("summary", documents)
        self.assertIn("stalls", documents)
        self.assertIn("skipped zip attachment", " ".join(warnings))

    def test_render_managed_comment_keeps_output_observational(self) -> None:
        triage = automation.build_triage_analysis(21, automation.parse_issue_form_sections(PERF_ISSUE_BODY))

        comment = automation.render_managed_comment(triage)

        self.assertIn("## Run summary", comment)
        self.assertIn("## Comparison", comment)
        self.assertIn("## Anomaly flags / follow-up suggestions", comment)
        self.assertIn(automation.PERF_TELEMETRY_PAYLOAD_START_MARKER, comment)
        self.assertNotIn("root cause", comment.lower())
        self.assertNotIn("mod caused the stall", comment.lower())

    def test_render_managed_comment_distinguishes_ignored_comparison_bundle(self) -> None:
        issue_fields = automation.parse_issue_form_sections(PERF_ISSUE_BODY)
        issue_fields["comparison_bundle"] = "```csv\n" + MALFORMED_SUMMARY_CSV + "\n```"

        triage = automation.build_triage_analysis(21, issue_fields)
        comment = automation.render_managed_comment(triage)
        payload = automation.build_machine_payload(triage)

        self.assertIn("A comparison telemetry bundle was provided but could not be used.", comment)
        self.assertIn("See the warnings above for the comparison bundle failure reason.", comment)
        self.assertNotIn("No comparison telemetry bundle was provided.", comment)
        self.assertTrue(payload["comparison_bundle"]["provided"])
        self.assertFalse(payload["comparison_bundle"]["loaded"])
        self.assertIn("Unexpected telemetry CSV header", payload["comparison_bundle"]["load_error"])

    def test_find_perf_telemetry_managed_comment_uses_perf_marker(self) -> None:
        comments = [
            {"id": 1, "body": "<!-- raw-log-triage:managed-comment -->", "created_at": "2026-03-24T00:00:00Z"},
            {
                "id": 2,
                "body": automation.PERF_TELEMETRY_MANAGED_COMMENT_MARKER + "\nold",
                "created_at": "2026-03-24T00:01:00Z",
            },
            {
                "id": 3,
                "body": automation.PERF_TELEMETRY_MANAGED_COMMENT_MARKER + "\nnew",
                "updated_at": "2026-03-24T00:02:00Z",
            },
        ]

        managed_comment = automation.find_perf_telemetry_managed_comment(comments)

        self.assertIsNotNone(managed_comment)
        self.assertEqual(managed_comment["id"], 3)

    def test_upsert_perf_telemetry_managed_comment_updates_existing_perf_comment(self) -> None:
        comments = [
            {"id": 10, "body": "<!-- raw-log-triage:managed-comment -->", "created_at": "2026-03-24T00:00:00Z"},
            {
                "id": 11,
                "body": automation.PERF_TELEMETRY_MANAGED_COMMENT_MARKER + "\nexisting",
                "updated_at": "2026-03-24T00:01:00Z",
            },
        ]

        with mock.patch.object(automation, "get_issue_comments", return_value=comments):
            with mock.patch.object(automation, "update_issue_comment", return_value={"id": 11}) as update_mock:
                with mock.patch.object(automation, "create_issue_comment") as create_mock:
                    result = automation.upsert_perf_telemetry_managed_comment(
                        "FennexFox/NoOfficeDemandFix",
                        100,
                        "managed-body",
                        "token",
                    )

        self.assertEqual(result, {"id": 11})
        update_mock.assert_called_once_with("FennexFox/NoOfficeDemandFix", 11, "managed-body", "token")
        create_mock.assert_not_called()
