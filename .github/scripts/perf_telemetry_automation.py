import csv
import io
import json
import math
import re
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from dataclasses import asdict, dataclass, field
from pathlib import PurePosixPath
from typing import Any

from raw_log_automation import (
    AttachmentDownloadError,
    AutomationError,
    clean_issue_form_value,
    comment_has_retriage_command,
    create_issue_comment,
    extract_attachment_urls,
    get_issue_comments,
    get_issue,
    is_allowed_attachment_url,
    is_bot_comment,
    is_maintainer_comment,
    load_event_payload,
    sanitize_url,
    update_issue_comment,
)


PERF_TELEMETRY_FORM_MARKER = "<!-- performance-telemetry-report -->"
PERF_TELEMETRY_TITLE_PREFIX = "[Performance Telemetry]"
PERF_TELEMETRY_MANAGED_COMMENT_MARKER = "<!-- perf-telemetry-triage:managed-comment -->"
PERF_TELEMETRY_PAYLOAD_START_MARKER = "<!-- perf-telemetry-triage:machine-payload:start -->"
PERF_TELEMETRY_PAYLOAD_END_MARKER = "<!-- perf-telemetry-triage:machine-payload:end -->"
AUTOMATION_PARSER_VERSION = "2026-03-27-perf-telemetry-status-contract"
USER_AGENT = "NoOfficeDemandFixPerfTelemetryAutomation/1.0"
SUMMARY_FILE_KIND = "summary"
STALLS_FILE_KIND = "stalls"
UNKNOWN_SCENARIO_ID = "unknown"
UNSAVED_NAME = "unsaved"
COMPARISON_LOAD_ERROR_LIMIT = 500
QUEUE_SAMPLING_STATE_OK = "ok"
QUEUE_SAMPLING_STATE_PARTIAL = "partial"
QUEUE_SAMPLING_STATE_FAILED = "failed"
QUEUE_SAMPLING_STATE_UNKNOWN = "unknown"
QUEUE_SAMPLING_REASON_NONE = "none"
QUEUE_SAMPLING_REASON_UNSUPPORTED_FIELDS = "unsupported_fields"
QUEUE_SAMPLING_REASON_BIND_FAILED = "bind_failed"
QUEUE_SAMPLING_REASON_RUNTIME_ERROR = "runtime_error"
QUEUE_SAMPLING_REASON_UNKNOWN = "unknown"
COMPARISON_BUNDLE_STATUS_OMITTED = "omitted"
COMPARISON_BUNDLE_STATUS_LOADED = "loaded"
COMPARISON_BUNDLE_STATUS_IGNORED = "ignored"
COMPARISON_STATUS_BASELINE_ONLY = "baseline_only"
COMPARISON_STATUS_COMPARISON_IGNORED = "comparison_ignored"
COMPARISON_STATUS_NOT_DIRECTLY_COMPARABLE = "not_directly_comparable"
COMPARISON_STATUS_COMPARABLE = "comparable"
COMPARISON_STATUS_COMPARABLE_SINGLE_FIX_TOGGLE = "comparable_single_fix_toggle"
FIX_TOGGLE_FIELDS = (
    ("enable_phantom_vacancy_fix", "EnablePhantomVacancyFix"),
    ("enable_outside_connection_virtual_seller_fix", "EnableOutsideConnectionVirtualSellerFix"),
    ("enable_virtual_office_resource_buyer_fix", "EnableVirtualOfficeResourceBuyerFix"),
    ("enable_office_demand_direct_patch", "EnableOfficeDemandDirectPatch"),
)

PERF_TELEMETRY_FORM_LABELS = {
    "game version": "game_version",
    "mod version": "mod_version",
    "save or city label": "save_or_city_label",
    "what changed": "what_changed",
    "platform notes": "platform_notes",
    "other mods": "other_mods",
    "baseline telemetry bundle": "baseline_bundle",
    "comparison telemetry bundle": "comparison_bundle",
}

PERF_TELEMETRY_REQUIRED_FIELDS = (
    "game_version",
    "mod_version",
    "save_or_city_label",
    "what_changed",
    "baseline_bundle",
)

SUMMARY_FIELDNAMES_V1 = [
    "run_id",
    "elapsed_sec",
    "simulation_tick",
    "fps_mean",
    "render_latency_mean_ms",
    "render_latency_p95_ms",
    "simulation_step_mean_ms",
    "pathfind_update_mean_ms",
    "mod_update_mean_ms",
    "mod_entities_inspected_count",
    "mod_repath_requested_count",
    "path_requests_pending_count",
    "path_queue_len_max",
    "is_stall_window",
]

SUMMARY_FIELDNAMES_V2 = [
    "run_id",
    "elapsed_sec",
    "simulation_tick",
    "fps_mean",
    "render_latency_mean_ms",
    "render_latency_p95_ms",
    "simulation_update_rate_mean",
    "simulation_update_interval_mean_ms",
    "simulation_update_interval_p95_ms",
    "simulation_step_mean_ms",
    "pathfind_update_mean_ms",
    "mod_update_mean_ms",
    "mod_entities_inspected_count",
    "mod_repath_requested_count",
    "path_requests_pending_count",
    "path_queue_len_max",
    "is_stall_window",
]

SUMMARY_FIELDNAMES = SUMMARY_FIELDNAMES_V2

STALL_FIELDNAMES = [
    "run_id",
    "stall_id",
    "stall_start_sec",
    "stall_end_sec",
    "stall_duration_sec",
    "stall_peak_render_latency_ms",
    "stall_p95_render_latency_ms",
    "stall_peak_path_queue_len",
    "stall_mod_repath_requested_count",
    "stall_mod_entities_inspected_count",
]

SUMMARY_HEADER_V1 = ",".join(SUMMARY_FIELDNAMES_V1)
SUMMARY_HEADER_V2 = ",".join(SUMMARY_FIELDNAMES_V2)


@dataclass
class TelemetryRunMetadata:
    telemetry_schema_version: str = ""
    telemetry_file_kind: str = ""
    run_id: str = ""
    run_start_utc: str = ""
    game_build_version: str = ""
    mod_version: str = ""
    save_name: str = ""
    scenario_id: str = ""
    sampling_interval_sec: float | None = None
    stall_threshold_ms: int | None = None
    enable_phantom_vacancy_fix: bool | None = None
    enable_outside_connection_virtual_seller_fix: bool | None = None
    enable_virtual_office_resource_buyer_fix: bool | None = None
    enable_office_demand_direct_patch: bool | None = None
    path_queue_sampling_state: str = QUEUE_SAMPLING_STATE_UNKNOWN
    path_queue_sampling_reason: str = QUEUE_SAMPLING_REASON_UNKNOWN

    def enabled_fix_state(self) -> tuple[bool | None, bool | None, bool | None, bool | None]:
        return (
            self.enable_phantom_vacancy_fix,
            self.enable_outside_connection_virtual_seller_fix,
            self.enable_virtual_office_resource_buyer_fix,
            self.enable_office_demand_direct_patch,
        )


@dataclass
class SummaryRow:
    run_id: str
    elapsed_sec: float
    simulation_tick: int
    fps_mean: float
    render_latency_mean_ms: float
    render_latency_p95_ms: float
    simulation_update_rate_mean: float
    simulation_update_interval_mean_ms: float
    simulation_update_interval_p95_ms: float
    simulation_step_mean_ms: float
    pathfind_update_mean_ms: float
    mod_update_mean_ms: float
    mod_entities_inspected_count: int
    mod_repath_requested_count: int
    path_requests_pending_count: int
    path_queue_len_max: int
    is_stall_window: bool
    window_duration_sec: float = 0.0


@dataclass
class StallRow:
    run_id: str
    stall_id: int
    stall_start_sec: float
    stall_end_sec: float
    stall_duration_sec: float
    stall_peak_render_latency_ms: float
    stall_p95_render_latency_ms: float
    stall_peak_path_queue_len: int
    stall_mod_repath_requested_count: int
    stall_mod_entities_inspected_count: int


@dataclass
class SteadyStateRollup:
    window_count: int
    duration_sec: float
    fps_mean: float
    render_latency_mean_ms: float
    render_latency_p95_ms_mean: float
    simulation_step_mean_ms: float
    pathfind_update_mean_ms: float
    mod_update_mean_ms: float
    mod_entities_inspected_per_sec: float
    mod_repath_requested_per_sec: float
    path_requests_pending_mean: float
    path_requests_pending_p95: float
    path_requests_pending_max: int
    path_queue_len_mean: float
    path_queue_len_p95: float
    path_queue_len_max: int


@dataclass
class StallRollup:
    file_available: bool
    count: int
    total_duration_sec: float
    mean_duration_sec: float
    p95_duration_sec: float
    max_duration_sec: float
    peak_render_latency_ms: float
    peak_path_queue_len: int
    total_mod_repath_requested: int
    total_mod_entities_inspected: int


@dataclass
class RunAnalysis:
    label: str
    source_mode: str
    source_description: str
    metadata: TelemetryRunMetadata
    summary_row_count: int
    stall_row_count: int
    warnings: list[str] = field(default_factory=list)
    steady_state: SteadyStateRollup | None = None
    stalls: StallRollup | None = None


@dataclass
class ComparisonAnalysis:
    directly_comparable: bool
    status: str = COMPARISON_STATUS_NOT_DIRECTLY_COMPARABLE
    comparability_basis: str = ""
    fix_toggle_differences: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    steady_state_fps_delta: float | None = None
    steady_state_render_latency_delta_ms: float | None = None
    steady_state_pathfind_update_delta_ms: float | None = None
    steady_state_mod_update_delta_ms: float | None = None
    steady_state_path_queue_p95_delta: float | None = None
    stall_count_delta: int | None = None
    total_stall_duration_sec_delta: float | None = None
    max_stall_duration_sec_delta: float | None = None
    stall_peak_path_queue_delta: int | None = None


@dataclass
class TriageAnalysis:
    issue_number: int
    issue_fields: dict[str, str]
    baseline: RunAnalysis
    comparison: RunAnalysis | None
    comparison_analysis: ComparisonAnalysis | None
    comparison_bundle_status: str
    comparison_bundle_provided: bool
    comparison_load_error: str | None
    warnings: list[str]
    anomaly_flags: list[str]
    follow_up_suggestions: list[str]


def get_parser_version() -> str:
    return AUTOMATION_PARSER_VERSION


def clean_perf_telemetry_issue_form_value(text: str) -> str:
    cleaned = text.replace("\r\n", "\n").strip()
    if cleaned == "_No response_":
        return ""
    return cleaned


def parse_issue_form_sections(body: str) -> dict[str, str]:
    sections: dict[str, list[str]] = {}
    current_header: str | None = None

    for line in body.replace("\r\n", "\n").split("\n"):
        stripped = line.lstrip()
        if stripped.startswith("### "):
            current_header = stripped[4:].strip().lower()
            sections[current_header] = []
            continue

        if current_header is not None:
            sections[current_header].append(line.rstrip())

    parsed: dict[str, str] = {}
    for header, field_name in PERF_TELEMETRY_FORM_LABELS.items():
        parsed[field_name] = clean_perf_telemetry_issue_form_value("\n".join(sections.get(header, [])))

    return parsed


def is_performance_telemetry_issue(issue_body: str, issue_title: str = "") -> bool:
    if PERF_TELEMETRY_FORM_MARKER in issue_body:
        return True

    if not issue_title.strip().startswith(PERF_TELEMETRY_TITLE_PREFIX):
        return False

    parsed_sections = parse_issue_form_sections(issue_body)
    return all(parsed_sections.get(field_name, "").strip() for field_name in PERF_TELEMETRY_REQUIRED_FIELDS)


def build_attachment_failure_comment(error_message: str) -> str:
    return (
        "Performance telemetry triage could not read the attached telemetry bundle. "
        f"Reason: `{truncate_text(error_message, 500)}`. "
        "Attach a `.zip` containing `perf_summary.csv` and `perf_stalls.csv`, "
        "attach the two CSV files directly, or paste the CSV text inline and edit the issue."
    )


def build_perf_telemetry_triage_failure_comment(error_message: str) -> str:
    return (
        "Performance telemetry triage hit an automation failure before it could update the managed comment. "
        f"Reason: `{truncate_text(error_message, 500)}`. "
        "Retry the issue edit or rerun the workflow after the GitHub or network problem is resolved."
    )


def build_triage_analysis(issue_number: int, issue_fields: dict[str, str]) -> TriageAnalysis:
    baseline, baseline_warnings = load_bundle_analysis(
        issue_fields.get("baseline_bundle", ""),
        label="Baseline",
        field_label="Baseline telemetry bundle",
        required=True,
    )
    if baseline is None:
        raise AssertionError("Required baseline telemetry bundle unexpectedly returned no analysis.")
    comparison_bundle_raw = issue_fields.get("comparison_bundle", "")
    comparison_bundle_provided = bool(comparison_bundle_raw.strip())
    comparison_load_error: str | None = None
    # The comparison bundle is optional, so a malformed or unreadable
    # comparison input should not discard a valid baseline report.
    # Downstream analysis will treat this as baseline-only triage and skip
    # direct comparison deltas because `comparison` stays `None`, but we
    # still preserve whether a comparison bundle was supplied and why it
    # was ignored so maintainers do not confuse an invalid attachment
    # with an omitted comparison.
    try:
        comparison, comparison_warnings = load_bundle_analysis(
            comparison_bundle_raw,
            label="Comparison",
            field_label="Comparison telemetry bundle",
            required=False,
        )
    except AutomationError as error:
        comparison = None
        comparison_load_error = truncate_text(str(error), COMPARISON_LOAD_ERROR_LIMIT)
        comparison_warnings = [f"Comparison telemetry bundle was ignored: {comparison_load_error}"]

    warnings = baseline_warnings + comparison_warnings
    comparison_bundle_status = (
        COMPARISON_BUNDLE_STATUS_IGNORED if comparison_bundle_provided else COMPARISON_BUNDLE_STATUS_OMITTED
    )
    comparison_analysis: ComparisonAnalysis | None
    if comparison is not None:
        comparison_bundle_status = COMPARISON_BUNDLE_STATUS_LOADED
        comparison_analysis = compare_runs(baseline, comparison)
        warnings.extend(comparison_analysis.warnings)
    elif comparison_bundle_provided:
        comparison_analysis = ComparisonAnalysis(
            directly_comparable=False,
            status=COMPARISON_STATUS_COMPARISON_IGNORED,
        )
    else:
        comparison_analysis = ComparisonAnalysis(
            directly_comparable=False,
            status=COMPARISON_STATUS_BASELINE_ONLY,
        )

    anomaly_flags = detect_anomaly_flags(baseline, comparison, comparison_analysis)
    follow_up_suggestions = build_follow_up_suggestions(baseline, comparison, comparison_analysis, anomaly_flags)

    return TriageAnalysis(
        issue_number=issue_number,
        issue_fields=issue_fields,
        baseline=baseline,
        comparison=comparison,
        comparison_analysis=comparison_analysis,
        comparison_bundle_status=comparison_bundle_status,
        comparison_bundle_provided=comparison_bundle_provided,
        comparison_load_error=comparison_load_error,
        warnings=dedupe_preserve_order(warnings),
        anomaly_flags=anomaly_flags,
        follow_up_suggestions=follow_up_suggestions,
    )


def render_managed_comment(triage: TriageAnalysis) -> str:
    lines = [
        PERF_TELEMETRY_MANAGED_COMMENT_MARKER,
        (
            f"Performance telemetry triage for #{triage.issue_number}. "
            "Do not edit this bot comment. Edit the issue or add `/retriage` in a new maintainer comment "
            "to rerun it with the latest parser."
        ),
        "",
        f"Parser version: `{get_parser_version()}`",
    ]

    if triage.warnings:
        lines.extend(
            [
                "",
                "Warnings:",
                *[f"- {warning}" for warning in triage.warnings],
            ]
        )

    lines.extend(
        [
            "",
            "## Run summary",
            f"- Reported change: {fallback_text(triage.issue_fields.get('what_changed', ''), 'none provided')}",
            *render_run_summary_block(triage.baseline),
        ]
    )

    if triage.comparison is not None:
        lines.extend(render_run_summary_block(triage.comparison))

    comparison_analysis = triage.comparison_analysis
    comparison_status = (
        comparison_analysis.status if comparison_analysis is not None else COMPARISON_STATUS_BASELINE_ONLY
    )
    comparison_lines = [
        "",
        "## Comparison status",
        f"- Status: `{comparison_status}`",
    ]
    if comparison_status == COMPARISON_STATUS_BASELINE_ONLY:
        comparison_lines.extend(
            [
                "- No comparison telemetry bundle was provided.",
                "- Direct before/after deltas were not computed.",
            ]
        )
    elif comparison_status == COMPARISON_STATUS_COMPARISON_IGNORED:
        comparison_lines.extend(
            [
                "- A comparison telemetry bundle was provided but could not be used.",
                "- Direct before/after deltas were not computed.",
            ]
        )
        if triage.comparison_load_error:
            comparison_lines.append("- See the warnings above for the comparison bundle failure reason.")
    elif comparison_status == COMPARISON_STATUS_NOT_DIRECTLY_COMPARABLE:
        comparison_lines.append("- Direct before/after deltas were not computed.")
        if comparison_analysis is not None:
            comparison_lines.extend(f"- {warning}" for warning in comparison_analysis.warnings)
    elif comparison_status == COMPARISON_STATUS_COMPARABLE_SINGLE_FIX_TOGGLE:
        comparison_lines.extend(
            [
                "- Direct before/after deltas are available for a single fix-toggle delta.",
                "- Fix-toggle delta: "
                + ", ".join(f"`{name}`" for name in comparison_analysis.fix_toggle_differences),
                "- Direct deltas reflect the full effect of the toggle under test, not like-for-like same-fix-set overhead.",
            ]
        )
    else:
        comparison_lines.append("- Direct before/after deltas are available.")
    lines.extend(comparison_lines)

    if comparison_analysis is not None and comparison_analysis.directly_comparable:
        lines.extend(
            [
                "",
                "## Direct comparison metrics",
                *render_comparison_metrics(comparison_analysis),
            ]
        )

    lines.extend(
        [
            "",
            "## Anomaly flags / follow-up suggestions",
            (
                "- Anomaly flags: "
                + ", ".join(f"`{flag}`" for flag in triage.anomaly_flags)
                if triage.anomaly_flags
                else "- Anomaly flags: none"
            ),
            (
                "- Follow-up suggestions: "
                + ", ".join(f"`{item}`" for item in triage.follow_up_suggestions)
                if triage.follow_up_suggestions
                else "- Follow-up suggestions: none"
            ),
            "",
            PERF_TELEMETRY_PAYLOAD_START_MARKER,
            "```json",
            json.dumps(build_machine_payload(triage), indent=2, sort_keys=True),
            "```",
            PERF_TELEMETRY_PAYLOAD_END_MARKER,
        ]
    )

    return "\n".join(lines).strip() + "\n"


def is_perf_telemetry_managed_comment(
    comment: dict[str, Any],
    managed_author_login: str | None = None,
) -> bool:
    if PERF_TELEMETRY_MANAGED_COMMENT_MARKER not in comment.get("body", ""):
        return False
    if is_bot_comment(comment):
        return True

    user_login = str(comment.get("user", {}).get("login", "")).strip()
    return bool(managed_author_login and user_login == managed_author_login.strip())


def find_perf_telemetry_managed_comment(
    comments: list[dict[str, Any]],
    managed_author_login: str | None = None,
) -> dict[str, Any] | None:
    managed_comments = [
        comment
        for comment in comments
        if is_perf_telemetry_managed_comment(comment, managed_author_login=managed_author_login)
    ]
    if not managed_comments:
        return None
    return sorted(managed_comments, key=lambda item: item.get("updated_at", item.get("created_at", "")))[-1]


def upsert_perf_telemetry_managed_comment(
    repo: str,
    issue_number: int,
    body: str,
    token: str,
    managed_author_login: str | None = None,
) -> dict[str, Any]:
    comments = get_issue_comments(repo, issue_number, token)
    managed_comment = find_perf_telemetry_managed_comment(comments, managed_author_login=managed_author_login)
    if managed_comment:
        return update_issue_comment(repo, int(managed_comment["id"]), body, token)
    return create_issue_comment(repo, issue_number, body, token)


def build_machine_payload(triage: TriageAnalysis) -> dict[str, Any]:
    return {
        "parser_version": get_parser_version(),
        "issue_number": triage.issue_number,
        "issue_context": {
            "game_version": triage.issue_fields.get("game_version", ""),
            "mod_version": triage.issue_fields.get("mod_version", ""),
            "save_or_city_label": triage.issue_fields.get("save_or_city_label", ""),
            "what_changed": triage.issue_fields.get("what_changed", ""),
            "platform_notes": triage.issue_fields.get("platform_notes", ""),
            "other_mods": triage.issue_fields.get("other_mods", ""),
        },
        "baseline": asdict(triage.baseline),
        "comparison_bundle": {
            "status": triage.comparison_bundle_status,
            "provided": triage.comparison_bundle_provided,
            "loaded": triage.comparison is not None,
            "load_error": triage.comparison_load_error,
        },
        "comparison": asdict(triage.comparison) if triage.comparison is not None else None,
        "comparison_analysis": asdict(triage.comparison_analysis) if triage.comparison_analysis is not None else None,
        "warnings": triage.warnings,
        "anomaly_flags": triage.anomaly_flags,
        "follow_up_suggestions": triage.follow_up_suggestions,
    }


def render_run_summary_block(run_analysis: RunAnalysis) -> list[str]:
    lines = [
        f"- {run_analysis.label} source: {run_analysis.source_description}",
        (
            f"- {run_analysis.label} run: "
            f"`{fallback_text(run_analysis.metadata.run_id, 'unknown')}`; "
            f"save `{fallback_text(run_analysis.metadata.save_name, UNSAVED_NAME)}`; "
            f"scenario `{fallback_text(run_analysis.metadata.scenario_id, UNKNOWN_SCENARIO_ID)}`"
        ),
        format_queue_sampling_summary_line(run_analysis),
    ]

    if run_analysis.steady_state is None:
        lines.append(f"- {run_analysis.label} steady state: unavailable")
    else:
        steady_state = run_analysis.steady_state
        lines.append(
            f"- {run_analysis.label} steady state: "
            f"{steady_state.window_count} non-stall windows over {format_float(steady_state.duration_sec)} s; "
            f"fps {format_float(steady_state.fps_mean)}; "
            f"render {format_float(steady_state.render_latency_mean_ms)} ms mean / "
            f"{format_float(steady_state.render_latency_p95_ms_mean)} ms p95-window mean; "
            f"simulation {format_float(steady_state.simulation_step_mean_ms)} ms; "
            f"pathfind {format_float(steady_state.pathfind_update_mean_ms)} ms; "
            f"mod {format_float(steady_state.mod_update_mean_ms)} ms"
        )
        lines.append(
            f"- {run_analysis.label} steady-state activity: "
            f"inspect {format_float(steady_state.mod_entities_inspected_per_sec)}/s; "
            f"repath {format_float(steady_state.mod_repath_requested_per_sec)}/s; "
            f"pending {format_float(steady_state.path_requests_pending_mean)} mean / "
            f"{format_float(steady_state.path_requests_pending_p95)} p95 / "
            f"{steady_state.path_requests_pending_max} max; "
            f"queue {format_float(steady_state.path_queue_len_mean)} mean / "
            f"{format_float(steady_state.path_queue_len_p95)} p95 / "
            f"{steady_state.path_queue_len_max} max"
        )

    if run_analysis.stalls is None:
        lines.append(f"- {run_analysis.label} stalls: `perf_stalls.csv` unavailable")
    else:
        stalls = run_analysis.stalls
        lines.append(
            f"- {run_analysis.label} stalls: "
            f"{stalls.count} events; total {format_float(stalls.total_duration_sec)} s; "
            f"mean {format_float(stalls.mean_duration_sec)} s / "
            f"p95 {format_float(stalls.p95_duration_sec)} s / "
            f"max {format_float(stalls.max_duration_sec)} s; "
            f"peak render {format_float(stalls.peak_render_latency_ms)} ms; "
            f"peak queue {stalls.peak_path_queue_len}; "
            f"stall repath {stalls.total_mod_repath_requested}; "
            f"stall inspect {stalls.total_mod_entities_inspected}"
        )

    for warning in run_analysis.warnings:
        lines.append(f"- {run_analysis.label} warning: {warning}")

    return lines


def render_comparison_metrics(comparison: ComparisonAnalysis) -> list[str]:
    lines: list[str] = []
    if comparison.steady_state_mod_update_delta_ms is not None:
        lines.append(
            f"- Steady-state delta: fps {format_signed_float(comparison.steady_state_fps_delta)}; "
            f"render {format_signed_float(comparison.steady_state_render_latency_delta_ms)} ms; "
            f"pathfind {format_signed_float(comparison.steady_state_pathfind_update_delta_ms)} ms; "
            f"mod {format_signed_float(comparison.steady_state_mod_update_delta_ms)} ms; "
            f"queue p95 {format_signed_float(comparison.steady_state_path_queue_p95_delta)}"
        )
    else:
        lines.append("- Steady-state delta: unavailable")

    if comparison.stall_count_delta is not None:
        lines.append(
            f"- Stall delta: count {format_signed_int(comparison.stall_count_delta)}; "
            f"total {format_signed_float(comparison.total_stall_duration_sec_delta)} s; "
            f"max {format_signed_float(comparison.max_stall_duration_sec_delta)} s; "
            f"peak queue {format_signed_int(comparison.stall_peak_path_queue_delta)}"
        )
    else:
        lines.append("- Stall delta: unavailable")

    return lines


def load_bundle_analysis(
    raw_field_text: str,
    *,
    label: str,
    field_label: str,
    required: bool,
) -> tuple[RunAnalysis | None, list[str]]:
    warnings: list[str] = []
    if not raw_field_text.strip():
        if required:
            raise AutomationError(f"{field_label} is required.")
        return None, warnings

    source_mode, source_description, documents, source_warnings = load_bundle_documents(raw_field_text, field_label)
    warnings.extend(source_warnings)

    summary_text = documents.get(SUMMARY_FILE_KIND, "")
    if not summary_text:
        if required:
            raise AutomationError(f"{field_label} is missing `perf_summary.csv`.")
        warnings.append(f"{field_label} is missing `perf_summary.csv`; comparison bundle was ignored.")
        return None, warnings

    summary_metadata, summary_rows, summary_warnings = parse_summary_document(summary_text, field_label)
    warnings.extend(summary_warnings)
    summary_rows, summary_artifact_warnings = drop_terminal_summary_artifact_rows(summary_rows, field_label)
    warnings.extend(summary_artifact_warnings)

    stall_rows: list[StallRow] | None = None
    if STALLS_FILE_KIND in documents:
        try:
            stall_metadata, parsed_stall_rows, stall_parse_warnings = parse_stall_document(
                documents[STALLS_FILE_KIND], field_label
            )
            warnings.extend(stall_parse_warnings)
            metadata_warnings = compare_metadata(summary_metadata, stall_metadata, field_label)
            warnings.extend(metadata_warnings)
            if metadata_warnings:
                warnings.append(
                    f"{field_label} `perf_stalls.csv` was ignored because its telemetry metadata does not match "
                    "`perf_summary.csv`."
                )
            else:
                stall_rows = parsed_stall_rows
        except AutomationError as error:
            warnings.append(f"{field_label} contains an unreadable `perf_stalls.csv`: {error}")
    else:
        warnings.append(f"{field_label} is missing `perf_stalls.csv`; stall metrics are unavailable.")

    run_analysis = RunAnalysis(
        label=label,
        source_mode=source_mode,
        source_description=source_description,
        metadata=summary_metadata,
        summary_row_count=len(summary_rows),
        stall_row_count=0 if stall_rows is None else len(stall_rows),
        warnings=dedupe_preserve_order(warnings),
        steady_state=rollup_steady_state(summary_rows, summary_metadata),
        stalls=None if stall_rows is None else rollup_stalls(stall_rows),
    )
    run_analysis.warnings = dedupe_preserve_order(run_analysis.warnings + detect_queue_metric_sampling_warnings(run_analysis))

    return run_analysis, run_analysis.warnings


def load_bundle_documents(raw_field_text: str, field_label: str) -> tuple[str, str, dict[str, str], list[str]]:
    attachment_urls = extract_attachment_urls(raw_field_text)
    if attachment_urls:
        return load_bundle_documents_from_attachments(attachment_urls, field_label)
    return load_bundle_documents_from_inline_text(raw_field_text, field_label)


def load_bundle_documents_from_inline_text(raw_field_text: str, field_label: str) -> tuple[str, str, dict[str, str], list[str]]:
    documents, warnings = extract_inline_documents(raw_field_text)
    if not documents:
        raise AutomationError(f"{field_label} does not contain recognizable telemetry CSV content.")
    return "inline", "inline CSV text", documents, warnings


def load_bundle_documents_from_attachments(
    attachment_urls: list[str],
    field_label: str,
) -> tuple[str, str, dict[str, str], list[str]]:
    warnings: list[str] = []
    documents: dict[str, str] = {}
    sanitized_urls = [sanitize_url(url) for url in attachment_urls]
    selected_source_description: str | None = None
    telemetry_attachment_count = 0

    for attachment_url in attachment_urls:
        attachment_name = sanitized_url_basename(attachment_url)
        content = download_attachment_bytes(attachment_url)
        is_zip_attachment = attachment_name.lower().endswith(".zip") or looks_like_zip_bytes(content)
        if is_zip_attachment:
            zip_documents, zip_warnings = extract_documents_from_zip(content, sanitize_url(attachment_url))
            warnings.extend(zip_warnings)
            if not zip_documents:
                warnings.append(
                    f"{field_label} skipped zip attachment `{sanitize_url(attachment_url)}` because it does not "
                    "contain recognizable telemetry CSV files."
                )
                continue
            telemetry_attachment_count += 1
            if selected_source_description is None:
                selected_source_description = f"zip attachment `{sanitize_url(attachment_url)}`"
            merge_documents(documents, zip_documents, field_label, warnings, source_hint=attachment_name)
            continue

        decoded_text = content.decode("utf-8", errors="replace")
        inline_documents, inline_warnings = extract_inline_documents(decoded_text)
        if not inline_documents:
            warnings.append(
                f"{field_label} skipped non-telemetry attachment `{sanitize_url(attachment_url)}`."
            )
            continue
        telemetry_attachment_count += 1
        if selected_source_description is None:
            selected_source_description = f"attachment `{sanitize_url(attachment_url)}`"
        warnings.extend(inline_warnings)
        merge_documents(documents, inline_documents, field_label, warnings, source_hint=attachment_name)

    if not documents:
        raise AutomationError(f"{field_label} attachments did not contain recognizable telemetry CSV files.")

    description = (
        selected_source_description
        if telemetry_attachment_count == 1 and selected_source_description is not None
        else f"attachment `{sanitized_urls[0]}`" if len(attachment_urls) == 1 else "attachment CSV pair"
    )
    return "attachment", description, documents, warnings


def extract_documents_from_zip(zip_bytes: bytes, source_name: str) -> tuple[dict[str, str], list[str]]:
    warnings: list[str] = []
    documents: dict[str, str] = {}

    try:
        archive = zipfile.ZipFile(io.BytesIO(zip_bytes))
    except zipfile.BadZipFile as error:
        raise AutomationError(f"Unreadable telemetry zip attachment `{source_name}`: {error}") from error

    with archive:
        candidates = [item for item in archive.infolist() if not item.is_dir()]
        for item in candidates:
            filename = PurePosixPath(item.filename).name
            decoded_text = archive.read(item).decode("utf-8", errors="replace")
            inline_documents, inline_warnings = extract_inline_documents(decoded_text)
            if not inline_documents:
                continue
            warnings.extend(inline_warnings)
            merge_documents(documents, inline_documents, source_name, warnings, source_hint=filename)

    return documents, warnings


def extract_inline_documents(raw_text: str) -> tuple[dict[str, str], list[str]]:
    normalized = raw_text.replace("\r\n", "\n").strip()
    if not normalized:
        return {}, []

    blocks = extract_fenced_blocks(normalized)
    if not blocks:
        blocks = [normalized]

    documents: dict[str, str] = {}
    warnings: list[str] = []
    for block in blocks:
        for fragment in split_csv_fragments(block):
            kind = classify_csv_fragment(fragment)
            if kind is None:
                continue
            if kind in documents:
                warnings.append(f"Found multiple `{kind}` CSV documents in the same bundle; using the first one.")
                continue
            documents[kind] = fragment.strip()

    return documents, warnings


def split_csv_fragments(block: str) -> list[str]:
    lines = block.strip().splitlines()
    header_positions: list[int] = []
    for index, line in enumerate(lines):
        if classify_header(line.strip()) is not None:
            header_positions.append(index)

    if not header_positions:
        return [block.strip()]

    fragments: list[str] = []
    starts: list[int] = []
    for header_index in header_positions:
        start = header_index
        while start > 0 and lines[start - 1].startswith("# "):
            start -= 1
        starts.append(start)

    for index, start in enumerate(starts):
        end = starts[index + 1] if index + 1 < len(starts) else len(lines)
        fragment = "\n".join(lines[start:end]).strip()
        if fragment:
            fragments.append(fragment)

    return fragments or [block.strip()]


def classify_header(line: str) -> str | None:
    header_tokens = normalize_csv_header_tokens(line)
    if header_tokens in (SUMMARY_FIELDNAMES_V1, SUMMARY_FIELDNAMES_V2):
        return SUMMARY_FILE_KIND
    if header_tokens == STALL_FIELDNAMES:
        return STALLS_FILE_KIND
    return None


def classify_csv_fragment(fragment: str) -> str | None:
    for line in fragment.replace("\r\n", "\n").splitlines():
        kind = classify_header(line.strip())
        if kind is not None:
            return kind

    metadata_values, _ = split_metadata_and_csv_metadata_only(fragment)
    file_kind = metadata_values.get("telemetry_file_kind", "").strip().lower()
    if file_kind == SUMMARY_FILE_KIND:
        return SUMMARY_FILE_KIND
    if file_kind == STALLS_FILE_KIND:
        return STALLS_FILE_KIND
    return None


def merge_documents(
    destination: dict[str, str],
    new_documents: dict[str, str],
    field_label: str,
    warnings: list[str],
    *,
    source_hint: str = "",
) -> None:
    for kind, content in new_documents.items():
        if kind in destination:
            suffix = f" from `{source_hint}`" if source_hint else ""
            warnings.append(f"{field_label} includes multiple `{kind}` CSV sources{suffix}; using the first one.")
            continue
        destination[kind] = content


def parse_summary_document(document_text: str, field_label: str) -> tuple[TelemetryRunMetadata, list[SummaryRow], list[str]]:
    metadata_values, csv_text = split_metadata_and_csv(document_text)
    metadata = build_metadata(metadata_values)
    summary_fieldnames, detected_schema_version = select_summary_schema(csv_text)
    warnings = validate_metadata_contract(
        metadata,
        expected_file_kind=SUMMARY_FILE_KIND,
        field_label=field_label,
        detected_schema_version=detected_schema_version,
    )
    metadata_schema_version = metadata.telemetry_schema_version.strip()
    if metadata_schema_version and metadata_schema_version != detected_schema_version:
        warnings.append(
            f"{field_label} metadata says `telemetry_schema_version={metadata_schema_version}` "
            f"but the summary header matches schema v{detected_schema_version}; using the header-derived schema."
        )
    metadata.telemetry_schema_version = detected_schema_version
    rows = parse_csv_rows(csv_text, summary_fieldnames, parse_summary_row)

    if metadata.run_id and any(row.run_id and row.run_id != metadata.run_id for row in rows):
        warnings.append(f"{field_label} summary rows include a run_id that does not match metadata.")

    return metadata, rows, warnings


def parse_stall_document(document_text: str, field_label: str) -> tuple[TelemetryRunMetadata, list[StallRow], list[str]]:
    metadata_values, csv_text = split_metadata_and_csv(document_text)
    metadata = build_metadata(metadata_values)
    warnings = validate_metadata_contract(metadata, expected_file_kind=STALLS_FILE_KIND, field_label=field_label)
    rows = parse_csv_rows(csv_text, STALL_FIELDNAMES, parse_stall_row)

    if metadata.run_id and any(row.run_id and row.run_id != metadata.run_id for row in rows):
        warnings.append(f"{field_label} stall rows include a run_id that does not match metadata.")

    return metadata, rows, warnings


def split_metadata_and_csv(document_text: str) -> tuple[dict[str, str], str]:
    lines = document_text.replace("\r\n", "\n").split("\n")
    metadata: dict[str, str] = {}
    csv_start_index: int | None = None

    for index, raw_line in enumerate(lines):
        line = raw_line.strip()
        if not line:
            continue
        if line.startswith("# "):
            key, value = parse_metadata_line(line)
            if key:
                metadata[key] = value
            continue
        csv_start_index = index
        break

    if csv_start_index is None:
        raise AutomationError("Telemetry CSV document does not contain a header row.")

    csv_text = "\n".join(line.lstrip() for line in lines[csv_start_index:]).strip()
    if not csv_text:
        raise AutomationError("Telemetry CSV document is empty after the metadata block.")
    return metadata, csv_text


def split_metadata_and_csv_metadata_only(document_text: str) -> tuple[dict[str, str], int | None]:
    lines = document_text.replace("\r\n", "\n").split("\n")
    metadata: dict[str, str] = {}
    csv_start_index: int | None = None

    for index, raw_line in enumerate(lines):
        line = raw_line.strip()
        if not line:
            continue
        if line.startswith("# "):
            key, value = parse_metadata_line(line)
            if key:
                metadata[key] = value
            continue
        csv_start_index = index
        break

    return metadata, csv_start_index


def parse_metadata_line(line: str) -> tuple[str, str]:
    key_value = line[2:]
    if "=" not in key_value:
        return "", ""
    key, value = key_value.split("=", 1)
    return key.strip(), value.strip()


def build_metadata(values: dict[str, str]) -> TelemetryRunMetadata:
    queue_sampling_state = normalize_queue_sampling_state(values.get("path_queue_sampling_state"))
    return TelemetryRunMetadata(
        telemetry_schema_version=values.get("telemetry_schema_version", ""),
        telemetry_file_kind=values.get("telemetry_file_kind", ""),
        run_id=values.get("run_id", ""),
        run_start_utc=values.get("run_start_utc", ""),
        game_build_version=values.get("game_build_version", ""),
        mod_version=values.get("mod_version", ""),
        save_name=values.get("save_name", ""),
        scenario_id=values.get("scenario_id", ""),
        sampling_interval_sec=parse_optional_float(values.get("sampling_interval_sec", "")),
        stall_threshold_ms=parse_optional_int(values.get("stall_threshold_ms", "")),
        enable_phantom_vacancy_fix=parse_optional_bool(values.get("enable_phantom_vacancy_fix", "")),
        enable_outside_connection_virtual_seller_fix=parse_optional_bool(
            values.get("enable_outside_connection_virtual_seller_fix", "")
        ),
        enable_virtual_office_resource_buyer_fix=parse_optional_bool(
            values.get("enable_virtual_office_resource_buyer_fix", "")
        ),
        enable_office_demand_direct_patch=parse_optional_bool(values.get("enable_office_demand_direct_patch", "")),
        path_queue_sampling_state=queue_sampling_state,
        path_queue_sampling_reason=normalize_queue_sampling_reason(
            values.get("path_queue_sampling_reason"),
            queue_sampling_state,
        ),
    )


def validate_metadata_contract(
    metadata: TelemetryRunMetadata,
    *,
    expected_file_kind: str,
    field_label: str,
    detected_schema_version: str | None = None,
) -> list[str]:
    warnings: list[str] = []
    schema_version = metadata.telemetry_schema_version.strip()
    if not schema_version:
        if detected_schema_version:
            warnings.append(
                f"{field_label} is missing `telemetry_schema_version`; inferred schema v{detected_schema_version} "
                "from the CSV header."
            )
        else:
            warnings.append(f"{field_label} is missing `telemetry_schema_version`; parsed with supported compatibility rules.")
    elif schema_version not in {"1", "2"}:
        warnings.append(
            f"{field_label} uses `telemetry_schema_version={schema_version}`; parsed with supported compatibility rules."
        )

    if not metadata.telemetry_file_kind:
        warnings.append(f"{field_label} is missing `telemetry_file_kind`; inferred `{expected_file_kind}` from the CSV header.")
    elif metadata.telemetry_file_kind != expected_file_kind:
        warnings.append(
            f"{field_label} metadata says `telemetry_file_kind={metadata.telemetry_file_kind}` but the parser expected `{expected_file_kind}`."
        )

    if not metadata.run_id:
        warnings.append(f"{field_label} metadata is missing `run_id`.")

    return warnings


def compare_metadata(
    baseline: TelemetryRunMetadata,
    candidate: TelemetryRunMetadata,
    field_label: str,
) -> list[str]:
    warnings: list[str] = []
    if baseline.run_id and candidate.run_id and baseline.run_id != candidate.run_id:
        warnings.append(f"{field_label} summary and stall files do not share the same `run_id`.")
    if baseline.save_name and candidate.save_name and baseline.save_name != candidate.save_name:
        warnings.append(f"{field_label} summary and stall files do not share the same `save_name`.")
    if baseline.scenario_id and candidate.scenario_id and baseline.scenario_id != candidate.scenario_id:
        warnings.append(f"{field_label} summary and stall files do not share the same `scenario_id`.")
    return warnings


def parse_csv_rows(
    csv_text: str,
    expected_fieldnames: list[str],
    row_parser,
) -> list[Any]:
    reader = csv.DictReader(io.StringIO(csv_text), skipinitialspace=True)
    actual_fieldnames = [field.strip() for field in (reader.fieldnames or [])]
    if actual_fieldnames != expected_fieldnames:
        raise AutomationError(
            "Unexpected telemetry CSV header. "
            f"Expected `{','.join(expected_fieldnames)}` but got `{','.join(actual_fieldnames)}`."
        )

    rows = []
    for raw_row in reader:
        if raw_row is None:
            continue
        if not any((value or "").strip() for value in raw_row.values()):
            continue
        rows.append(row_parser(normalize_csv_row(raw_row)))
    return rows


def select_summary_schema(csv_text: str) -> tuple[list[str], str]:
    header_tokens = get_csv_header_tokens(csv_text)
    if header_tokens == SUMMARY_FIELDNAMES_V2:
        return SUMMARY_FIELDNAMES_V2, "2"
    if header_tokens == SUMMARY_FIELDNAMES_V1:
        return SUMMARY_FIELDNAMES_V1, "1"
    raise AutomationError(
        "Unexpected telemetry CSV header. "
        f"Expected `{SUMMARY_HEADER_V1}` or `{SUMMARY_HEADER_V2}` but got `{','.join(header_tokens)}`."
    )


def get_csv_header_line(csv_text: str) -> str:
    for line in csv_text.replace("\r\n", "\n").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("# "):
            continue
        return stripped
    return ""


def get_csv_header_tokens(csv_text: str) -> list[str]:
    return normalize_csv_header_tokens(get_csv_header_line(csv_text))


def normalize_csv_header_tokens(line: str) -> list[str]:
    try:
        header_row = next(csv.reader([line], skipinitialspace=True), [])
    except csv.Error:
        return [line.strip()]
    return [token.strip() for token in header_row]


def normalize_csv_row(raw_row: dict[str | None, str | None]) -> dict[str, str]:
    return {(key or "").strip(): value or "" for key, value in raw_row.items() if key is not None}


def parse_summary_row(raw_row: dict[str, str]) -> SummaryRow:
    return SummaryRow(
        run_id=(raw_row.get("run_id") or "").strip(),
        elapsed_sec=parse_float(raw_row.get("elapsed_sec")),
        simulation_tick=parse_int(raw_row.get("simulation_tick")),
        fps_mean=parse_float(raw_row.get("fps_mean")),
        render_latency_mean_ms=parse_float(raw_row.get("render_latency_mean_ms")),
        render_latency_p95_ms=parse_float(raw_row.get("render_latency_p95_ms")),
        simulation_update_rate_mean=parse_float(raw_row.get("simulation_update_rate_mean")),
        simulation_update_interval_mean_ms=parse_float(raw_row.get("simulation_update_interval_mean_ms")),
        simulation_update_interval_p95_ms=parse_float(raw_row.get("simulation_update_interval_p95_ms")),
        simulation_step_mean_ms=parse_float(raw_row.get("simulation_step_mean_ms")),
        pathfind_update_mean_ms=parse_float(raw_row.get("pathfind_update_mean_ms")),
        mod_update_mean_ms=parse_float(raw_row.get("mod_update_mean_ms")),
        mod_entities_inspected_count=parse_int(raw_row.get("mod_entities_inspected_count")),
        mod_repath_requested_count=parse_int(raw_row.get("mod_repath_requested_count")),
        path_requests_pending_count=parse_int(raw_row.get("path_requests_pending_count")),
        path_queue_len_max=parse_int(raw_row.get("path_queue_len_max")),
        is_stall_window=parse_bool(raw_row.get("is_stall_window")),
    )


def parse_stall_row(raw_row: dict[str, str]) -> StallRow:
    return StallRow(
        run_id=(raw_row.get("run_id") or "").strip(),
        stall_id=parse_int(raw_row.get("stall_id")),
        stall_start_sec=parse_float(raw_row.get("stall_start_sec")),
        stall_end_sec=parse_float(raw_row.get("stall_end_sec")),
        stall_duration_sec=parse_float(raw_row.get("stall_duration_sec")),
        stall_peak_render_latency_ms=parse_float(raw_row.get("stall_peak_render_latency_ms")),
        stall_p95_render_latency_ms=parse_float(raw_row.get("stall_p95_render_latency_ms")),
        stall_peak_path_queue_len=parse_int(raw_row.get("stall_peak_path_queue_len")),
        stall_mod_repath_requested_count=parse_int(raw_row.get("stall_mod_repath_requested_count")),
        stall_mod_entities_inspected_count=parse_int(raw_row.get("stall_mod_entities_inspected_count")),
    )


def drop_terminal_summary_artifact_rows(summary_rows: list[SummaryRow], field_label: str) -> tuple[list[SummaryRow], list[str]]:
    filtered_rows = list(summary_rows)
    dropped = 0
    while len(filtered_rows) >= 2 and is_likely_terminal_summary_artifact(filtered_rows[-2], filtered_rows[-1]):
        filtered_rows.pop()
        dropped += 1

    if dropped == 0:
        return filtered_rows, []

    return filtered_rows, [
        f"{field_label} ignored {dropped} likely terminal flush artifact row(s) in `perf_summary.csv`."
    ]


def is_likely_terminal_summary_artifact(previous_row: SummaryRow, candidate_row: SummaryRow) -> bool:
    if candidate_row.simulation_tick != previous_row.simulation_tick:
        return False

    elapsed_delta = candidate_row.elapsed_sec - previous_row.elapsed_sec
    if elapsed_delta < 0.0 or elapsed_delta > 0.01:
        return False

    return candidate_row.simulation_step_mean_ms <= 0.0 and candidate_row.pathfind_update_mean_ms <= 0.0


def rollup_steady_state(
    summary_rows: list[SummaryRow],
    metadata: TelemetryRunMetadata,
) -> SteadyStateRollup | None:
    if not summary_rows:
        return None

    ordered_rows = sorted(summary_rows, key=lambda row: row.elapsed_sec)
    previous_elapsed = 0.0
    fallback_duration = metadata.sampling_interval_sec or 0.0
    for row in ordered_rows:
        raw_duration = row.elapsed_sec - previous_elapsed
        row.window_duration_sec = raw_duration if raw_duration > 0 else fallback_duration
        previous_elapsed = max(previous_elapsed, row.elapsed_sec)

    non_stall_rows = [row for row in ordered_rows if not row.is_stall_window]
    if not non_stall_rows:
        return None

    total_duration_sec = sum(max(row.window_duration_sec, 0.0) for row in non_stall_rows)
    if total_duration_sec <= 0:
        total_duration_sec = (metadata.sampling_interval_sec or 0.0) * len(non_stall_rows)

    return SteadyStateRollup(
        window_count=len(non_stall_rows),
        duration_sec=total_duration_sec,
        fps_mean=weighted_mean((row.fps_mean, row.window_duration_sec) for row in non_stall_rows),
        render_latency_mean_ms=weighted_mean((row.render_latency_mean_ms, row.window_duration_sec) for row in non_stall_rows),
        render_latency_p95_ms_mean=weighted_mean((row.render_latency_p95_ms, row.window_duration_sec) for row in non_stall_rows),
        simulation_step_mean_ms=weighted_mean((row.simulation_step_mean_ms, row.window_duration_sec) for row in non_stall_rows),
        pathfind_update_mean_ms=weighted_mean((row.pathfind_update_mean_ms, row.window_duration_sec) for row in non_stall_rows),
        mod_update_mean_ms=weighted_mean((row.mod_update_mean_ms, row.window_duration_sec) for row in non_stall_rows),
        mod_entities_inspected_per_sec=safe_divide(
            sum(row.mod_entities_inspected_count for row in non_stall_rows),
            total_duration_sec,
        ),
        mod_repath_requested_per_sec=safe_divide(
            sum(row.mod_repath_requested_count for row in non_stall_rows),
            total_duration_sec,
        ),
        path_requests_pending_mean=mean(row.path_requests_pending_count for row in non_stall_rows),
        path_requests_pending_p95=calculate_percentile(
            [float(row.path_requests_pending_count) for row in non_stall_rows],
            0.95,
        ),
        path_requests_pending_max=max(row.path_requests_pending_count for row in non_stall_rows),
        path_queue_len_mean=mean(row.path_queue_len_max for row in non_stall_rows),
        path_queue_len_p95=calculate_percentile(
            [float(row.path_queue_len_max) for row in non_stall_rows],
            0.95,
        ),
        path_queue_len_max=max(row.path_queue_len_max for row in non_stall_rows),
    )


def rollup_stalls(stall_rows: list[StallRow]) -> StallRollup:
    if not stall_rows:
        return StallRollup(True, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0, 0, 0)

    ordered_rows = sorted(stall_rows, key=lambda row: (row.stall_start_sec, row.stall_id))
    durations = [row.stall_duration_sec for row in ordered_rows]
    return StallRollup(
        file_available=True,
        count=len(ordered_rows),
        total_duration_sec=sum(durations),
        mean_duration_sec=mean(durations),
        p95_duration_sec=calculate_percentile(durations, 0.95),
        max_duration_sec=max(durations),
        peak_render_latency_ms=max(row.stall_peak_render_latency_ms for row in ordered_rows),
        peak_path_queue_len=max(row.stall_peak_path_queue_len for row in ordered_rows),
        total_mod_repath_requested=sum(row.stall_mod_repath_requested_count for row in ordered_rows),
        total_mod_entities_inspected=sum(row.stall_mod_entities_inspected_count for row in ordered_rows),
    )


def detect_queue_metric_sampling_warnings(run_analysis: RunAnalysis) -> list[str]:
    queue_sampling_state = run_analysis.metadata.path_queue_sampling_state
    if queue_sampling_state == QUEUE_SAMPLING_STATE_FAILED:
        return [
            "path queue sampling failed for this run; queue-based conclusions are suppressed."
        ]
    if queue_sampling_state == QUEUE_SAMPLING_STATE_PARTIAL:
        return [
            "path queue sampling is partial for this run; queue-based conclusions are suppressed."
        ]
    if queue_sampling_state == QUEUE_SAMPLING_STATE_UNKNOWN:
        return [
            "path queue sampling state is unknown for this run; queue-based conclusions are suppressed."
        ]

    steady_state = run_analysis.steady_state
    if steady_state is None:
        return []

    if steady_state.path_queue_len_max != 0:
        return []

    stalls = run_analysis.stalls
    if stalls is not None and stalls.peak_path_queue_len != 0:
        return []

    has_path_pressure = steady_state.path_requests_pending_max >= 100 or steady_state.path_requests_pending_p95 >= 50
    has_pathfind_activity = steady_state.pathfind_update_mean_ms >= 0.05
    if not has_path_pressure or not has_pathfind_activity:
        return []

    return [
        "path queue metrics are all zero despite non-trivial pathfind activity; verify PathfindQueueSystem sampling and check the mod log for telemetry bind errors."
    ]


def compare_runs(baseline: RunAnalysis, comparison: RunAnalysis) -> ComparisonAnalysis:
    warnings: list[str] = []
    directly_comparable = True
    comparability_basis = ""

    baseline_metadata = baseline.metadata
    comparison_metadata = comparison.metadata
    baseline_schema_version = baseline_metadata.telemetry_schema_version.strip() or "unknown"
    comparison_schema_version = comparison_metadata.telemetry_schema_version.strip() or "unknown"

    if (
        baseline_schema_version != "unknown"
        and comparison_schema_version != "unknown"
        and baseline_schema_version != comparison_schema_version
    ):
        directly_comparable = False
        warnings.append(
            "telemetry_schema_version mismatch: "
            f"`{baseline_schema_version}` vs `{comparison_schema_version}`; "
            "direct comparison is not allowed across schema versions."
        )

    save_names_known = is_known_save_name(baseline_metadata.save_name) and is_known_save_name(comparison_metadata.save_name)
    scenario_ids_known = is_known_scenario_id(baseline_metadata.scenario_id) and is_known_scenario_id(comparison_metadata.scenario_id)

    same_save_verified = (
        is_known_save_name(baseline_metadata.save_name)
        and is_known_save_name(comparison_metadata.save_name)
        and baseline_metadata.save_name == comparison_metadata.save_name
    )
    same_scenario_verified = (
        is_known_scenario_id(baseline_metadata.scenario_id)
        and is_known_scenario_id(comparison_metadata.scenario_id)
        and baseline_metadata.scenario_id == comparison_metadata.scenario_id
    )

    if save_names_known and baseline_metadata.save_name != comparison_metadata.save_name:
        if same_scenario_verified:
            warnings.append(
                "save_name mismatch: "
                f"`{baseline_metadata.save_name}` vs `{comparison_metadata.save_name}`; "
                "allowing direct comparison because scenario_id matches."
            )
        else:
            warnings.append(
                f"save_name mismatch: `{baseline_metadata.save_name}` vs `{comparison_metadata.save_name}`."
            )
    if scenario_ids_known and baseline_metadata.scenario_id != comparison_metadata.scenario_id:
        if same_save_verified:
            warnings.append(
                "scenario_id mismatch: "
                f"`{baseline_metadata.scenario_id}` vs `{comparison_metadata.scenario_id}`; "
                "allowing direct comparison because save_name matches."
            )
        else:
            warnings.append(
                f"scenario_id mismatch: `{baseline_metadata.scenario_id}` vs `{comparison_metadata.scenario_id}`."
            )

    if not same_save_verified and not same_scenario_verified:
        directly_comparable = False
        warnings.append("matching save or scenario identity could not be verified from telemetry metadata.")

    fix_toggle_differences, unknown_fix_toggles = compare_fix_toggle_state(baseline_metadata, comparison_metadata)
    if unknown_fix_toggles:
        directly_comparable = False
        warnings.append("enabled-fix state could not be fully verified from telemetry metadata.")
        warnings.append(
            "fix-toggle metadata missing or unknown for: "
            + ", ".join(f"`{name}`" for name in unknown_fix_toggles)
            + "."
        )
    elif len(fix_toggle_differences) > 1:
        directly_comparable = False
        warnings.append(
            "enabled-fix set mismatch spans multiple fix toggles; direct comparison requires at most one fix-toggle delta."
        )
        warnings.append(
            "fix-toggle differences: " + ", ".join(f"`{name}`" for name in fix_toggle_differences) + "."
        )
    elif len(fix_toggle_differences) == 1:
        comparability_basis = "single_fix_toggle_delta"
    else:
        comparability_basis = "same_fix_set"

    if baseline_metadata.sampling_interval_sec != comparison_metadata.sampling_interval_sec:
        directly_comparable = False
        warnings.append("sampling_interval_sec mismatch between baseline and comparison.")

    if baseline_metadata.stall_threshold_ms != comparison_metadata.stall_threshold_ms:
        directly_comparable = False
        warnings.append("stall_threshold_ms mismatch between baseline and comparison.")

    if baseline_metadata.game_build_version and comparison_metadata.game_build_version:
        if baseline_metadata.game_build_version != comparison_metadata.game_build_version:
            warnings.append(
                f"game_build_version mismatch: `{baseline_metadata.game_build_version}` vs `{comparison_metadata.game_build_version}`."
            )

    if baseline_metadata.mod_version and comparison_metadata.mod_version:
        if baseline_metadata.mod_version != comparison_metadata.mod_version:
            warnings.append(
                f"mod_version mismatch: `{baseline_metadata.mod_version}` vs `{comparison_metadata.mod_version}`."
            )

    analysis = ComparisonAnalysis(
        directly_comparable=directly_comparable,
        status=COMPARISON_STATUS_NOT_DIRECTLY_COMPARABLE,
        comparability_basis=comparability_basis if directly_comparable else "",
        fix_toggle_differences=fix_toggle_differences,
        warnings=dedupe_preserve_order(warnings),
    )
    if not directly_comparable:
        return analysis

    analysis.status = (
        COMPARISON_STATUS_COMPARABLE_SINGLE_FIX_TOGGLE
        if comparability_basis == "single_fix_toggle_delta"
        else COMPARISON_STATUS_COMPARABLE
    )
    queue_metrics_usable = queue_sampling_is_usable(baseline.metadata) and queue_sampling_is_usable(comparison.metadata)
    if not queue_metrics_usable:
        analysis.warnings.append(
            "queue metrics were excluded from direct comparison because path queue sampling was not `ok` for one or both runs."
        )

    if baseline.steady_state is not None and comparison.steady_state is not None:
        analysis.steady_state_fps_delta = comparison.steady_state.fps_mean - baseline.steady_state.fps_mean
        analysis.steady_state_render_latency_delta_ms = (
            comparison.steady_state.render_latency_mean_ms - baseline.steady_state.render_latency_mean_ms
        )
        analysis.steady_state_pathfind_update_delta_ms = (
            comparison.steady_state.pathfind_update_mean_ms - baseline.steady_state.pathfind_update_mean_ms
        )
        analysis.steady_state_mod_update_delta_ms = (
            comparison.steady_state.mod_update_mean_ms - baseline.steady_state.mod_update_mean_ms
        )
        if queue_metrics_usable:
            analysis.steady_state_path_queue_p95_delta = (
                comparison.steady_state.path_queue_len_p95 - baseline.steady_state.path_queue_len_p95
            )
    else:
        analysis.warnings.append("steady-state deltas are unavailable because one run has no non-stall summary windows.")

    if baseline.stalls is not None and comparison.stalls is not None:
        analysis.stall_count_delta = comparison.stalls.count - baseline.stalls.count
        analysis.total_stall_duration_sec_delta = comparison.stalls.total_duration_sec - baseline.stalls.total_duration_sec
        analysis.max_stall_duration_sec_delta = comparison.stalls.max_duration_sec - baseline.stalls.max_duration_sec
        if queue_metrics_usable:
            analysis.stall_peak_path_queue_delta = comparison.stalls.peak_path_queue_len - baseline.stalls.peak_path_queue_len
    else:
        analysis.warnings.append("stall deltas are unavailable because one run has no valid `perf_stalls.csv` data.")

    analysis.warnings = dedupe_preserve_order(analysis.warnings)
    return analysis


def compare_fix_toggle_state(
    baseline_metadata: TelemetryRunMetadata,
    comparison_metadata: TelemetryRunMetadata,
) -> tuple[list[str], list[str]]:
    differing: list[str] = []
    unknown: list[str] = []

    for field_name, display_name in FIX_TOGGLE_FIELDS:
        baseline_value = getattr(baseline_metadata, field_name)
        comparison_value = getattr(comparison_metadata, field_name)
        if baseline_value is None or comparison_value is None:
            unknown.append(display_name)
            continue
        if baseline_value != comparison_value:
            differing.append(display_name)

    return differing, unknown


def detect_anomaly_flags(
    baseline: RunAnalysis,
    comparison: RunAnalysis | None,
    comparison_analysis: ComparisonAnalysis | None,
) -> list[str]:
    flags: list[str] = []

    if comparison is not None and comparison_analysis is not None and comparison_analysis.directly_comparable:
        if (
            baseline.steady_state is not None
            and comparison.steady_state is not None
            and comparison_analysis.steady_state_mod_update_delta_ms is not None
        ):
            threshold = max(0.25, baseline.steady_state.mod_update_mean_ms * 0.2)
            if comparison_analysis.steady_state_mod_update_delta_ms >= threshold:
                flags.append("steady_state_mod_overhead_elevated")

        if comparison_analysis.stall_count_delta is not None and comparison_analysis.total_stall_duration_sec_delta is not None:
            if comparison_analysis.stall_count_delta >= 1 or comparison_analysis.total_stall_duration_sec_delta >= 5.0:
                flags.append("stall_frequency_elevated")

        if comparison_analysis.max_stall_duration_sec_delta is not None and comparison_analysis.max_stall_duration_sec_delta >= 5.0:
            flags.append("stall_duration_elevated")

        if comparison_analysis.stall_peak_path_queue_delta is not None:
            baseline_peak_queue = baseline.stalls.peak_path_queue_len if baseline.stalls is not None else 0
            if comparison_analysis.stall_peak_path_queue_delta >= max(20, int(baseline_peak_queue * 0.5)):
                flags.append("queue_pressure_during_stalls")

        if baseline.stalls is not None and comparison.stalls is not None:
            baseline_stall_rate = safe_divide(
                baseline.stalls.total_mod_repath_requested,
                max(baseline.stalls.total_duration_sec, 1.0),
            )
            comparison_stall_rate = safe_divide(
                comparison.stalls.total_mod_repath_requested,
                max(comparison.stalls.total_duration_sec, 1.0),
            )
            if comparison_stall_rate >= max(1.0, baseline_stall_rate * 2.0):
                flags.append("mod_activity_rises_during_stalls")
    else:
        if baseline.steady_state is not None and baseline.steady_state.mod_update_mean_ms >= 2.0:
            flags.append("steady_state_mod_overhead_elevated")

        if baseline.stalls is not None:
            if baseline.stalls.count >= 3 and baseline.stalls.total_duration_sec >= 10.0:
                flags.append("stall_frequency_elevated")
            if baseline.stalls.max_duration_sec >= 5.0:
                flags.append("stall_duration_elevated")
            if queue_sampling_is_usable(baseline.metadata) and baseline.stalls.peak_path_queue_len >= 100:
                flags.append("queue_pressure_during_stalls")
            if baseline.steady_state is not None:
                baseline_non_stall_rate = baseline.steady_state.mod_repath_requested_per_sec
                stall_rate = safe_divide(
                    baseline.stalls.total_mod_repath_requested,
                    max(baseline.stalls.total_duration_sec, 1.0),
                )
                if baseline.stalls.count > 0 and stall_rate >= max(1.0, baseline_non_stall_rate * 2.0):
                    flags.append("mod_activity_rises_during_stalls")

    return dedupe_preserve_order(flags)


def build_follow_up_suggestions(
    baseline: RunAnalysis,
    comparison: RunAnalysis | None,
    comparison_analysis: ComparisonAnalysis | None,
    anomaly_flags: list[str],
) -> list[str]:
    suggestions: list[str] = []

    if comparison is None:
        suggestions.append("same-save rerun recommended")
    elif comparison_analysis is not None and not comparison_analysis.directly_comparable:
        suggestions.append("capture a paired comparison with matching save/settings/threshold")

    if any(
        flag in anomaly_flags
        for flag in (
            "stall_frequency_elevated",
            "stall_duration_elevated",
            "queue_pressure_during_stalls",
            "mod_activity_rises_during_stalls",
        )
    ):
        suggestions.append("collect matching diagnostics log for semantic cause analysis")

    if baseline.stalls is None:
        suggestions.append("include perf_stalls.csv in the next capture")

    if not queue_sampling_is_usable(baseline.metadata) or (
        comparison is not None and not queue_sampling_is_usable(comparison.metadata)
    ):
        suggestions.append("verify queue sampling health before using queue metrics")

    return dedupe_preserve_order(suggestions)


def normalize_queue_sampling_state(value: str | None) -> str:
    normalized = (value or "").strip().lower()
    if normalized in {
        QUEUE_SAMPLING_STATE_OK,
        QUEUE_SAMPLING_STATE_PARTIAL,
        QUEUE_SAMPLING_STATE_FAILED,
    }:
        return normalized
    return QUEUE_SAMPLING_STATE_UNKNOWN


def normalize_queue_sampling_reason(value: str | None, sampling_state: str) -> str:
    normalized = (value or "").strip().lower()
    if normalized in {
        QUEUE_SAMPLING_REASON_NONE,
        QUEUE_SAMPLING_REASON_UNSUPPORTED_FIELDS,
        QUEUE_SAMPLING_REASON_BIND_FAILED,
        QUEUE_SAMPLING_REASON_RUNTIME_ERROR,
    }:
        return normalized
    if sampling_state == QUEUE_SAMPLING_STATE_PARTIAL:
        return QUEUE_SAMPLING_REASON_UNSUPPORTED_FIELDS
    if sampling_state == QUEUE_SAMPLING_STATE_OK:
        return QUEUE_SAMPLING_REASON_NONE
    return QUEUE_SAMPLING_REASON_UNKNOWN


def queue_sampling_is_usable(metadata: TelemetryRunMetadata) -> bool:
    return metadata.path_queue_sampling_state == QUEUE_SAMPLING_STATE_OK


def format_queue_sampling_summary_line(run_analysis: RunAnalysis) -> str:
    queue_sampling_state = run_analysis.metadata.path_queue_sampling_state
    queue_sampling_reason = run_analysis.metadata.path_queue_sampling_reason
    if queue_sampling_state == QUEUE_SAMPLING_STATE_OK:
        return f"- {run_analysis.label} queue sampling: ok"
    if queue_sampling_state == QUEUE_SAMPLING_STATE_UNKNOWN:
        return (
            f"- {run_analysis.label} queue sampling: unknown; queue-based conclusions are suppressed for this run"
        )
    return (
        f"- {run_analysis.label} queue sampling: {queue_sampling_state} (`{queue_sampling_reason}`); "
        "queue-based conclusions are suppressed for this run"
    )


def download_attachment_bytes(url: str) -> bytes:
    if not is_allowed_attachment_url(url):
        raise AttachmentDownloadError(
            f"Telemetry attachment host is not allowed: {sanitize_url(url)}. Only GitHub-hosted attachment URLs are accepted."
        )

    request = urllib.request.Request(
        url,
        headers={"User-Agent": USER_AGENT, "Accept": "*/*"},
        method="GET",
    )
    try:
        with urllib.request.urlopen(request) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        message = error.read().decode("utf-8", errors="replace")
        raise AttachmentDownloadError(
            f"Failed to download telemetry attachment ({error.code}): {message or error.reason}"
        ) from error
    except urllib.error.URLError as error:
        raise AttachmentDownloadError(f"Failed to download telemetry attachment: {error.reason}") from error


def extract_fenced_blocks(text: str) -> list[str]:
    return [
        match.group(1).strip()
        for match in re.finditer(r"```(?:[^\n`]*)\n(.*?)```", text, flags=re.DOTALL)
        if match.group(1).strip()
    ]


def calculate_percentile(values: list[float], percentile: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    raw_index = int(math.ceil(len(ordered) * percentile)) - 1
    percentile_index = max(0, min(len(ordered) - 1, raw_index))
    return float(ordered[percentile_index])


def mean(values) -> float:
    values_list = [float(value) for value in values]
    if not values_list:
        return 0.0
    return sum(values_list) / len(values_list)


def weighted_mean(pairs) -> float:
    total_weight = 0.0
    weighted_total = 0.0
    for value, weight in pairs:
        current_weight = max(float(weight), 0.0)
        weighted_total += float(value) * current_weight
        total_weight += current_weight
    if total_weight <= 0:
        return 0.0
    return weighted_total / total_weight


def safe_divide(numerator: float, denominator: float) -> float:
    if denominator <= 0:
        return 0.0
    return float(numerator) / float(denominator)


def parse_float(value: str | None) -> float:
    if value is None or not value.strip():
        return 0.0
    return float(value.strip())


def parse_int(value: str | None) -> int:
    if value is None or not value.strip():
        return 0
    return int(float(value.strip()))


def parse_bool(value: str | None) -> bool:
    if value is None:
        return False
    return value.strip().lower() == "true"


def parse_optional_float(value: str | None) -> float | None:
    if value is None or not value.strip():
        return None
    return float(value.strip())


def parse_optional_int(value: str | None) -> int | None:
    if value is None or not value.strip():
        return None
    return int(float(value.strip()))


def parse_optional_bool(value: str | None) -> bool | None:
    if value is None or not value.strip():
        return None
    return value.strip().lower() == "true"


def dedupe_preserve_order(values: list[str]) -> list[str]:
    seen: set[str] = set()
    ordered: list[str] = []
    for value in values:
        normalized = value.strip()
        if not normalized or normalized in seen:
            continue
        seen.add(normalized)
        ordered.append(normalized)
    return ordered


def format_float(value: float | None) -> str:
    if value is None:
        return "n/a"
    return f"{value:.2f}".rstrip("0").rstrip(".")


def format_signed_float(value: float | None) -> str:
    if value is None:
        return "n/a"
    return f"{value:+.2f}".rstrip("0").rstrip(".")


def format_signed_int(value: int | None) -> str:
    if value is None:
        return "n/a"
    return f"{value:+d}"


def fallback_text(value: str, fallback: str) -> str:
    return value if value.strip() else fallback


def truncate_text(value: str, limit: int) -> str:
    if len(value) <= limit:
        return value
    return value[: max(0, limit - 3)] + "..."


def looks_like_zip_bytes(content: bytes) -> bool:
    return content.startswith(b"PK\x03\x04")


def sanitized_url_basename(url: str) -> str:
    parsed = urllib.parse.urlsplit(sanitize_url(url))
    return PurePosixPath(parsed.path).name or "attachment"


def is_known_save_name(save_name: str) -> bool:
    normalized = save_name.strip()
    return bool(normalized) and normalized.lower() != UNSAVED_NAME


def is_known_scenario_id(scenario_id: str) -> bool:
    normalized = scenario_id.strip()
    return bool(normalized) and normalized.lower() != UNKNOWN_SCENARIO_ID
