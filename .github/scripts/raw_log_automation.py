import json
import os
import re
import textwrap
import urllib.error
import urllib.parse
import urllib.request
from typing import Any


RAW_LOG_FORM_MARKER = "<!-- raw-log-report -->"
RAW_LOG_TITLE_PREFIX = "[Raw Log]"
MANAGED_COMMENT_MARKER = "<!-- raw-log-triage:managed-comment -->"
REPLY_TEMPLATE_START_MARKER = "<!-- raw-log-triage:reply-template:start -->"
REPLY_TEMPLATE_END_MARKER = "<!-- raw-log-triage:reply-template:end -->"
OVERRIDES_START_MARKER = "<!-- raw-log-triage:overrides:start -->"
OVERRIDES_END_MARKER = "<!-- raw-log-triage:overrides:end -->"
PAYLOAD_START_MARKER = "<!-- raw-log-triage:machine-payload:start -->"
PAYLOAD_END_MARKER = "<!-- raw-log-triage:machine-payload:end -->"
SOURCE_RAW_ISSUE_MARKER = "<!-- source-raw-log-issue:{issue_number} -->"
SOURCE_RAW_COMMENT_MARKER = "<!-- source-raw-log-comment:{comment_id} -->"
PROMOTE_COMMAND = "/promote-evidence"

# MachineParsedLogContract.cs is the source of truth for these machine-parsed
# prefixes. Keep the Python parser in sync with that contract.
DIAGNOSTICS_OBSERVATION_PREFIX = "softwareEvidenceDiagnostics observation_window("
DIAGNOSTICS_DETAIL_PREFIX = "softwareEvidenceDiagnostics detail("
SOFTWARE_OFFICE_STATES_DETAIL_MARKER = "detail_type=softwareOfficeStates"
PHANTOM_CORRECTION_PREFIX = "Signature phantom vacancy guard corrected"
PATCH_SUMMARY_PREFIXES = (
    "Office resource storage patch applied for the current load.",
    "Office resource storage patch applied.",
)

GITHUB_API_BASE_URL = "https://api.github.com"
GITHUB_MODELS_CHAT_COMPLETIONS_URL = "https://models.github.ai/inference/chat/completions"
GITHUB_ATTACHMENT_HOST_SUFFIX = ".githubusercontent.com"
DEFAULT_GITHUB_MODELS_MODEL = "openai/gpt-4.1-mini"
DEFAULT_SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL = "openai/gpt-4.1"
PRIMARY_GITHUB_MODELS_MODEL_ENV = "PRIMARY_GITHUB_MODELS_MODEL"
ESCALATION_GITHUB_MODELS_MODEL_ENV = "ESCALATION_GITHUB_MODELS_MODEL"
SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL_ENV = "SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL"
COMMENT_BODY_LIMIT = 60000
DETAIL_PREVIEW_LIMIT = 1800
LLM_DETAIL_EXCERPT_LIMIT = 700
LLM_SUMMARY_LINE_LIMIT = 220
LLM_TITLE_MAX_LENGTH = 140
LLM_EVIDENCE_SUMMARY_MAX_LENGTH = 500
LLM_COMPARISON_BASELINE_MAX_LENGTH = 280
LLM_CONFOUNDERS_MAX_LENGTH = 400
LLM_ANALYSIS_BASIS_MAX_LENGTH = 240
LLM_LOG_EXCERPT_MAX_LENGTH = 2200
LLM_NOTES_MAX_LENGTH = 1000
LLM_REASONING_SUMMARY_MAX_LENGTH = 220
LLM_MAX_PATCH_SUMMARIES = 2
LLM_MAX_PHANTOM_CORRECTIONS = 2
MAX_EXCERPT_DETAIL_LINES = 3
MAX_EXCERPT_CANDIDATES_PER_GROUP = 2
MAX_SELECTED_EXCERPT_CANDIDATES = 4
MAX_DETERMINISTIC_EXCERPT_CANDIDATES = 2
MAX_STYLE_EXAMPLES = 2
LLM_CONTEXT_VARIANTS = (
    {
        "name": "default",
        "anchor_limit": 12,
        "anchor_excerpt_limit": LLM_SUMMARY_LINE_LIMIT,
        "anchor_detail_limit": DETAIL_PREVIEW_LIMIT,
        "snippet_limit": 8,
        "snippet_text_limit": DETAIL_PREVIEW_LIMIT,
        "candidate_limit": 4,
        "candidate_lines_per_item": MAX_EXCERPT_DETAIL_LINES,
        "candidate_line_limit": LLM_DETAIL_EXCERPT_LIMIT,
        "style_example_limit": MAX_STYLE_EXAMPLES,
        "semantic_fact_limit": 8,
        "semantic_fact_text_limit": 260,
        "what_happened_limit": 500,
        "platform_notes_limit": 300,
        "other_mods_limit": 300,
        "fallback_summary_limit": 500,
        "fallback_notes_limit": 700,
        "redaction_note_limit": 120,
    },
    {
        "name": "compact",
        "anchor_limit": 8,
        "anchor_excerpt_limit": 160,
        "anchor_detail_limit": 700,
        "snippet_limit": 4,
        "snippet_text_limit": 700,
        "candidate_limit": 2,
        "candidate_lines_per_item": 2,
        "candidate_line_limit": 400,
        "style_example_limit": 1,
        "semantic_fact_limit": 6,
        "semantic_fact_text_limit": 220,
        "what_happened_limit": 320,
        "platform_notes_limit": 180,
        "other_mods_limit": 180,
        "fallback_summary_limit": 320,
        "fallback_notes_limit": 420,
        "redaction_note_limit": 90,
    },
    {
        "name": "minimal",
        "anchor_limit": 4,
        "anchor_excerpt_limit": 120,
        "anchor_detail_limit": 320,
        "snippet_limit": 2,
        "snippet_text_limit": 320,
        "candidate_limit": 2,
        "candidate_lines_per_item": 1,
        "candidate_line_limit": 220,
        "style_example_limit": 0,
        "semantic_fact_limit": 4,
        "semantic_fact_text_limit": 180,
        "what_happened_limit": 180,
        "platform_notes_limit": 120,
        "other_mods_limit": 120,
        "fallback_summary_limit": 220,
        "fallback_notes_limit": 260,
        "redaction_note_limit": 70,
    },
)

RAW_LOG_FORM_LABELS = {
    "game version": "game_version",
    "mod version": "mod_version",
    "save or city label": "save_or_city_label",
    "what happened": "what_happened",
    "platform notes": "platform_notes",
    "other mods": "other_mods",
    "raw log": "raw_log",
}

RAW_LOG_REQUIRED_FIELDS = (
    "game_version",
    "mod_version",
    "save_or_city_label",
    "what_happened",
    "raw_log",
)

OVERRIDE_FIELD_ORDER = [
    "title",
    "scenario_label",
    "scenario_type",
    "reproduction_conditions",
    "mod_ref",
    "platform_notes",
    "comparison_baseline",
    "symptom_classification",
    "custom_symptom_classification",
    "evidence_summary",
    "confidence",
    "confounders",
    "analysis_basis",
    "log_excerpt",
    "notes",
]

SOFTWARE_EVIDENCE_CANONICAL_CLASSIFICATIONS = {
    "software_office_propertyless",
    "software_office_efficiency_zero",
    "software_office_lack_resources_zero",
    "software_demand_mismatch",
    "software_track_unclear",
}
NON_FATAL_VALIDATION_ERRORS = {
    "unsupported_missing_user_input",
    "unsupported_evidence_summary_interpretation",
    "unsupported_notes_interpretation",
    "unsupported_reasoning_summary_format",
    "unsupported_excerpt_line",
}
UNSUPPORTED_SUMMARY_PATTERNS = [
    r"\bresolved\b",
    r"\bresolving\b",
    r"\bsignificantly\b",
]
UNSUPPORTED_NOTES_PATTERNS = [
    r"\bthis suggests\b",
    r"\bthis indicates\b",
    r"\bthis implies\b",
    r"\bunresolved\b",
    r"\blikely\b",
]
UNSUPPORTED_PHANTOM_ZERO_PATTERNS = [
    r"\bphantom[-\s]*vacanc(?:y|ies)\b.{0,120}\b(?:remain|remained|stayed|stay|were|was)\s+(?:at\s+)?zero\b",
    r"\bguard[-\s]*corrections?\b.{0,120}\b(?:remain|remained|stayed|stay|were|was)\s+(?:at\s+)?zero\b",
]
UNSUPPORTED_PHANTOM_ABSENCE_PATTERNS = [
    r"\bno indication of phantom[-\s]*vacanc(?:y|ies)\b",
    r"\bno detected phantom[-\s]*vacanc(?:y|ies)\b",
    r"\bno phantom[-\s]*vacanc(?:y|ies)\b",
    r"\bno phantom[-\s]*vacanc(?:y|ies)\b.{0,120}\bguard[-\s]*corrections?\b.{0,80}\b(?:detected|observed|seen|present)\b",
    r"\bno detected guard[-\s]*corrections?\b",
    r"\bno guard[-\s]*corrections?\b.{0,80}\b(?:detected|observed|seen|present)\b",
    r"\bphantom[-\s]*vacancy activity was absent\b",
]

EVIDENCE_STYLE_EXAMPLES = [
    {
        "name": "same-save comparison run",
        "summary_style": "Start with the tested condition, then describe the bounded window outcome without causal claims.",
        "example_summary": "With `EnableTradePatch=False`, the same save lineage still reproduced consumer-side shortage before ending with producer-side distress while office demand remained high.",
        "log_excerpt_style": "Use `### Day ...` subsections with fenced `text` blocks that quote only the selected producer or consumer detail lines.",
        "notes_style": "Use 3-5 factual bullets that walk through the run chronologically: stable start, recent anchored detail, final sample, and any important trade-state cue.",
    },
    {
        "name": "single bounded run",
        "summary_style": "Describe what the diagnostics captured in the window, not why it happened.",
        "example_summary": "This bounded run captured a day-21 software-consumer shortage and a day-22 producer-side `Electronics(stock=0)` signal while office demand remained high.",
        "log_excerpt_style": "Prefer one producer block and one consumer block when both help interpretation; omit extra blocks if they do not add signal.",
        "notes_style": "Bullets may mention office-demand counters, consumer `softwareInputZero`, producer `efficiencyZero` / `lackResourcesZero`, and a reminder that `officeDemand.buildingDemand` is a factor counter.",
    },
]


class AutomationError(RuntimeError):
    pass


class AttachmentDownloadError(AutomationError):
    pass


def get_primary_github_models_model() -> str:
    return os.getenv(PRIMARY_GITHUB_MODELS_MODEL_ENV, "").strip() or DEFAULT_GITHUB_MODELS_MODEL


def get_escalation_github_models_model() -> str:
    return os.getenv(ESCALATION_GITHUB_MODELS_MODEL_ENV, "").strip()


def get_summary_refinement_github_models_model() -> str:
    return (
        os.getenv(SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL_ENV, "").strip()
        or DEFAULT_SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL
    )


def load_event_payload(event_path: str) -> dict[str, Any]:
    with open(event_path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def read_text_file(path: str) -> str:
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_code_fences(text: str) -> str:
    stripped = text.strip()
    if stripped.startswith("```") and stripped.endswith("```"):
        lines = stripped.splitlines()
        if len(lines) >= 2:
            return "\n".join(lines[1:-1]).strip()
    return text.strip()


def clean_issue_form_value(text: str) -> str:
    cleaned = strip_code_fences(text.replace("\r\n", "\n").strip())
    if cleaned == "_No response_":
        return ""
    return cleaned


def parse_issue_form_sections(body: str) -> dict[str, str]:
    sections: dict[str, list[str]] = {}
    current_header: str | None = None

    for line in body.replace("\r\n", "\n").split("\n"):
        if line.startswith("### "):
            current_header = line[4:].strip().lower()
            sections[current_header] = []
            continue

        if current_header is not None:
            sections[current_header].append(line)

    parsed: dict[str, str] = {}
    for header, field_name in RAW_LOG_FORM_LABELS.items():
        parsed[field_name] = clean_issue_form_value("\n".join(sections.get(header, [])))

    return parsed


def is_raw_log_issue(issue_body: str, issue_title: str = "") -> bool:
    if RAW_LOG_FORM_MARKER in issue_body:
        return True

    if not issue_title.strip().startswith(RAW_LOG_TITLE_PREFIX):
        return False

    parsed_sections = parse_issue_form_sections(issue_body)
    return all(parsed_sections.get(field_name) for field_name in RAW_LOG_REQUIRED_FIELDS)


def extract_markdown_links(text: str) -> list[str]:
    return re.findall(r"\[[^\]]+\]\((https?://[^)\s]+)\)", text)


def extract_attachment_urls(raw_log_section: str) -> list[str]:
    urls = extract_markdown_links(raw_log_section)
    urls.extend(re.findall(r"(?<!\()https?://[^\s)]+", raw_log_section))

    seen: set[str] = set()
    ordered_urls: list[str] = []
    for url in urls:
        if url in seen:
            continue
        seen.add(url)
        ordered_urls.append(url)
    return ordered_urls


def sanitize_url(url: str) -> str:
    parsed = urllib.parse.urlsplit(url)
    return urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, parsed.path, "", ""))


def is_allowed_attachment_url(url: str) -> bool:
    parsed = urllib.parse.urlsplit(url)
    host = (parsed.hostname or "").lower()
    if parsed.scheme != "https" or not host:
        return False

    if host == "github.com":
        return parsed.path.startswith("/user-attachments/")

    return host.endswith(GITHUB_ATTACHMENT_HOST_SUFFIX)


def redact_log_text(log_text: str) -> tuple[str, list[str]]:
    redaction_notes: list[str] = []
    redacted = log_text.replace("\r\n", "\n")

    def replace_path(match: re.Match[str]) -> str:
        path = match.group(0).replace("\\", "/")
        redaction_notes.append("Redacted local filesystem path.")
        path = re.sub(r"/Users/[^/]+", "/Users/<redacted-user>", path, flags=re.IGNORECASE)
        return "<redacted-path>" + path[path.rfind("/") :]

    redacted = re.sub(r"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^ \n\r\t\"']+", replace_path, redacted)
    redacted = re.sub(
        r"/(?:Users|home)/[^/\s]+(?:/[^\s\"']+)+",
        replace_path,
        redacted,
        flags=re.IGNORECASE,
    )

    def replace_url(match: re.Match[str]) -> str:
        url = match.group(0)
        sanitized = sanitize_url(url)
        if sanitized != url:
            redaction_notes.append("Stripped URL query parameters from raw log text.")
        return sanitized

    redacted = re.sub(r"https?://[^\s)]+", replace_url, redacted)
    return redacted, list(dict.fromkeys(redaction_notes))


def http_request(
    method: str,
    url: str,
    *,
    token: str | None = None,
    payload: dict[str, Any] | None = None,
    headers: dict[str, str] | None = None,
) -> tuple[int, Any, str]:
    request_headers = {
        "Accept": "application/vnd.github+json, application/json",
        "User-Agent": "NoOfficeDemandFixRawLogAutomation/1.0",
    }
    if headers:
        request_headers.update(headers)
    if token:
        request_headers["Authorization"] = f"Bearer {token}"

    body: bytes | None = None
    if payload is not None:
        body = json.dumps(payload).encode("utf-8")
        request_headers["Content-Type"] = "application/json"

    request = urllib.request.Request(url, data=body, headers=request_headers, method=method.upper())
    try:
        with urllib.request.urlopen(request) as response:
            raw = response.read().decode("utf-8")
            parsed = json.loads(raw) if raw else {}
            return response.status, parsed, raw
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8")
        parsed = json.loads(raw) if raw else {}
        return error.code, parsed, raw
    except urllib.error.URLError as error:
        host = urllib.parse.urlsplit(url).netloc or url
        raise AutomationError(
            f"HTTP {method.upper()} request to {host} failed: {error.reason}"
        ) from error


def download_attachment(url: str) -> str:
    if not is_allowed_attachment_url(url):
        sanitized_url = sanitize_url(url)
        raise AttachmentDownloadError(
            f"Raw log attachment host is not allowed: {sanitized_url}. "
            "Only GitHub-hosted attachment URLs are accepted."
        )

    request_headers = {
        "User-Agent": "NoOfficeDemandFixRawLogAutomation/1.0",
        "Accept": "*/*",
    }

    request = urllib.request.Request(url, headers=request_headers, method="GET")
    try:
        with urllib.request.urlopen(request) as response:
            return response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as error:
        message = error.read().decode("utf-8", errors="replace")
        raise AttachmentDownloadError(
            f"Failed to download attachment ({error.code}): {message or error.reason}"
        ) from error
    except urllib.error.URLError as error:
        raise AttachmentDownloadError(f"Failed to download attachment: {error.reason}") from error


def select_raw_log_source(issue_fields: dict[str, str]) -> dict[str, Any]:
    raw_log_section = issue_fields.get("raw_log", "")
    attachment_urls = extract_attachment_urls(raw_log_section)

    if attachment_urls:
        preferred_url = attachment_urls[0]
        return {
            "mode": "attachment",
            "url": preferred_url,
            "text": download_attachment(preferred_url),
            "attachment_urls": attachment_urls,
        }

    return {
        "mode": "inline",
        "url": "",
        "text": strip_code_fences(raw_log_section),
        "attachment_urls": [],
    }


def split_group_entries(raw: str) -> list[tuple[str, str]]:
    entries: list[tuple[str, str]] = []
    depth = 0
    current: list[str] = []
    name: list[str] = []
    reading_name = True

    for char in raw:
        if reading_name:
            if char == "(":
                reading_name = False
                depth = 1
                continue
            if not name and char in {";", " ", "\t", "\n"}:
                continue
            if char == ";" and name:
                name = []
                continue
            name.append(char)
            continue

        if char == "(":
            depth += 1
            current.append(char)
            continue
        if char == ")":
            depth -= 1
            if depth == 0:
                entries.append(("".join(name).strip(), "".join(current).strip()))
                name = []
                current = []
                reading_name = True
                continue
            current.append(char)
            continue

        current.append(char)

    return entries


def split_top_level_delimited(raw: str, delimiter: str) -> list[str]:
    parts: list[str] = []
    current: list[str] = []
    depth_round = 0
    depth_square = 0
    in_quotes = False
    escaped = False

    for char in raw:
        if char == "\\" and in_quotes and not escaped:
            escaped = True
            current.append(char)
            continue

        if char == '"' and not escaped:
            in_quotes = not in_quotes
            current.append(char)
            continue

        escaped = False

        if not in_quotes:
            if char == "(":
                depth_round += 1
            elif char == ")":
                depth_round = max(0, depth_round - 1)
            elif char == "[":
                depth_square += 1
            elif char == "]":
                depth_square = max(0, depth_square - 1)
            elif char == delimiter and depth_round == 0 and depth_square == 0:
                part = "".join(current).strip()
                if part:
                    parts.append(part)
                current = []
                continue

        current.append(char)

    tail = "".join(current).strip()
    if tail:
        parts.append(tail)
    return parts


def parse_scalar(value: str) -> Any:
    lowered = value.lower()
    if lowered == "true":
        return True
    if lowered == "false":
        return False
    if re.fullmatch(r"-?\d+", value):
        return int(value)
    if re.fullmatch(r"-?\d+\.\d+", value):
        return float(value)
    return value.strip().strip('"')


def parse_key_value_text(raw: str, delimiter: str) -> dict[str, Any]:
    values: dict[str, Any] = {}
    for part in split_top_level_delimited(raw, ","):
        if delimiter not in part:
            continue
        key, value = part.split(delimiter, 1)
        values[key.strip()] = parse_scalar(value.strip())
    return values


def parse_counter_groups(raw: str) -> dict[str, dict[str, Any]]:
    groups: dict[str, dict[str, Any]] = {}
    for name, contents in split_group_entries(raw):
        groups[name] = parse_key_value_text(contents, "=")
    return groups


def extract_log_message(line: str) -> str:
    if "] [INFO]" in line:
        return line.split("] [INFO]", 1)[1].strip()
    return line.strip()


def extract_log_timestamp(line: str) -> str:
    match = re.match(r"^\[([0-9:-]+\s+[0-9:,]+)\]", line.strip())
    return match.group(1) if match else ""


def build_anchor_record(
    kind: str,
    raw_line: str,
    *,
    parse_confidence: str,
    source: str = "log",
    extras: dict[str, Any] | None = None,
    **fields: Any,
) -> dict[str, Any]:
    anchor = {
        "kind": kind,
        "raw_line": raw_line.strip(),
        "timestamp": extract_log_timestamp(raw_line),
        "source": source,
        "parse_confidence": parse_confidence,
        "extras": extras or {},
    }
    anchor.update(fields)
    return anchor


def parse_observation_line(line: str) -> dict[str, Any]:
    message = extract_log_message(line)
    markers = {
        "observation_window": DIAGNOSTICS_OBSERVATION_PREFIX,
        "environment": "); environment(settings=",
        "patch_state": ", patch_state=",
        "diagnostic_counters": "); diagnostic_counters(",
        "diagnostic_context": "); diagnostic_context(topFactors=[",
    }

    start = message.index(markers["observation_window"]) + len(markers["observation_window"])
    environment_index = message.index(markers["environment"])
    observation_window_raw = message[start:environment_index]

    patch_index = message.index(markers["patch_state"], environment_index)
    settings_raw = message[environment_index + len(markers["environment"]) : patch_index]

    counters_index = message.index(markers["diagnostic_counters"], patch_index)
    patch_state = message[patch_index + len(markers["patch_state"]) : counters_index]

    context_index = message.index(markers["diagnostic_context"], counters_index)
    diagnostic_counters_raw = message[
        counters_index + len(markers["diagnostic_counters"]) : context_index
    ]
    context_raw = message[context_index + len(markers["diagnostic_context"]) : -2]

    observation_window = parse_key_value_text(observation_window_raw, "=")
    settings = parse_key_value_text(settings_raw, ":")
    diagnostic_counters = parse_counter_groups(diagnostic_counters_raw)
    top_factors = [part.strip() for part in context_raw.split(",") if part.strip()]

    return build_anchor_record(
        "observation",
        line,
        parse_confidence="high",
        message=message,
        observation_window_raw=observation_window_raw,
        observation_window=observation_window,
        settings_raw=settings_raw,
        settings=settings,
        patch_state=patch_state.strip(),
        diagnostic_counters_raw=diagnostic_counters_raw,
        diagnostic_counters=diagnostic_counters,
        diagnostic_context_raw=context_raw,
        top_factors=top_factors,
        session_id=str(observation_window.get("session_id", "")),
        run_id=safe_int(observation_window.get("run_id")),
        start_day=safe_int(observation_window.get("start_day")),
        end_day=safe_int(observation_window.get("end_day")),
        start_sample_index=safe_int(observation_window.get("start_sample_index")),
        end_sample_index=safe_int(observation_window.get("end_sample_index")),
        sample_day=safe_int(observation_window.get("sample_day")),
        sample_index=safe_int(observation_window.get("sample_index")),
        sample_slot=safe_int(observation_window.get("sample_slot")),
        samples_per_day=safe_int(observation_window.get("samples_per_day")),
        sample_count=safe_int(observation_window.get("sample_count")),
        observation_kind=str(observation_window.get("observation_kind", "")),
        skipped_sample_slots=safe_int(observation_window.get("skipped_sample_slots")),
        clock_source=str(observation_window.get("clock_source", "")),
        trigger=str(observation_window.get("trigger", "")),
        extras={
            "diagnostic_counter_groups": list(diagnostic_counters.keys()),
        },
    )


def parse_detail_line(line: str) -> dict[str, Any]:
    message = extract_log_message(line)
    start = message.index(DIAGNOSTICS_DETAIL_PREFIX) + len(DIAGNOSTICS_DETAIL_PREFIX)
    values_marker = ", values="
    values_index = message.index(values_marker)
    metadata_raw = message[start:values_index]
    values = message[values_index + len(values_marker) : -1]
    metadata = parse_key_value_text(metadata_raw, "=")
    role = ""
    if "role=consumer" in values:
        role = "consumer"
    elif "role=producer" in values:
        role = "producer"

    return build_anchor_record(
        "detail",
        line,
        parse_confidence="high",
        message=message,
        metadata_raw=metadata_raw,
        metadata=metadata,
        values=values,
        detail_type=str(metadata.get("detail_type", "")),
        session_id=str(metadata.get("session_id", "")),
        run_id=safe_int(metadata.get("run_id")),
        observation_end_day=safe_int(metadata.get("observation_end_day")),
        observation_end_sample_index=safe_int(metadata.get("observation_end_sample_index")),
        role=role,
    )


def observation_identity(observation: dict[str, Any] | None) -> tuple[str, str]:
    observation_window = (observation or {}).get("observation_window", {})
    return (
        str(observation_window.get("session_id", "")),
        str(observation_window.get("run_id", "")),
    )


def observation_sample_index(observation: dict[str, Any] | None) -> int:
    return safe_int((observation or {}).get("observation_window", {}).get("sample_index"))


def observation_day(observation: dict[str, Any] | None) -> int:
    observation_window = (observation or {}).get("observation_window", {})
    return safe_int(observation_window.get("sample_day") or observation_window.get("end_day"))


def detail_sample_index(detail: dict[str, Any] | None) -> int:
    return safe_int((detail or {}).get("metadata", {}).get("observation_end_sample_index"))


def detail_day(detail: dict[str, Any] | None) -> int:
    return safe_int((detail or {}).get("metadata", {}).get("observation_end_day"))


def detail_role(detail: dict[str, Any] | None) -> str:
    values = str((detail or {}).get("values", ""))
    if "role=consumer" in values:
        return "consumer"
    if "role=producer" in values:
        return "producer"
    return ""


def counter_value(observation: dict[str, Any] | None, group_name: str, field_name: str) -> int:
    counter_groups = (observation or {}).get("diagnostic_counters", {})
    return safe_int(counter_groups.get(group_name, {}).get(field_name))


def format_counter_group(name: str, values: dict[str, Any]) -> str:
    joined = ", ".join(f"{key}={value}" for key, value in values.items())
    return f"{name}({joined})"


def format_relevant_counter_group(
    name: str,
    values: dict[str, Any],
    ordered_fields: tuple[str, ...],
    *,
    include_zero_fields: tuple[str, ...] = (),
) -> str:
    include_zero = set(include_zero_fields)
    filtered: dict[str, int] = {}
    for field_name in ordered_fields:
        if field_name not in values:
            continue
        numeric_value = safe_int(values.get(field_name))
        if numeric_value > 0 or field_name in include_zero:
            filtered[field_name] = numeric_value
    if not filtered:
        return ""
    return format_counter_group(name, filtered)


def build_recent_detail_batches(
    details: list[dict[str, Any]],
    *,
    role: str,
    sample_limit: int = MAX_EXCERPT_CANDIDATES_PER_GROUP,
    line_limit: int = MAX_EXCERPT_DETAIL_LINES,
) -> list[list[dict[str, Any]]]:
    role_details = [detail for detail in details if detail_role(detail) == role]
    if not role_details:
        return []

    ordered_groups: list[list[dict[str, Any]]] = []
    seen_samples: set[tuple[int, int]] = set()
    ordered_details = sorted(
        role_details,
        key=lambda item: (detail_day(item), detail_sample_index(item)),
        reverse=True,
    )
    for detail in ordered_details:
        sample_key = (detail_day(detail), detail_sample_index(detail))
        if sample_key in seen_samples:
            continue
        seen_samples.add(sample_key)
        batch = [
            item
            for item in role_details
            if detail_day(item) == sample_key[0] and detail_sample_index(item) == sample_key[1]
        ]
        ordered_groups.append(batch[:line_limit])
        if len(ordered_groups) >= sample_limit:
            break
    return ordered_groups


def find_observation_for_detail(
    observations: list[dict[str, Any]],
    detail: dict[str, Any] | None,
) -> dict[str, Any] | None:
    if not observations or not detail:
        return None

    target_sample_index = detail_sample_index(detail)
    target_day = detail_day(detail)
    for observation in reversed(observations):
        if observation_sample_index(observation) == target_sample_index:
            return observation
    for observation in reversed(observations):
        if observation_day(observation) == target_day:
            return observation
    return observations[-1]


def excerpt_recency_label(index: int) -> str:
    if index <= 0:
        return "latest"
    if index == 1:
        return "previous"
    return f"recent_{index + 1}"


def build_excerpt_candidate(
    label: str,
    observation: dict[str, Any] | None,
    details: list[dict[str, Any]],
) -> dict[str, Any] | None:
    if not observation or not details:
        return None

    detail_kind = detail_role(details[0])
    if detail_kind == "consumer":
        section_title = f"Day {observation_day(observation)} consumer-side detail"
    elif detail_kind == "producer":
        section_title = f"Day {observation_day(observation)} producer-side detail"
    else:
        section_title = f"Day {observation_day(observation)} software detail"

    lines = [truncate_text(detail.get("values", ""), LLM_DETAIL_EXCERPT_LIMIT) for detail in details]
    return {
        "label": label,
        "day": observation_day(observation),
        "sample_index": observation_sample_index(observation),
        "title": section_title,
        "observation_window": observation.get("observation_window_raw", ""),
        "diagnostic_counters": observation.get("diagnostic_counters_raw", ""),
        "lines": lines,
        "markdown": "\n".join(
            [
                f"### {section_title}",
                "```text",
                "\n".join(lines),
                "```",
            ]
        ),
    }


def build_latest_run_candidates(
    latest_observation: dict[str, Any] | None,
    observations: list[dict[str, Any]],
    software_office_details: list[dict[str, Any]],
) -> dict[str, Any]:
    if not latest_observation:
        return {
            "latest_run_observations": [],
            "latest_run_details": [],
            "final_observation": None,
            "latest_consumer_detail_observation": None,
            "latest_producer_detail_observation": None,
            "consumer_peak_observation": None,
            "producer_peak_observation": None,
            "log_excerpt_candidates": [],
        }

    latest_session_id, latest_run_id = observation_identity(latest_observation)
    latest_run_observations = [
        observation
        for observation in observations
        if observation_identity(observation) == (latest_session_id, latest_run_id)
    ]
    latest_run_details = [
        detail
        for detail in software_office_details
        if str(detail.get("metadata", {}).get("session_id", "")) == latest_session_id
        and str(detail.get("metadata", {}).get("run_id", "")) == latest_run_id
    ]

    consumer_batches = build_recent_detail_batches(latest_run_details, role="consumer")
    producer_batches = build_recent_detail_batches(latest_run_details, role="producer")

    latest_consumer_detail_observation = find_observation_for_detail(
        latest_run_observations,
        consumer_batches[0][0] if consumer_batches else None,
    )
    latest_producer_detail_observation = find_observation_for_detail(
        latest_run_observations,
        producer_batches[0][0] if producer_batches else None,
    )

    log_excerpt_candidates: list[dict[str, Any]] = []
    for index, batch in enumerate(reversed(consumer_batches)):
        observation = find_observation_for_detail(latest_run_observations, batch[0] if batch else None)
        consumer_candidate = build_excerpt_candidate(
            f"consumer_{excerpt_recency_label(len(consumer_batches) - index - 1)}",
            observation,
            batch,
        )
        if consumer_candidate:
            log_excerpt_candidates.append(consumer_candidate)

    for index, batch in enumerate(reversed(producer_batches)):
        observation = find_observation_for_detail(latest_run_observations, batch[0] if batch else None)
        producer_candidate = build_excerpt_candidate(
            f"producer_{excerpt_recency_label(len(producer_batches) - index - 1)}",
            observation,
            batch,
        )
        if producer_candidate:
            log_excerpt_candidates.append(producer_candidate)

    log_excerpt_candidates.sort(
        key=lambda item: (safe_int(item["day"]), safe_int(item["sample_index"]), item["label"])
    )
    return {
        "latest_run_observations": latest_run_observations,
        "latest_run_details": latest_run_details,
        "final_observation": latest_observation,
        "latest_consumer_detail_observation": latest_consumer_detail_observation,
        "latest_producer_detail_observation": latest_producer_detail_observation,
        "consumer_peak_observation": latest_consumer_detail_observation,
        "producer_peak_observation": latest_producer_detail_observation,
        "log_excerpt_candidates": log_excerpt_candidates,
    }


def build_anchor_index(anchors: list[dict[str, Any]]) -> dict[str, int]:
    index: dict[str, int] = {}
    for anchor in anchors:
        kind = str(anchor.get("kind", ""))
        index[kind] = index.get(kind, 0) + 1
    return index


def excerpt_candidate_role(candidate: dict[str, Any]) -> str:
    label = str(candidate.get("label", ""))
    if label.startswith("consumer_"):
        return "consumer"
    if label.startswith("producer_"):
        return "producer"

    title = str(candidate.get("title", "")).lower()
    if "consumer" in title:
        return "consumer"
    if "producer" in title:
        return "producer"

    lines = "\n".join(str(line) for line in candidate.get("lines", []))
    if "role=consumer" in lines:
        return "consumer"
    if "role=producer" in lines:
        return "producer"
    return ""


def select_preferred_excerpt_candidates(
    candidates: list[dict[str, Any]],
    limit: int,
) -> list[dict[str, Any]]:
    if limit <= 0 or not candidates:
        return []

    grouped = {
        "consumer": [candidate for candidate in candidates if excerpt_candidate_role(candidate) == "consumer"],
        "producer": [candidate for candidate in candidates if excerpt_candidate_role(candidate) == "producer"],
    }

    selected: list[dict[str, Any]] = []
    selected_ids: set[int] = set()

    def append_candidate(candidate: dict[str, Any]) -> None:
        candidate_id = id(candidate)
        if candidate_id in selected_ids or len(selected) >= limit:
            return
        selected.append(candidate)
        selected_ids.add(candidate_id)

    rank = 0
    while len(selected) < limit:
        added = False
        for role in ("consumer", "producer"):
            role_candidates = grouped[role]
            if rank < len(role_candidates):
                append_candidate(role_candidates[-(rank + 1)])
                added = True
        if not added:
            break
        rank += 1

    for candidate in reversed(candidates):
        append_candidate(candidate)
        if len(selected) >= limit:
            break

    return selected


def build_selected_snippets(
    log_excerpt_candidates: list[dict[str, Any]],
    patch_summaries: list[str],
    phantom_corrections: list[str],
) -> list[dict[str, Any]]:
    snippets: list[dict[str, Any]] = []

    preferred_candidates = select_preferred_excerpt_candidates(
        log_excerpt_candidates,
        MAX_SELECTED_EXCERPT_CANDIDATES,
    )
    for candidate in preferred_candidates:
        snippets.append(
            {
                "label": candidate.get("label", ""),
                "kind": "detail_excerpt",
                "title": candidate.get("title", ""),
                "day": safe_int(candidate.get("day")),
                "sample_index": safe_int(candidate.get("sample_index")),
                "text": "\n".join(candidate.get("lines", [])),
            }
        )

    for patch_summary in patch_summaries[-LLM_MAX_PATCH_SUMMARIES:]:
        snippets.append(
            {
                "label": "patch_summary",
                "kind": "patch_summary",
                "title": "Patch summary",
                "day": 0,
                "sample_index": 0,
                "text": patch_summary,
            }
        )

    for correction in phantom_corrections[-LLM_MAX_PHANTOM_CORRECTIONS:]:
        snippets.append(
            {
                "label": "phantom_correction",
                "kind": "phantom_correction",
                "title": "Phantom correction",
                "day": 0,
                "sample_index": 0,
                "text": correction,
            }
        )

    return snippets


def parse_log(log_text: str) -> dict[str, Any]:
    observations: list[dict[str, Any]] = []
    software_office_details: list[dict[str, Any]] = []
    patch_summaries: list[str] = []
    phantom_corrections: list[str] = []
    anchors: list[dict[str, Any]] = []

    for line in log_text.replace("\r\n", "\n").split("\n"):
        stripped = line.strip()
        if not stripped:
            continue

        message = extract_log_message(stripped)

        if message.startswith(DIAGNOSTICS_OBSERVATION_PREFIX):
            try:
                observation = parse_observation_line(stripped)
            except Exception:
                observation = build_anchor_record(
                    "observation",
                    stripped,
                    parse_confidence="low",
                    message=extract_log_message(stripped),
                )
            observations.append(observation)
            anchors.append(observation)
            continue

        if message.startswith(DIAGNOSTICS_DETAIL_PREFIX) and SOFTWARE_OFFICE_STATES_DETAIL_MARKER in message:
            try:
                detail = parse_detail_line(stripped)
            except Exception:
                detail = build_anchor_record(
                    "detail",
                    stripped,
                    parse_confidence="low",
                    message=extract_log_message(stripped),
                )
            software_office_details.append(detail)
            anchors.append(detail)
            continue

        if any(message.startswith(prefix) for prefix in PATCH_SUMMARY_PREFIXES):
            patch_message = message
            patch_summaries.append(patch_message)
            anchors.append(
                build_anchor_record(
                    "patch_summary",
                    stripped,
                    parse_confidence="medium",
                    message=patch_message,
                )
            )
            continue

        if message.startswith(PHANTOM_CORRECTION_PREFIX):
            correction = message
            phantom_corrections.append(correction)
            anchors.append(
                build_anchor_record(
                    "phantom_correction",
                    stripped,
                    parse_confidence="medium",
                    message=correction,
                )
            )

    latest_observation = observations[-1] if observations else None
    latest_run_candidates = build_latest_run_candidates(
        latest_observation,
        observations,
        software_office_details,
    )
    latest_run_details = latest_run_candidates["latest_run_details"]
    latest_detail = None
    if latest_observation and software_office_details:
        latest_run_id = latest_observation["observation_window"].get("run_id")
        latest_session_id = latest_observation["observation_window"].get("session_id")
        latest_sample_index = latest_observation["observation_window"].get("sample_index")
        matching_details = [
            detail
            for detail in software_office_details
            if detail["metadata"].get("run_id") == latest_run_id
            and detail["metadata"].get("session_id") == latest_session_id
            and detail["metadata"].get("observation_end_sample_index") == latest_sample_index
        ]
        latest_detail = matching_details[-1] if matching_details else (latest_run_details[-1] if latest_run_details else None)

    return {
        "anchors": anchors,
        "anchor_index": build_anchor_index(anchors),
        "latest_observation": latest_observation,
        "latest_software_office_detail": latest_detail,
        "latest_patch_summary": patch_summaries[-1] if patch_summaries else "",
        "patch_summaries": patch_summaries[-5:],
        "phantom_corrections": phantom_corrections[-5:],
        "observation_count": len(observations),
        "detail_count": len(software_office_details),
        "latest_run_observations": latest_run_candidates["latest_run_observations"],
        "latest_run_details": latest_run_details,
        "final_observation": latest_run_candidates["final_observation"],
        "latest_consumer_detail_observation": latest_run_candidates["latest_consumer_detail_observation"],
        "latest_producer_detail_observation": latest_run_candidates["latest_producer_detail_observation"],
        "consumer_peak_observation": latest_run_candidates["consumer_peak_observation"],
        "producer_peak_observation": latest_run_candidates["producer_peak_observation"],
        "log_excerpt_candidates": latest_run_candidates["log_excerpt_candidates"],
        "selected_snippets": build_selected_snippets(
            latest_run_candidates["log_excerpt_candidates"],
            patch_summaries,
            phantom_corrections,
        ),
        "fallback_hints": {},
    }


def safe_int(value: Any) -> int:
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return int(value)
    if isinstance(value, str) and re.fullmatch(r"-?\d+", value.strip()):
        return int(value.strip())
    return 0


def derive_symptom_classification(counter_groups: dict[str, dict[str, Any]]) -> str:
    _ = counter_groups
    return "software_track_unclear"


def derive_fallback_symptom_classification(parsed_log: dict[str, Any]) -> str:
    _ = parsed_log
    return "software_track_unclear"


def build_deterministic_title(
    issue_fields: dict[str, str],
    parsed_log: dict[str, Any],
    derived_classification: str,
) -> str:
    final_observation = parsed_log.get("final_observation") or parsed_log.get("latest_observation") or {}
    final_day = observation_day(final_observation)
    settings = final_observation.get("settings", {})
    trade_patch = settings.get("EnableTradePatch")
    _ = derived_classification

    scenario_label = issue_fields.get("save_or_city_label", "").strip()
    if scenario_label and final_day > 0:
        return f"[Software Evidence] {scenario_label} evidence by day {final_day}"
    if scenario_label:
        return f"[Software Evidence] {scenario_label}"
    if final_day > 0 and trade_patch is True:
        return f"[Software Evidence] EnableTradePatch-enabled diagnostics by day {final_day}"
    if final_day > 0 and trade_patch is False:
        return f"[Software Evidence] EnableTradePatch-disabled diagnostics by day {final_day}"
    if final_day > 0:
        return f"[Software Evidence] raw-log diagnostics by day {final_day}"
    return "[Software Evidence] from raw log intake"


def truncate_text(value: str, limit: int = DETAIL_PREVIEW_LIMIT) -> str:
    if len(value) <= limit:
        return value
    return value[: limit - 3].rstrip() + "..."


def join_unique_lines(*values: str) -> str:
    ordered: list[str] = []
    seen: set[str] = set()
    for value in values:
        for line in value.replace("\r\n", "\n").split("\n"):
            cleaned = line.strip()
            if not cleaned:
                continue
            lowered = cleaned.lower()
            if lowered in seen:
                continue
            seen.add(lowered)
            ordered.append(cleaned)
    return "\n".join(ordered)


def markdown_bullets(*lines: str) -> str:
    cleaned_lines = [line.strip() for line in lines if line and line.strip()]
    return "\n".join(f"- {line}" for line in cleaned_lines)


def normalize_confounders_text(value: str) -> str:
    items: list[str] = []
    for raw_line in value.replace("\r\n", "\n").split("\n"):
        cleaned = raw_line.strip()
        if not cleaned:
            continue
        if cleaned.startswith("- "):
            cleaned = cleaned[2:].strip()
        parts = [part.strip() for part in cleaned.split(";")] if ";" in cleaned else [cleaned]
        for part in parts:
            if part:
                items.append(part)
    return markdown_bullets(*join_unique_lines("\n".join(items)).split("\n")) if items else ""


def choose_confounders_value(
    override_value: str,
    deterministic_value: str,
    llm_value: str,
) -> str:
    override = normalize_confounders_text(override_value)
    if override:
        return override

    deterministic = normalize_confounders_text(deterministic_value)
    if deterministic:
        return deterministic

    return normalize_confounders_text(llm_value)


def compact_office_snapshot(observation: dict[str, Any] | None) -> str:
    if not observation:
        return ""

    counter_groups = observation.get("diagnostic_counters", {})
    lines: list[str] = []
    office_demand = counter_groups.get("officeDemand", {})
    office_demand_snapshot = format_relevant_counter_group(
        "officeDemand",
        office_demand,
        ("building", "company", "emptyBuildings", "buildingDemand"),
    )
    if office_demand_snapshot:
        lines.append(office_demand_snapshot)

    producer_snapshot = format_relevant_counter_group(
        "softwareProducerOffices",
        counter_groups.get("softwareProducerOffices", {}),
        ("propertyless", "efficiencyZero", "lackResourcesZero"),
    )
    if producer_snapshot:
        lines.append(producer_snapshot)

    consumer_snapshot = format_relevant_counter_group(
        "softwareConsumerOffices",
        counter_groups.get("softwareConsumerOffices", {}),
        ("propertyless", "efficiencyZero", "lackResourcesZero", "softwareInputZero"),
    )
    if consumer_snapshot:
        lines.append(consumer_snapshot)

    buyer_state = counter_groups.get("softwareConsumerBuyerState", {})
    if any(safe_int(value) > 0 for value in buyer_state.values()):
        buyer_state_snapshot = format_relevant_counter_group(
            "softwareConsumerBuyerState",
            buyer_state,
            ("needSelected", "noBuyerDespiteNeed", "tradeCostOnly", "buyerActive"),
            include_zero_fields=("buyerActive",),
        )
        if buyer_state_snapshot:
            lines.append(buyer_state_snapshot)
    return ", ".join(lines)


def build_deterministic_log_excerpt(parsed_log: dict[str, Any]) -> str:
    preferred_candidates = select_preferred_excerpt_candidates(
        list(parsed_log.get("log_excerpt_candidates", [])),
        MAX_DETERMINISTIC_EXCERPT_CANDIDATES,
    )
    blocks = [candidate["markdown"] for candidate in preferred_candidates if candidate.get("markdown")]
    return "\n\n".join(blocks)


def build_deterministic_notes(parsed_log: dict[str, Any]) -> str:
    latest_run_observations = parsed_log.get("latest_run_observations", [])
    latest_consumer_detail_observation = (
        parsed_log.get("latest_consumer_detail_observation") or parsed_log.get("consumer_peak_observation")
    )
    latest_producer_detail_observation = (
        parsed_log.get("latest_producer_detail_observation") or parsed_log.get("producer_peak_observation")
    )
    final_observation = parsed_log.get("final_observation")
    log_excerpt_candidates = parsed_log.get("log_excerpt_candidates", [])

    note_lines: list[str] = []
    if latest_run_observations:
        first_observation = latest_run_observations[0]
        start_snapshot = compact_office_snapshot(first_observation)
        if start_snapshot:
            note_lines.append(f"Day {observation_day(first_observation)} started with `{start_snapshot}`.")

    if latest_consumer_detail_observation:
        consumer_snapshot = compact_office_snapshot(latest_consumer_detail_observation)
        if consumer_snapshot:
            note_lines.append(
                "Latest consumer detail anchor came from day "
                f"{observation_day(latest_consumer_detail_observation)} with `{consumer_snapshot}`."
            )

    if latest_producer_detail_observation:
        producer_snapshot = compact_office_snapshot(latest_producer_detail_observation)
        if producer_snapshot:
            note_lines.append(
                "Latest producer detail anchor came from day "
                f"{observation_day(latest_producer_detail_observation)} with `{producer_snapshot}`."
            )

    if final_observation:
        final_snapshot = compact_office_snapshot(final_observation)
        if final_snapshot:
            note_lines.append(
                f"Day {observation_day(final_observation)} ended with `{final_snapshot}`."
            )

    if any("tradeCostEntry=True" in "\n".join(candidate.get("lines", [])) for candidate in log_excerpt_candidates):
        note_lines.append(
            "Sampled trade-state detail still showed `tradeCostEntry=True` on affected office lines."
        )

    office_demand = (final_observation or {}).get("diagnostic_counters", {}).get("officeDemand", {})
    if "buildingDemand" in office_demand:
        note_lines.append(
            "`officeDemand.buildingDemand` is preserved as a factor counter, not a claim that final office demand was zero."
        )

    return markdown_bullets(*note_lines)


def build_deterministic_summary(
    parsed_log: dict[str, Any],
    derived_classification: str,
) -> str:
    _ = derived_classification
    latest_run_observations = parsed_log.get("latest_run_observations", [])
    final_observation = parsed_log.get("final_observation") or parsed_log.get("latest_observation")
    summary_sentences: list[str] = []
    if latest_run_observations:
        first_observation = latest_run_observations[0]
        last_observation = latest_run_observations[-1]
        start_day = observation_day(first_observation)
        end_day = observation_day(last_observation)
        if start_day > 0 and end_day > 0:
            summary_sentences.append(
                f"This bounded run captured {len(latest_run_observations)} observation windows from day {start_day} to day {end_day}."
            )

    if final_observation:
        final_day = observation_day(final_observation)
        final_snapshot = compact_office_snapshot(final_observation)
        if final_snapshot:
            if final_day > 0:
                summary_sentences.append(f"The latest day-{final_day} observation kept `{final_snapshot}`.")
            else:
                summary_sentences.append(f"The latest observation kept `{final_snapshot}`.")

    if not summary_sentences:
        latest_observation = parsed_log.get("latest_observation") or {}
        summary_sentences.append(
            f"Latest observation window: {latest_observation.get('observation_window_raw', 'not available')}."
        )

    return " ".join(summary_sentences)


def summarize_observation_for_llm(observation: dict[str, Any] | None) -> dict[str, Any]:
    if not observation:
        return {}
    return {
        "day": observation_day(observation),
        "sample_index": observation_sample_index(observation),
        "observation_window": truncate_text(observation.get("observation_window_raw", ""), LLM_SUMMARY_LINE_LIMIT),
        "diagnostic_counters": truncate_text(
            observation.get("diagnostic_counters_raw", ""),
            DETAIL_PREVIEW_LIMIT,
        ),
    }


def build_anchor_summaries_for_llm(parsed_log: dict[str, Any]) -> list[dict[str, Any]]:
    summaries: list[dict[str, Any]] = []
    for anchor in parsed_log.get("anchors", []):
        kind = str(anchor.get("kind", ""))
        summary: dict[str, Any] = {
            "kind": kind,
            "timestamp": str(anchor.get("timestamp", "")),
            "parse_confidence": str(anchor.get("parse_confidence", "")),
            "raw_excerpt": truncate_text(str(anchor.get("raw_line", "")), LLM_SUMMARY_LINE_LIMIT),
        }
        if kind == "observation":
            summary.update(
                {
                    "session_id": str(anchor.get("session_id", "")),
                    "run_id": safe_int(anchor.get("run_id")),
                    "sample_day": safe_int(anchor.get("sample_day")),
                    "sample_index": safe_int(anchor.get("sample_index")),
                    "clock_source": str(anchor.get("clock_source", "")),
                    "trigger": str(anchor.get("trigger", "")),
                    "settings": truncate_text(str(anchor.get("settings_raw", "")), LLM_SUMMARY_LINE_LIMIT),
                    "patch_state": str(anchor.get("patch_state", "")),
                    "diagnostic_counters": truncate_text(
                        str(anchor.get("diagnostic_counters_raw", "")),
                        DETAIL_PREVIEW_LIMIT,
                    ),
                }
            )
        elif kind == "detail":
            summary.update(
                {
                    "session_id": str(anchor.get("session_id", "")),
                    "run_id": safe_int(anchor.get("run_id")),
                    "observation_end_day": safe_int(anchor.get("observation_end_day")),
                    "observation_end_sample_index": safe_int(anchor.get("observation_end_sample_index")),
                    "detail_type": str(anchor.get("detail_type", "")),
                    "role": str(anchor.get("role", "")),
                    "values": truncate_text(str(anchor.get("values", "")), DETAIL_PREVIEW_LIMIT),
                }
            )
        elif kind in {"patch_summary", "phantom_correction"}:
            summary["message"] = truncate_text(str(anchor.get("message", "")), LLM_SUMMARY_LINE_LIMIT)
        summaries.append(summary)
    return summaries[-12:]


def build_selected_snippets_for_llm(parsed_log: dict[str, Any]) -> list[dict[str, Any]]:
    snippets: list[dict[str, Any]] = []
    for snippet in parsed_log.get("selected_snippets", []):
        snippets.append(
            {
                "label": snippet.get("label", ""),
                "kind": snippet.get("kind", ""),
                "title": snippet.get("title", ""),
                "day": safe_int(snippet.get("day")),
                "sample_index": safe_int(snippet.get("sample_index")),
                "text": truncate_text(str(snippet.get("text", "")), DETAIL_PREVIEW_LIMIT),
            }
        )
    return snippets


def build_excerpt_candidates_for_llm(parsed_log: dict[str, Any]) -> list[dict[str, Any]]:
    candidates: list[dict[str, Any]] = []
    for candidate in parsed_log.get("log_excerpt_candidates", []):
        candidates.append(
            {
                "label": candidate.get("label", ""),
                "title": candidate.get("title", ""),
                "day": candidate.get("day", 0),
                "sample_index": candidate.get("sample_index", 0),
                "observation_window": truncate_text(candidate.get("observation_window", ""), LLM_SUMMARY_LINE_LIMIT),
                "lines": [truncate_text(line, LLM_DETAIL_EXCERPT_LIMIT) for line in candidate.get("lines", [])],
            }
        )
    return candidates


def build_deterministic_confounders(
    issue_fields: dict[str, str],
    latest_observation: dict[str, Any],
    parsed_log: dict[str, Any],
) -> str:
    settings = latest_observation.get("settings", {})
    confounder_lines: list[str] = []

    other_mods = issue_fields.get("other_mods", "").strip()
    if other_mods:
        confounder_lines.append(f"other mods reported: {other_mods}")

    patch_state = str(latest_observation.get("patch_state", "")).strip()
    if patch_state and patch_state != "release-build":
        confounder_lines.append(f"patch_state={patch_state}")

    if "EnableTradePatch" in settings:
        if settings.get("EnableTradePatch"):
            confounder_lines.append("trade patch enabled during capture")
        else:
            confounder_lines.append("trade patch disabled during capture")

    if not settings.get("CaptureStableEvidence"):
        confounder_lines.append("no stable baseline capture in this raw intake")

    clock_source = str((latest_observation or {}).get("observation_window", {}).get("clock_source", "")).strip()
    if clock_source and clock_source not in {"displayed_clock", "runtime_time_system"}:
        confounder_lines.append(f"clock_source={clock_source}")

    if safe_int(parsed_log.get("observation_count", 0)) <= 1:
        confounder_lines.append("single observation window in raw intake")

    if not supports_explicit_comparison(issue_fields):
        confounder_lines.append("no explicit comparison baseline in raw intake")

    if not confounder_lines:
        return "none known from raw log intake alone"

    return "\n".join(f"- {line}" for line in confounder_lines)


def build_deterministic_draft(
    issue_number: int,
    issue_fields: dict[str, str],
    parsed_log: dict[str, Any],
    log_source: dict[str, Any],
    redaction_notes: list[str],
) -> dict[str, Any]:
    latest_observation = parsed_log.get("latest_observation")
    if not latest_observation:
        return {
            "title": build_deterministic_title(issue_fields, parsed_log, "software_track_unclear"),
            "scenario_label": issue_fields.get("save_or_city_label", ""),
            "scenario_type": "existing save" if issue_fields.get("save_or_city_label") else "",
            "platform_notes": issue_fields.get("platform_notes", ""),
            "comparison_baseline": "",
            "symptom_classification": "",
            "custom_symptom_classification": "",
            "evidence_summary": "",
            "log_excerpt": "",
            "analysis_basis": "",
            "confounders": "",
            "notes": "",
            "missing_user_input": [
                "raw_log_with_softwareEvidenceDiagnostics",
                "scenario_label",
                "reproduction_conditions",
            ],
            "confidence": "medium",
            "reasoning_summary": "No current-branch softwareEvidenceDiagnostics observation window was found in the raw log.",
        }

    derived_classification = derive_fallback_symptom_classification(parsed_log)
    patch_summary = parsed_log.get("latest_patch_summary", "")
    confounders = build_deterministic_confounders(issue_fields, latest_observation, parsed_log)
    deterministic_excerpt = build_deterministic_log_excerpt(parsed_log)

    note_lines = [
        f"Source raw issue: #{issue_number}",
        f"Raw log source: {log_source['mode']}",
    ]
    if log_source.get("url"):
        note_lines.append(f"Raw log attachment: {sanitize_url(log_source['url'])}")
    if redaction_notes:
        note_lines.append("Redaction notes: " + "; ".join(redaction_notes))
    if patch_summary:
        note_lines.append("Latest patch summary: " + patch_summary)
    if parsed_log.get("phantom_corrections"):
        note_lines.append(
            "Recent phantom corrections: "
            + "; ".join(parsed_log.get("phantom_corrections", [])[-LLM_MAX_PHANTOM_CORRECTIONS:])
        )

    missing_user_input: list[str] = []
    if not issue_fields.get("save_or_city_label"):
        missing_user_input.append("scenario_label")
    if not issue_fields.get("what_happened"):
        missing_user_input.append("reproduction_conditions")

    deterministic_draft = {
        "title": build_deterministic_title(issue_fields, parsed_log, derived_classification),
        "scenario_label": issue_fields.get("save_or_city_label", ""),
        "scenario_type": "existing save" if issue_fields.get("save_or_city_label") else "",
        "platform_notes": issue_fields.get("platform_notes", ""),
        "comparison_baseline": "",
        "symptom_classification": derived_classification,
        "custom_symptom_classification": "",
        "evidence_summary": build_deterministic_summary(parsed_log, derived_classification),
        "log_excerpt": deterministic_excerpt,
        "analysis_basis": "",
        "confounders": confounders,
        "notes": join_unique_lines(build_deterministic_notes(parsed_log), markdown_bullets(*note_lines)),
        "missing_user_input": list(dict.fromkeys(missing_user_input)),
        "confidence": "medium",
        "reasoning_summary": (
            "Derived from observation/detail anchors and literal snippet selection, "
            "with conservative fallback wording."
        ),
    }

    parsed_log["fallback_hints"] = {
        "title": deterministic_draft["title"],
        "symptom_classification": deterministic_draft["symptom_classification"],
        "evidence_summary": deterministic_draft["evidence_summary"],
        "comparison_baseline": deterministic_draft["comparison_baseline"],
    }

    return deterministic_draft


def build_llm_context(
    issue_fields: dict[str, str],
    parsed_log: dict[str, Any],
    deterministic_draft: dict[str, Any],
    redaction_notes: list[str],
) -> dict[str, Any]:
    raw_issue_context = {
        "game_version": issue_fields.get("game_version", ""),
        "mod_version": issue_fields.get("mod_version", ""),
        "save_or_city_label": issue_fields.get("save_or_city_label", ""),
        "what_happened": truncate_text(issue_fields.get("what_happened", ""), 500),
        "platform_notes": truncate_text(issue_fields.get("platform_notes", ""), 300),
        "other_mods": truncate_text(issue_fields.get("other_mods", ""), 300),
    }

    semantic_facts = build_llm_semantic_facts(parsed_log)
    fallback_hints = {
        "title": deterministic_draft.get("title", ""),
        "symptom_classification": deterministic_draft.get("symptom_classification", ""),
        "evidence_summary": truncate_text(deterministic_draft.get("evidence_summary", ""), 500),
        "comparison_baseline": truncate_text(deterministic_draft.get("comparison_baseline", ""), 300),
        "notes": truncate_text(deterministic_draft.get("notes", ""), 700),
    }

    return {
        "raw_issue": raw_issue_context,
        "anchors": build_anchor_summaries_for_llm(parsed_log),
        "selected_snippets": build_selected_snippets_for_llm(parsed_log),
        "excerpt_candidates": build_excerpt_candidates_for_llm(parsed_log),
        "style_examples": EVIDENCE_STYLE_EXAMPLES[:MAX_STYLE_EXAMPLES],
        "fallback_hints": fallback_hints,
        "redaction_notes": redaction_notes,
        "semantic_facts": semantic_facts,
        "observation_count": safe_int(parsed_log.get("observation_count", 0)),
        "detail_count": safe_int(parsed_log.get("detail_count", 0)),
        "allowed_missing_user_input": deterministic_draft.get("missing_user_input", []),
    }


def build_summary_refinement_context(
    issue_fields: dict[str, str],
    parsed_log: dict[str, Any],
    deterministic_draft: dict[str, Any],
    draft: dict[str, Any],
) -> dict[str, Any]:
    excerpt_candidates = build_excerpt_candidates_for_llm(parsed_log)
    selected_candidates = select_preferred_excerpt_candidates(excerpt_candidates, 2)
    selected_candidate = selected_candidates[0] if selected_candidates else {}
    latest_consumer_detail_observation = (
        parsed_log.get("latest_consumer_detail_observation") or parsed_log.get("consumer_peak_observation")
    )
    return {
        "raw_issue": {
            "save_or_city_label": issue_fields.get("save_or_city_label", ""),
            "what_happened": truncate_text(issue_fields.get("what_happened", ""), 240),
            "other_mods": truncate_text(issue_fields.get("other_mods", ""), 120),
        },
        "draft_title": draft.get("title", ""),
        "current_summary": truncate_text(str(draft.get("evidence_summary", "")), 500),
        "fallback_summary": truncate_text(str(deterministic_draft.get("evidence_summary", "")), 500),
        "final_observation": summarize_observation_for_llm(
            parsed_log.get("final_observation") or parsed_log.get("latest_observation")
        ),
        "latest_consumer_detail_observation": summarize_observation_for_llm(latest_consumer_detail_observation),
        "selected_excerpt_candidate": {
            "label": selected_candidate.get("label", ""),
            "title": selected_candidate.get("title", ""),
            "day": safe_int(selected_candidate.get("day")),
            "sample_index": safe_int(selected_candidate.get("sample_index")),
            "lines": [truncate_text(str(line), 320) for line in selected_candidate.get("lines", [])[:2]],
        },
        "selected_excerpt_candidates": [
            {
                "label": candidate.get("label", ""),
                "title": candidate.get("title", ""),
                "day": safe_int(candidate.get("day")),
                "sample_index": safe_int(candidate.get("sample_index")),
                "lines": [truncate_text(str(line), 320) for line in candidate.get("lines", [])[:2]],
            }
            for candidate in selected_candidates
        ],
        "semantic_facts": build_llm_semantic_facts(parsed_log)[:6],
    }


def encode_llm_context(context: dict[str, Any]) -> str:
    return json.dumps(context, ensure_ascii=True, separators=(",", ":"))


def compact_anchor_summaries_for_llm(
    anchors: list[dict[str, Any]],
    anchor_limit: int,
    anchor_excerpt_limit: int,
    anchor_detail_limit: int,
) -> list[dict[str, Any]]:
    summaries: list[dict[str, Any]] = []
    for anchor in anchors[-anchor_limit:]:
        compact = dict(anchor)
        compact["raw_excerpt"] = truncate_text(str(compact.get("raw_excerpt", "")), anchor_excerpt_limit)
        for field_name in ("settings", "diagnostic_counters", "values"):
            if field_name in compact:
                compact[field_name] = truncate_text(str(compact.get(field_name, "")), anchor_detail_limit)
        if "message" in compact:
            compact["message"] = truncate_text(str(compact.get("message", "")), anchor_excerpt_limit)
        summaries.append(compact)
    return summaries


def compact_selected_snippets_for_llm(
    snippets: list[dict[str, Any]],
    snippet_limit: int,
    snippet_text_limit: int,
) -> list[dict[str, Any]]:
    compacted: list[dict[str, Any]] = []
    for snippet in snippets[:snippet_limit]:
        compact = dict(snippet)
        compact["text"] = truncate_text(str(compact.get("text", "")), snippet_text_limit)
        compacted.append(compact)
    return compacted


def compact_excerpt_candidates_for_llm(
    candidates: list[dict[str, Any]],
    candidate_limit: int,
    candidate_lines_per_item: int,
    candidate_line_limit: int,
) -> list[dict[str, Any]]:
    compacted: list[dict[str, Any]] = []
    if candidate_limit <= 0:
        return compacted
    for candidate in select_preferred_excerpt_candidates(candidates, candidate_limit):
        compact = dict(candidate)
        compact["observation_window"] = truncate_text(
            str(compact.get("observation_window", "")),
            candidate_line_limit,
        )
        compact["lines"] = [
            truncate_text(str(line), candidate_line_limit)
            for line in compact.get("lines", [])[:candidate_lines_per_item]
        ]
        compacted.append(compact)
    return compacted


def compact_llm_context(context: dict[str, Any], spec: dict[str, int | str]) -> dict[str, Any]:
    raw_issue = dict(context.get("raw_issue", {}))
    raw_issue["what_happened"] = truncate_text(
        str(raw_issue.get("what_happened", "")),
        int(spec["what_happened_limit"]),
    )
    raw_issue["platform_notes"] = truncate_text(
        str(raw_issue.get("platform_notes", "")),
        int(spec["platform_notes_limit"]),
    )
    raw_issue["other_mods"] = truncate_text(
        str(raw_issue.get("other_mods", "")),
        int(spec["other_mods_limit"]),
    )

    fallback_hints = dict(context.get("fallback_hints", {}))
    fallback_hints["evidence_summary"] = truncate_text(
        str(fallback_hints.get("evidence_summary", "")),
        int(spec["fallback_summary_limit"]),
    )
    fallback_hints["comparison_baseline"] = truncate_text(
        str(fallback_hints.get("comparison_baseline", "")),
        int(spec["platform_notes_limit"]),
    )
    fallback_hints["notes"] = truncate_text(
        str(fallback_hints.get("notes", "")),
        int(spec["fallback_notes_limit"]),
    )

    return {
        "raw_issue": raw_issue,
        "anchors": compact_anchor_summaries_for_llm(
            list(context.get("anchors", [])),
            int(spec["anchor_limit"]),
            int(spec["anchor_excerpt_limit"]),
            int(spec["anchor_detail_limit"]),
        ),
        "selected_snippets": compact_selected_snippets_for_llm(
            list(context.get("selected_snippets", [])),
            int(spec["snippet_limit"]),
            int(spec["snippet_text_limit"]),
        ),
        "excerpt_candidates": compact_excerpt_candidates_for_llm(
            list(context.get("excerpt_candidates", [])),
            int(spec["candidate_limit"]),
            int(spec["candidate_lines_per_item"]),
            int(spec["candidate_line_limit"]),
        ),
        "style_examples": list(context.get("style_examples", []))[: int(spec["style_example_limit"])],
        "fallback_hints": fallback_hints,
        "redaction_notes": [
            truncate_text(str(note), int(spec["redaction_note_limit"]))
            for note in list(context.get("redaction_notes", []))[:2]
        ],
        "semantic_facts": [
            truncate_text(str(fact), int(spec["semantic_fact_text_limit"]))
            for fact in list(context.get("semantic_facts", []))[: int(spec["semantic_fact_limit"])]
        ],
        "observation_count": safe_int(context.get("observation_count", 0)),
        "detail_count": safe_int(context.get("detail_count", 0)),
        "allowed_missing_user_input": list(context.get("allowed_missing_user_input", [])),
    }


def build_llm_context_variants(context: dict[str, Any]) -> list[dict[str, Any]]:
    variants: list[dict[str, Any]] = []
    seen: set[str] = set()
    for spec in LLM_CONTEXT_VARIANTS:
        variant = compact_llm_context(context, spec)
        serialized = encode_llm_context(variant)
        if serialized in seen:
            continue
        seen.add(serialized)
        variants.append(variant)
    return variants


def build_llm_semantic_facts(parsed_log: dict[str, Any]) -> list[str]:
    latest_observation = parsed_log.get("latest_observation") or {}
    counter_groups = latest_observation.get("diagnostic_counters", {})
    producers = counter_groups.get("softwareProducerOffices", {})
    consumers = counter_groups.get("softwareConsumerOffices", {})
    buyer_state = counter_groups.get("softwareConsumerBuyerState", {})
    facts = [
        "Treat fallback_hints as conservative defaults; anchors, excerpt candidates, and selected snippets are the primary evidence.",
        "softwareProducerOffices.lackResourcesZero and softwareConsumerOffices.lackResourcesZero count offices where the diagnostic field lackResources=0.",
        "lackResourcesZero does not mean the office had zero resources, no resources, or a confirmed input shortage.",
        "softwareConsumerOffices.softwareInputZero counts consumer offices where the software input state was zero in diagnostics; it is not a citywide demand verdict by itself.",
        "softwareConsumerBuyerState.noBuyerDespiteNeed counts consumer offices with selected software need but no active buyer in the latest observation.",
        "If a detail line shows softwareInputZero=False, do not summarize that office as a confirmed softwareInputZero case or a confirmed shortage; describe the buyer-state fields literally.",
        "If the facts show efficiency=0 and lackResources=0, summarize that conservatively as 'efficiency=0 while lackResources=0'.",
        "Do not use phrases like 'zero resources' or 'no resources' unless the provided facts literally support that wording.",
        "When selecting log excerpts, use only the provided candidate lines and preserve their wording inside fenced `text` blocks.",
        "Use `### Day ...` subsection headings inside `log_excerpt` when you include producer-side or consumer-side detail excerpts.",
    ]

    producer_lack_resources_zero = safe_int(producers.get("lackResourcesZero"))
    if producer_lack_resources_zero > 0:
        facts.append(
            f"Latest counters: softwareProducerOffices.lackResourcesZero={producer_lack_resources_zero} means {producer_lack_resources_zero} producer offices had lackResources=0 in the latest observation."
        )

    consumer_lack_resources_zero = safe_int(consumers.get("lackResourcesZero"))
    if consumer_lack_resources_zero > 0:
        facts.append(
            f"Latest counters: softwareConsumerOffices.lackResourcesZero={consumer_lack_resources_zero} means {consumer_lack_resources_zero} consumer offices had lackResources=0 in the latest observation."
        )

    software_input_zero = safe_int(consumers.get("softwareInputZero"))
    if software_input_zero > 0:
        facts.append(
            f"Latest counters: softwareConsumerOffices.softwareInputZero={software_input_zero} means {software_input_zero} consumer offices had softwareInputZero in the latest observation."
        )

    no_buyer_despite_need = safe_int(buyer_state.get("noBuyerDespiteNeed"))
    if no_buyer_despite_need > 0:
        facts.append(
            f"Latest counters: softwareConsumerBuyerState.noBuyerDespiteNeed={no_buyer_despite_need} and buyerActive={safe_int(buyer_state.get('buyerActive'))} describe selected software need with no active buyer in the latest observation."
        )

    office_demand_building = safe_int(counter_groups.get("officeDemand", {}).get("building"))
    if office_demand_building > 0:
        facts.append(
            f"Latest counters: officeDemand.building={office_demand_building} in the latest observation."
        )

    return facts


def build_llm_request_payload(context: dict[str, Any], model: str | None = None) -> dict[str, Any]:
    schema = {
        "type": "object",
        "additionalProperties": False,
        "properties": {
            "title": {"type": "string", "maxLength": LLM_TITLE_MAX_LENGTH},
            "symptom_classification": {"type": "string"},
            "custom_symptom_classification": {"type": "string"},
            "evidence_summary": {"type": "string", "maxLength": LLM_EVIDENCE_SUMMARY_MAX_LENGTH},
            "comparison_baseline": {"type": "string", "maxLength": LLM_COMPARISON_BASELINE_MAX_LENGTH},
            "confidence": {"type": "string"},
            "confounders": {"type": "string", "maxLength": LLM_CONFOUNDERS_MAX_LENGTH},
            "analysis_basis": {"type": "string", "maxLength": LLM_ANALYSIS_BASIS_MAX_LENGTH},
            "log_excerpt": {"type": "string", "maxLength": LLM_LOG_EXCERPT_MAX_LENGTH},
            "notes": {"type": "string", "maxLength": LLM_NOTES_MAX_LENGTH},
            "missing_user_input": {
                "type": "array",
                "items": {"type": "string"},
                "maxItems": 3,
            },
            "reasoning_summary": {"type": "string", "maxLength": LLM_REASONING_SUMMARY_MAX_LENGTH},
        },
        "required": [
            "title",
            "symptom_classification",
            "custom_symptom_classification",
            "evidence_summary",
            "comparison_baseline",
            "confidence",
            "confounders",
            "analysis_basis",
            "log_excerpt",
            "notes",
            "missing_user_input",
            "reasoning_summary",
        ],
    }

    instructions = textwrap.dedent(
        """
        You are drafting suggested fields for a GitHub evidence issue from a redacted game log.
        Follow these rules:
        - Use the provided anchors and raw snippets as the primary source of truth.
        - Do not invent numeric counters, comparison baselines, issue references, or quoted detail lines that are not present in the provided facts.
        - Leave fields empty rather than guessing when confidence is low.
        - Draft for a final `Software evidence` issue body that should read like issues #25 and #26 in this repo: factual, compact, easy to scan, and editorially strong.
        - `title` should be a concise issue title suitable for GitHub, ideally under 120 characters, and should read like a reusable evidence title rather than a raw intake label.
        - Do not simply repeat the raw issue title or scenario label as the evidence title.
        - `evidence_summary` must stay factual and observational. Keep it to 2-4 short sentences about what the provided diagnostics showed.
        - Do not mention the chosen symptom label, classification process, or why a label applies inside `evidence_summary`.
        - Do not use `evidence_summary` for causal claims, recommendations, or likely explanations.
        - Prefer literal counter language in `evidence_summary`, such as `officeDemand.building=...`, `softwareConsumerBuyerState.noBuyerDespiteNeed=...`, or `buyerActive=0`, when those counters are the main signal.
        - `comparison_baseline` should stay empty unless the provided facts explicitly support a save-lineage, issue-reference, or patch-state comparison.
        - `confidence` must be one of: high, medium, low. Use `medium` unless the facts strongly justify another choice.
        - Keep confounders short and checklist-like. Prefer 1-4 short lines about run conditions, other mods, patch state, missing baseline, or similar evidence limits.
        - Keep the total confounders text brief; do not pad it.
        - Only include confounders that could materially affect interpretation. Do not list routine diagnostics settings unless they materially changed behavior or evidence capture.
        - Do not repeat fallback hints unless they help and remain fully supported by the anchors/snippets.
        - `analysis_basis` may stay blank. Fill it only when the provided facts directly justify a brief factual note about code-reading basis.
        - `log_excerpt` should be Markdown, not prose-only text. Use 1-2 `### Day ...` subsections followed by fenced `text` blocks, and copy only the candidate excerpt lines that were provided.
        - Do not invent excerpt lines, fields, days, sample indices, or comparisons. If the candidates are weak, leave `log_excerpt` blank.
        - `notes` may add extra factual context or excerpt observations, but do not speculate about root cause, misconfiguration, ownership problems, or likely explanations.
        - Prefer bullet-list `notes` that walk through the run chronologically when the facts support that.
        - Keep `notes` compact: 2-5 short bullets, not a long paragraph.
        - Put label-selection rationale and interpretation only in `reasoning_summary`.
        - `lackResourcesZero` is a diagnostic counter name for offices where `lackResources=0`; it does not mean "zero resources" or "no resources."
        - `softwareInputZero` is an office-state/input condition for software consumers; do not generalize it into an overall citywide demand or shortage conclusion without explicit facts.
        - If a detail line shows `softwareInputZero=False`, do not summarize that office as a software-input-zero case; describe the observed buyer-state or trade-state fields literally instead.
        - If the facts show `efficiency=0` and `lackResources=0`, describe that conservatively as `efficiency=0 while lackResources=0` or equivalent.
        - Do not translate diagnostic counter names into stronger plain-language shortage claims unless the provided facts explicitly support that claim.
        - Avoid phrases like "zero resources" or "no resources" unless the provided facts literally support that wording.
        - Avoid unsupported interpretation in `evidence_summary` and `notes`.
        - Do not say "no indication of phantom vacancies" when the bounded window includes phantom corrections or non-zero `guardCorrections`.
        - Do not say phantom-vacancy counters or guard corrections stayed zero if the provided run facts include phantom corrections or non-zero `guardCorrections` anywhere in the bounded window.
        - Avoid subjective intensifiers like "significantly"; prefer direct counter changes instead.
        - When the run is mainly showing buyer-state pressure, describe the buyer-state fields literally rather than claiming the demand was resolved or unresolved.
        - Prefer `software_demand_mismatch` when software-track distress is present while office-demand counters stay high or rise in the provided window.
        - If you use a non-canonical symptom label, set `symptom_classification` to `other` and put the final label in `custom_symptom_classification`.
        - Use one of these labels when possible:
          software_office_propertyless, software_office_efficiency_zero,
          software_office_lack_resources_zero, software_demand_mismatch,
          software_track_unclear.
        - `reasoning_summary` must be 1-2 short sentences, complete, concise, and under 220 characters. Do not use ellipses.
        - `missing_user_input` should list only field ids from `allowed_missing_user_input`. If that list is empty, return an empty array.
        """
    ).strip()

    return {
        "model": model or get_primary_github_models_model(),
        "messages": [
            {
                "role": "system",
                "content": instructions,
            },
            {
                "role": "user",
                "content": encode_llm_context(context),
            },
        ],
        "response_format": {
            "type": "json_schema",
            "json_schema": {
                "name": "raw_log_triage_suggestions",
                "schema": schema,
                "strict": True,
            },
        },
    }


def build_summary_refinement_request_payload(context: dict[str, Any], model: str | None = None) -> dict[str, Any]:
    schema = {
        "type": "object",
        "additionalProperties": False,
        "properties": {
            "evidence_summary": {"type": "string", "maxLength": LLM_EVIDENCE_SUMMARY_MAX_LENGTH},
        },
        "required": ["evidence_summary"],
    }

    instructions = textwrap.dedent(
        """
        Rewrite only the `evidence_summary` field for a GitHub software evidence issue.
        Follow these rules:
        - Keep it to 2-4 short factual sentences and under 500 characters.
        - Use only the provided run facts.
        - Prefer literal counter language when counters are the key signal.
        - Do not mention labels, comparisons, recommendations, or root-cause theories.
        - Do not say phantom-vacancy activity was absent if the provided facts show phantom corrections or guardCorrections.
        - If buyer-state fields are the main signal, describe those buyer-state fields literally.
        - Avoid subjective intensifiers like "significantly".
        - Return only a stronger factual summary, not extra commentary.
        """
    ).strip()

    return {
        "model": model or get_summary_refinement_github_models_model(),
        "messages": [
            {
                "role": "system",
                "content": instructions,
            },
            {
                "role": "user",
                "content": encode_llm_context(context),
            },
        ],
        "response_format": {
            "type": "json_schema",
            "json_schema": {
                "name": "raw_log_evidence_summary_refinement",
                "schema": schema,
                "strict": True,
            },
        },
    }


def extract_chat_completion_text(response_payload: dict[str, Any]) -> str:
    for choice in response_payload.get("choices", []):
        message = choice.get("message", {})
        content = message.get("content", "")
        if isinstance(content, str) and content.strip():
            return content
        if isinstance(content, list):
            for item in content:
                text = item.get("text", "") if isinstance(item, dict) else ""
                if isinstance(text, str) and text.strip():
                    return text
    return ""


def generate_llm_suggestions(
    context: dict[str, Any],
    github_token: str | None,
    model: str | None = None,
) -> dict[str, Any] | None:
    if not github_token:
        return None

    last_error: AutomationError | None = None
    for variant in build_llm_context_variants(context):
        status, response_payload, raw_text = http_request(
            "POST",
            GITHUB_MODELS_CHAT_COMPLETIONS_URL,
            token=github_token,
            payload=build_llm_request_payload(variant, model=model),
            headers={
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        if status >= 400:
            error = AutomationError(f"GitHub Models request failed ({status}): {raw_text}")
            if status == 413:
                last_error = error
                continue
            raise error

        response_text = extract_chat_completion_text(response_payload)
        if not response_text:
            raise AutomationError("GitHub Models returned no output text.")

        suggestions = json.loads(response_text)
        return apply_llm_wording_guards(suggestions)

    if last_error is not None:
        raise last_error
    raise AutomationError("GitHub Models request failed: no request variant produced a response.")


def refine_evidence_summary(
    issue_fields: dict[str, str],
    parsed_log: dict[str, Any],
    deterministic_draft: dict[str, Any],
    draft: dict[str, Any],
    github_token: str | None,
    model: str | None = None,
) -> tuple[str, str]:
    original_summary = str(draft.get("evidence_summary", "")).strip()
    if not github_token or not original_summary:
        return original_summary, ""

    refinement_model = model or get_summary_refinement_github_models_model()
    context = build_summary_refinement_context(issue_fields, parsed_log, deterministic_draft, draft)
    status, response_payload, raw_text = http_request(
        "POST",
        GITHUB_MODELS_CHAT_COMPLETIONS_URL,
        token=github_token,
        payload=build_summary_refinement_request_payload(context, model=refinement_model),
        headers={
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    if status >= 400:
        raise AutomationError(f"GitHub Models request failed ({status}): {raw_text}")

    response_text = extract_chat_completion_text(response_payload)
    if not response_text:
        raise AutomationError("GitHub Models returned no output text.")

    payload = json.loads(response_text)
    refined_summary = apply_llm_wording_guards(
        {"evidence_summary": str(payload.get("evidence_summary", ""))}
    ).get("evidence_summary", "")
    refined_summary = str(refined_summary).strip()
    if not refined_summary:
        return original_summary, ""

    allowed_days = extract_supported_days(parsed_log)
    allowed_sample_indices = extract_supported_sample_indices(parsed_log)
    allowed_issue_refs = build_allowed_issue_refs(issue_fields, deterministic_draft)
    if has_unsupported_day_reference(refined_summary, allowed_days):
        return original_summary, ""
    if has_unsupported_sample_reference(refined_summary, allowed_sample_indices):
        return original_summary, ""
    if has_unsupported_issue_reference(refined_summary, allowed_issue_refs):
        return original_summary, ""
    if has_unsupported_evidence_summary_interpretation(refined_summary, parsed_log):
        return original_summary, ""
    return refined_summary, refinement_model


def normalize_evidence_title(title: str, fallback_title: str) -> str:
    cleaned = re.sub(r"\s+", " ", title.strip())
    if not cleaned:
        cleaned = fallback_title.strip()
    if not cleaned.startswith("[Software Evidence]"):
        cleaned = f"[Software Evidence] {cleaned}".strip()
    return cleaned


def title_is_generic(title: str, issue_fields: dict[str, str], fallback_title: str) -> bool:
    normalized = normalize_evidence_title(title, fallback_title)
    suffix = normalized.replace("[Software Evidence]", "", 1).strip().lower()
    raw_title = issue_fields.get("save_or_city_label", "").strip().lower()
    if not suffix or suffix == raw_title:
        return True
    if suffix.startswith("from raw #") or suffix == "from raw log intake":
        return True
    return len(suffix) < 16


def build_evidence_issue_title(fields: dict[str, str], raw_issue_title: str, raw_issue_number: int) -> str:
    candidate = normalize_evidence_title(
        fields.get("title", ""),
        f"[Software Evidence] from raw #{raw_issue_number}",
    )
    if candidate.strip() and not candidate.endswith("[Software Evidence]"):
        return candidate

    title = raw_issue_title.strip()
    prefix = "[Raw Log]"
    if title.startswith(prefix):
        suffix = title[len(prefix) :].strip()
        return f"[Software Evidence] {suffix or f'from raw #{raw_issue_number}'}"
    return f"[Software Evidence] from raw #{raw_issue_number}"


def supports_explicit_comparison(issue_fields: dict[str, str]) -> bool:
    comparison_hints = " ".join(
        [
            issue_fields.get("what_happened", ""),
            issue_fields.get("save_or_city_label", ""),
        ]
    ).lower()
    keywords = ("same save", "same lineage", "baseline", "compare", "comparison", "#", "trade patch")
    return any(keyword in comparison_hints for keyword in keywords)


def extract_supported_days(parsed_log: dict[str, Any]) -> set[int]:
    days: set[int] = set()
    for anchor in parsed_log.get("anchors", []):
        for field_name in ("sample_day", "end_day", "observation_end_day"):
            value = safe_int(anchor.get(field_name))
            if value > 0:
                days.add(value)
    return days


def extract_supported_sample_indices(parsed_log: dict[str, Any]) -> set[int]:
    sample_indices: set[int] = set()
    for anchor in parsed_log.get("anchors", []):
        for field_name in ("sample_index", "end_sample_index", "observation_end_sample_index"):
            value = safe_int(anchor.get(field_name))
            if value > 0:
                sample_indices.add(value)
    return sample_indices


def extract_supported_issue_refs(issue_fields: dict[str, str]) -> set[int]:
    refs: set[int] = set()
    for value in (issue_fields.get("what_happened", ""), issue_fields.get("save_or_city_label", "")):
        for match in re.findall(r"#(\d+)", value):
            refs.add(int(match))
    return refs


def build_allowed_issue_refs(
    issue_fields: dict[str, str],
    deterministic_draft: dict[str, Any] | None = None,
) -> set[int]:
    refs = extract_supported_issue_refs(issue_fields)
    if not deterministic_draft:
        return refs

    for field_name in (
        "title",
        "evidence_summary",
        "comparison_baseline",
        "notes",
        "reasoning_summary",
    ):
        for match in re.findall(r"#(\d+)", str(deterministic_draft.get(field_name, ""))):
            refs.add(int(match))
    return refs


def extract_excerpt_body_lines(log_excerpt: str) -> list[str]:
    lines: list[str] = []
    for line in log_excerpt.replace("\r\n", "\n").split("\n"):
        stripped = line.strip()
        if not stripped or stripped == "```text" or stripped == "```" or stripped.startswith("### "):
            continue
        lines.append(stripped)
    return lines


def has_unsupported_day_reference(text: str, allowed_days: set[int]) -> bool:
    for match in re.finditer(r"\bday[- ]?(\d+)\b", text, flags=re.IGNORECASE):
        if int(match.group(1)) not in allowed_days:
            return True
    return False


def has_unsupported_sample_reference(text: str, allowed_sample_indices: set[int]) -> bool:
    for match in re.finditer(r"\bsample(?:[_ -]?index)?[ =:-]?(\d+)\b", text, flags=re.IGNORECASE):
        if int(match.group(1)) not in allowed_sample_indices:
            return True
    return False


def has_unsupported_issue_reference(text: str, allowed_issue_refs: set[int]) -> bool:
    for match in re.finditer(r"#(\d+)", text):
        if int(match.group(1)) not in allowed_issue_refs:
            return True
    return False


def parsed_log_has_phantom_activity(parsed_log: dict[str, Any]) -> bool:
    if parsed_log.get("phantom_corrections"):
        return True
    for observation in parsed_log.get("latest_run_observations", []):
        if counter_value(observation, "phantomVacancy", "guardCorrections") > 0:
            return True
    latest_observation = parsed_log.get("latest_observation")
    return counter_value(latest_observation, "phantomVacancy", "guardCorrections") > 0


def has_unsupported_phantom_zero_claim(text: str, parsed_log: dict[str, Any]) -> bool:
    if not parsed_log_has_phantom_activity(parsed_log):
        return False
    return any(re.search(pattern, text, flags=re.IGNORECASE | re.DOTALL) for pattern in UNSUPPORTED_PHANTOM_ZERO_PATTERNS)


def has_unsupported_phantom_absence_claim(text: str, parsed_log: dict[str, Any]) -> bool:
    if not parsed_log_has_phantom_activity(parsed_log):
        return False
    return any(
        re.search(pattern, text, flags=re.IGNORECASE | re.DOTALL)
        for pattern in UNSUPPORTED_PHANTOM_ABSENCE_PATTERNS
    )


def has_unsupported_evidence_summary_interpretation(text: str, parsed_log: dict[str, Any]) -> bool:
    if any(re.search(pattern, text, flags=re.IGNORECASE) for pattern in UNSUPPORTED_SUMMARY_PATTERNS):
        return True
    if has_unsupported_phantom_absence_claim(text, parsed_log):
        return True
    if has_unsupported_phantom_zero_claim(text, parsed_log):
        return True
    return False


def has_unsupported_notes_interpretation(text: str, parsed_log: dict[str, Any]) -> bool:
    if any(re.search(pattern, text, flags=re.IGNORECASE) for pattern in UNSUPPORTED_NOTES_PATTERNS):
        return True
    if has_unsupported_phantom_absence_claim(text, parsed_log):
        return True
    return has_unsupported_phantom_zero_claim(text, parsed_log)


def normalize_missing_user_input(
    value: Any,
    allowed_missing_user_input: list[str],
) -> tuple[list[str], bool]:
    if not isinstance(value, list):
        return list(allowed_missing_user_input), bool(value)

    allowed_set = set(allowed_missing_user_input)
    normalized: list[str] = []
    had_unsupported = False
    for item in value:
        if not isinstance(item, str):
            had_unsupported = True
            continue
        cleaned = item.strip()
        if not cleaned:
            continue
        if cleaned not in allowed_set:
            had_unsupported = True
            continue
        if cleaned not in normalized:
            normalized.append(cleaned)
    return normalized, had_unsupported


def has_unsupported_reasoning_summary_format(text: str) -> bool:
    stripped = text.strip()
    if not stripped:
        return False
    if "..." in stripped or "…" in stripped or "??" in stripped:
        return True
    if len(stripped) > LLM_REASONING_SUMMARY_MAX_LENGTH:
        return True
    return False


def build_allowed_excerpt_pool(parsed_log: dict[str, Any]) -> list[str]:
    allowed: list[str] = []
    for snippet in parsed_log.get("selected_snippets", []):
        text = str(snippet.get("text", "")).strip()
        if text:
            allowed.append(text)
    for candidate in parsed_log.get("log_excerpt_candidates", []):
        for line in candidate.get("lines", []):
            text = str(line).strip()
            if text:
                allowed.append(text)
    return allowed


def validate_llm_draft(
    draft: dict[str, Any],
    issue_fields: dict[str, str],
    parsed_log: dict[str, Any],
    deterministic_draft: dict[str, Any],
) -> tuple[dict[str, Any], list[str]]:
    sanitized = dict(draft)
    sanitized["title"] = normalize_evidence_title(
        str(sanitized.get("title", "")),
        deterministic_draft.get("title", "[Software Evidence] from raw log intake"),
    )
    sanitized["comparison_baseline"] = str(sanitized.get("comparison_baseline", "")).strip()
    sanitized["confidence"] = str(sanitized.get("confidence", "medium")).strip().lower() or "medium"
    sanitized["symptom_classification"], sanitized["custom_symptom_classification"] = normalize_symptom_fields(
        str(sanitized.get("symptom_classification", "")),
        str(sanitized.get("custom_symptom_classification", "")),
    )
    sanitized["reasoning_summary"] = str(
        sanitized.get("reasoning_summary", deterministic_draft.get("reasoning_summary", ""))
    ).strip()

    errors: list[str] = []
    allowed_days = extract_supported_days(parsed_log)
    allowed_sample_indices = extract_supported_sample_indices(parsed_log)
    allowed_issue_refs = build_allowed_issue_refs(issue_fields, deterministic_draft)
    allowed_excerpt_pool = build_allowed_excerpt_pool(parsed_log)
    allowed_missing_user_input = list(dict.fromkeys(deterministic_draft.get("missing_user_input", [])))

    if title_is_generic(sanitized["title"], issue_fields, deterministic_draft.get("title", "")):
        errors.append("generic_title")

    if sanitized["confidence"] not in {"high", "medium", "low"}:
        sanitized["confidence"] = "medium"
        errors.append("invalid_confidence")

    if sanitized["confidence"] == "low":
        errors.append("low_confidence")

    if sanitized["comparison_baseline"] and not supports_explicit_comparison(issue_fields):
        sanitized["comparison_baseline"] = ""
        errors.append("unsupported_comparison")

    for field_name in ("title", "evidence_summary", "comparison_baseline", "notes"):
        field_value = str(sanitized.get(field_name, ""))
        if has_unsupported_day_reference(field_value, allowed_days):
            sanitized[field_name] = deterministic_draft.get(field_name, "") if field_name != "comparison_baseline" else ""
            errors.append(f"unsupported_day:{field_name}")
        if has_unsupported_sample_reference(field_value, allowed_sample_indices):
            sanitized[field_name] = deterministic_draft.get(field_name, "") if field_name != "comparison_baseline" else ""
            errors.append(f"unsupported_sample:{field_name}")
        if has_unsupported_issue_reference(field_value, allowed_issue_refs):
            sanitized[field_name] = deterministic_draft.get(field_name, "") if field_name != "comparison_baseline" else ""
            errors.append(f"unsupported_issue_ref:{field_name}")

    excerpt_lines = extract_excerpt_body_lines(str(sanitized.get("log_excerpt", "")))
    if excerpt_lines and any(not any(line in allowed for allowed in allowed_excerpt_pool) for line in excerpt_lines):
        sanitized["log_excerpt"] = deterministic_draft.get("log_excerpt", "")
        errors.append("unsupported_excerpt_line")

    sanitized["missing_user_input"], had_unsupported_missing = normalize_missing_user_input(
        sanitized.get("missing_user_input", []),
        allowed_missing_user_input,
    )
    if had_unsupported_missing:
        errors.append("unsupported_missing_user_input")

    if has_unsupported_evidence_summary_interpretation(str(sanitized.get("evidence_summary", "")), parsed_log):
        sanitized["evidence_summary"] = deterministic_draft.get("evidence_summary", "")
        errors.append("unsupported_evidence_summary_interpretation")

    if has_unsupported_notes_interpretation(str(sanitized.get("notes", "")), parsed_log):
        sanitized["notes"] = deterministic_draft.get("notes", "")
        errors.append("unsupported_notes_interpretation")

    if has_unsupported_reasoning_summary_format(sanitized["reasoning_summary"]):
        sanitized["reasoning_summary"] = deterministic_draft.get("reasoning_summary", "")
        errors.append("unsupported_reasoning_summary_format")

    return sanitized, list(dict.fromkeys(errors))


def validation_errors_require_escalation(errors: list[str]) -> bool:
    return any(error not in NON_FATAL_VALIDATION_ERRORS for error in errors)


def log_requires_editorial_escalation(parsed_log: dict[str, Any]) -> bool:
    return safe_int(parsed_log.get("observation_count", 0)) >= 3 or len(parsed_log.get("selected_snippets", [])) >= 3


def generate_validated_llm_draft(
    context: dict[str, Any],
    issue_fields: dict[str, str],
    parsed_log: dict[str, Any],
    deterministic_draft: dict[str, Any],
    github_token: str | None,
) -> dict[str, Any]:
    result = {
        "draft": None,
        "status": "skipped",
        "detail": "no eligible observation",
        "model": "",
        "validation_errors": [],
        "escalation_reason": "",
    }
    if not github_token:
        result["detail"] = "missing token"
        return result

    primary_model = get_primary_github_models_model()
    escalation_model = get_escalation_github_models_model()

    try:
        primary_draft = generate_llm_suggestions(context, github_token, model=primary_model)
    except AutomationError as error:
        if sanitize_llm_detail(str(error)).endswith("payload too large"):
            result["status"] = "fallback"
            result["detail"] = "context fallback: payload too large"
            return result
        raise
    if primary_draft is None:
        result["detail"] = "missing token"
        return result

    validated_primary, primary_errors = validate_llm_draft(
        primary_draft,
        issue_fields,
        parsed_log,
        deterministic_draft,
    )
    editorial_complexity = log_requires_editorial_escalation(parsed_log)
    primary_requires_escalation = validation_errors_require_escalation(primary_errors)
    should_escalate = primary_requires_escalation or editorial_complexity
    result["validation_errors"] = primary_errors

    if not should_escalate:
        refinement_model = ""
        try:
            validated_primary["evidence_summary"], refinement_model = refine_evidence_summary(
                issue_fields,
                parsed_log,
                deterministic_draft,
                validated_primary,
                github_token,
            )
        except AutomationError:
            refinement_model = ""
        result["draft"] = validated_primary
        result["status"] = "enabled"
        result["detail"] = (
            f"{primary_model}; summary_refinement={refinement_model}"
            if refinement_model
            else primary_model
        )
        result["model"] = primary_model
        return result

    if editorial_complexity and not primary_requires_escalation and not escalation_model:
        refinement_model = ""
        try:
            validated_primary["evidence_summary"], refinement_model = refine_evidence_summary(
                issue_fields,
                parsed_log,
                deterministic_draft,
                validated_primary,
                github_token,
            )
        except AutomationError:
            refinement_model = ""
        result["draft"] = validated_primary
        result["status"] = "enabled"
        result["detail"] = (
            f"{primary_model}; summary_refinement={refinement_model}"
            if refinement_model
            else primary_model
        )
        result["model"] = primary_model
        return result

    result["escalation_reason"] = ", ".join(primary_errors) if primary_errors else "editorial complexity"
    if escalation_model and escalation_model != primary_model:
        try:
            escalation_draft = generate_llm_suggestions(context, github_token, model=escalation_model)
        except AutomationError as error:
            if sanitize_llm_detail(str(error)).endswith("payload too large"):
                result["status"] = "fallback"
                result["detail"] = "context fallback: payload too large"
                return result
            raise
        if escalation_draft is not None:
            validated_escalation, escalation_errors = validate_llm_draft(
                escalation_draft,
                issue_fields,
                parsed_log,
                deterministic_draft,
            )
            if not validation_errors_require_escalation(escalation_errors):
                refinement_model = ""
                try:
                    validated_escalation["evidence_summary"], refinement_model = refine_evidence_summary(
                        issue_fields,
                        parsed_log,
                        deterministic_draft,
                        validated_escalation,
                        github_token,
                    )
                except AutomationError:
                    refinement_model = ""
                result["draft"] = validated_escalation
                result["status"] = "escalated"
                result["detail"] = (
                    f"{escalation_model}; summary_refinement={refinement_model}"
                    if refinement_model
                    else escalation_model
                )
                result["model"] = escalation_model
                result["validation_errors"] = escalation_errors
                return result
            result["validation_errors"] = escalation_errors

    result["status"] = "fallback"
    result["detail"] = (
        f"validation fallback: {result['escalation_reason'] or ', '.join(result['validation_errors']) or 'draft rejected'}"
    )
    return result


def apply_llm_wording_guards(suggestions: dict[str, Any]) -> dict[str, Any]:
    normalized = dict(suggestions)
    for field_name in (
        "title",
        "evidence_summary",
        "comparison_baseline",
        "confounders",
        "analysis_basis",
        "log_excerpt",
        "notes",
    ):
        value = str(normalized.get(field_name, ""))
        value = re.sub(r"\bzero resources\b", "lackResources=0", value, flags=re.IGNORECASE)
        value = re.sub(r"\bno resources\b", "lackResources=0", value, flags=re.IGNORECASE)
        normalized[field_name] = value
    return normalized


def sanitize_llm_detail(raw_detail: str) -> str:
    detail = raw_detail.lower()
    status_match = re.search(r"\((\d{3})\)", detail)
    status_prefix = f"http_{status_match.group(1)}: " if status_match else ""

    if "403" in detail and ("access" in detail or "models" in detail or "denied" in detail):
        return status_prefix + "models access denied"
    if "413" in detail or "too large" in detail or "payload" in detail and "large" in detail:
        return status_prefix + "payload too large"
    if "404" in detail or "model" in detail and "not found" in detail:
        return status_prefix + "model unavailable"
    if "429" in detail or "rate limit" in detail:
        return status_prefix + "rate limited"
    if "no output text" in detail or "empty" in detail:
        return status_prefix + "empty model response"
    if "token" in detail and "missing" in detail:
        return status_prefix + "missing token"
    return status_prefix + "request failed" if status_prefix else "request failed"


def normalize_multiline_value(value: str) -> str:
    return value.replace("\r\n", "\n").strip()


def dump_named_yaml_block(root_key: str, values: dict[str, str]) -> str:
    lines = [f"{root_key}:"]
    for field_name in OVERRIDE_FIELD_ORDER:
        value = normalize_multiline_value(values.get(field_name, ""))
        if not value:
            lines.append(f'  {field_name}: ""')
            continue

        if "\n" in value:
            lines.append(f"  {field_name}: |")
            for item in value.split("\n"):
                lines.append(f"    {item}")
            continue

        escaped = value.replace('"', '\\"')
        lines.append(f'  {field_name}: "{escaped}"')
    return "\n".join(lines)


def dump_reply_yaml(reply_fields: dict[str, str]) -> str:
    return dump_named_yaml_block("maintainer_reply", reply_fields)


def parse_named_yaml_block(yaml_text: str, root_key: str) -> dict[str, str]:
    result = {field_name: "" for field_name in OVERRIDE_FIELD_ORDER}
    lines = yaml_text.replace("\r\n", "\n").split("\n")
    if not lines or lines[0].strip() != f"{root_key}:":
        return result

    index = 1
    while index < len(lines):
        line = lines[index]
        if not line.startswith("  "):
            index += 1
            continue

        key_value = line[2:]
        if ":" not in key_value:
            index += 1
            continue

        key, value = key_value.split(":", 1)
        key = key.strip()
        value = value.lstrip()
        if key not in result:
            index += 1
            continue

        if value == "|":
            block_lines: list[str] = []
            index += 1
            while index < len(lines):
                block_line = lines[index]
                if block_line.startswith("    "):
                    block_lines.append(block_line[4:])
                    index += 1
                    continue
                if block_line == "":
                    block_lines.append("")
                    index += 1
                    continue
                break

            result[key] = "\n".join(block_lines).rstrip()
            continue

        cleaned = value.strip()
        if cleaned == '""':
            result[key] = ""
        elif cleaned.startswith('"') and cleaned.endswith('"'):
            result[key] = cleaned[1:-1].replace('\\"', '"')
        else:
            result[key] = cleaned
        index += 1

    return result


def parse_reply_yaml(yaml_text: str) -> dict[str, str]:
    return parse_named_yaml_block(yaml_text, "maintainer_reply")


def parse_override_yaml(yaml_text: str) -> dict[str, str]:
    return parse_named_yaml_block(yaml_text, "maintainer_overrides")


def extract_fenced_block(body: str, start_marker: str, end_marker: str) -> str:
    start_index = body.find(start_marker)
    end_index = body.find(end_marker)
    if start_index == -1 or end_index == -1 or end_index <= start_index:
        return ""

    section = body[start_index + len(start_marker) : end_index].strip()
    match = re.search(r"```[a-zA-Z0-9_-]*\n(.*?)\n```", section, flags=re.DOTALL)
    return match.group(1).strip() if match else ""


def extract_first_matching_fenced_block(body: str, root_key: str) -> str:
    pattern = re.compile(r"```[a-zA-Z0-9_-]*\n(.*?)\n```", flags=re.DOTALL)
    for match in pattern.finditer(body):
        candidate = match.group(1).strip()
        if candidate.startswith(f"{root_key}:"):
            return candidate
    return ""


def extract_first_named_yaml_block(body: str, root_key: str) -> str:
    fenced = extract_first_matching_fenced_block(body, root_key)
    if fenced:
        return fenced

    lines = body.replace("\r\n", "\n").split("\n")
    for start_index, line in enumerate(lines):
        stripped = line.strip()
        if stripped != f"{root_key}:":
            continue

        indent = len(line) - len(line.lstrip(" "))
        candidate_lines = [f"{root_key}:"]
        index = start_index + 1
        while index < len(lines):
            current_line = lines[index]
            if current_line == "":
                candidate_lines.append("")
                index += 1
                continue
            if len(current_line) - len(current_line.lstrip(" ")) >= indent + 2:
                candidate_lines.append(current_line[indent:])
                index += 1
                continue
            break

        candidate = "\n".join(candidate_lines).rstrip()
        if any(value.strip() for value in parse_named_yaml_block(candidate, root_key).values()):
            return candidate

    return ""


def comment_has_promote_command(comment_body: str) -> bool:
    return re.search(rf"(?mi)(?:^|\s){re.escape(PROMOTE_COMMAND)}(?:\s|$)", comment_body) is not None


def is_bot_comment(comment: dict[str, Any]) -> bool:
    user_login = str(comment.get("user", {}).get("login", ""))
    return user_login.endswith("[bot]")


def is_maintainer_comment(comment: dict[str, Any]) -> bool:
    association = str(comment.get("author_association", "")).upper()
    return association in {"OWNER", "MEMBER", "COLLABORATOR"}


def parse_reply_comment(comment_body: str) -> dict[str, str]:
    reply_yaml = extract_first_named_yaml_block(comment_body, "maintainer_reply")
    return parse_reply_yaml(reply_yaml) if reply_yaml else {field_name: "" for field_name in OVERRIDE_FIELD_ORDER}


def has_nonempty_reply_fields(reply_fields: dict[str, str]) -> bool:
    return any(value.strip() for value in reply_fields.values())


def find_latest_reply_comment(comments: list[dict[str, Any]]) -> dict[str, Any] | None:
    reply_comments = []
    for comment in comments:
        if is_bot_comment(comment):
            continue
        if not is_maintainer_comment(comment):
            continue
        parsed_reply = parse_reply_comment(comment.get("body", ""))
        if has_nonempty_reply_fields(parsed_reply):
            reply_comments.append(comment)

    if not reply_comments:
        return None
    return sorted(reply_comments, key=lambda item: item.get("updated_at", item.get("created_at", "")))[-1]


def render_managed_comment(
    issue_number: int,
    issue_fields: dict[str, str],
    log_source: dict[str, Any],
    parsed_log: dict[str, Any],
    deterministic_draft: dict[str, Any],
    llm_draft: dict[str, Any] | None,
    reply_fields: dict[str, str],
    redaction_notes: list[str],
    llm_status: str,
    llm_detail: str,
) -> tuple[str, dict[str, Any]]:
    latest_observation = parsed_log.get("latest_observation")
    preview_fields = build_preview_evidence_fields(
        issue_number,
        issue_fields,
        log_source,
        parsed_log,
        deterministic_draft,
        llm_draft,
        reply_fields,
    )

    combined_missing = list(
        dict.fromkeys(
            deterministic_draft.get("missing_user_input", [])
            + (llm_draft or {}).get("missing_user_input", [])
        )
    )

    payload = {
        "raw_issue": {
            "number": issue_number,
            "fields": issue_fields,
        },
        "log_source": {
            "mode": log_source["mode"],
            "url": sanitize_url(log_source["url"]) if log_source.get("url") else "",
            "attachment_urls": [sanitize_url(url) for url in log_source.get("attachment_urls", [])],
        },
        "redaction_notes": redaction_notes,
        "parsed_log": {
            "anchors": parsed_log.get("anchors", []),
            "anchor_index": parsed_log.get("anchor_index", {}),
            "latest_observation": latest_observation,
            "final_observation": parsed_log.get("final_observation"),
            "consumer_peak_observation": parsed_log.get("consumer_peak_observation"),
            "producer_peak_observation": parsed_log.get("producer_peak_observation"),
            "latest_software_office_detail": parsed_log.get("latest_software_office_detail"),
            "latest_patch_summary": parsed_log.get("latest_patch_summary", ""),
            "patch_summaries": parsed_log.get("patch_summaries", []),
            "phantom_corrections": parsed_log.get("phantom_corrections", []),
            "observation_count": parsed_log.get("observation_count", 0),
            "detail_count": parsed_log.get("detail_count", 0),
            "log_excerpt_candidates": parsed_log.get("log_excerpt_candidates", []),
            "selected_snippets": parsed_log.get("selected_snippets", []),
            "fallback_hints": parsed_log.get("fallback_hints", {}),
        },
        "deterministic_draft": deterministic_draft,
        "llm_draft": llm_draft,
        "combined_missing_user_input": combined_missing,
        "preview_fields": preview_fields,
        "llm_model": llm_detail if llm_draft else "",
        "llm_status": llm_status,
        "llm_detail": llm_detail,
    }

    redaction_summary = "; ".join(redaction_notes) if redaction_notes else "none"
    deterministic_reasoning = deterministic_draft.get("reasoning_summary", "not available")
    llm_reasoning = (llm_draft or {}).get("reasoning_summary", "")
    preview_body = render_evidence_issue_body(issue_number, 0, preview_fields).rstrip()

    body = "\n".join(
        [
            MANAGED_COMMENT_MARKER,
            f"Raw log triage draft for #{issue_number}. Do not edit this bot comment. Copy the `maintainer_reply` YAML below into a new maintainer comment, edit it there, and add `{PROMOTE_COMMAND}` in that same comment when the evidence issue is ready. Pasting the copied YAML directly is valid; code fences are optional.",
            "",
            "## Draft Evidence Issue Preview",
            f"Draft title: `{preview_fields.get('title', '')}`",
            preview_body,
            "",
            "### Draft provenance",
            f"- LLM status: `{llm_status}`",
            f"- LLM detail: `{llm_detail}`",
            f"- Missing before promote: `{', '.join(combined_missing) if combined_missing else 'none'}`",
            f"- Raw log source: `{log_source['mode']}`",
            f"- Redaction notes: `{redaction_summary}`",
            f"- Deterministic reasoning: `{deterministic_reasoning}`",
            f"- LLM reasoning: `{llm_reasoning if llm_reasoning else 'not used'}`",
            "",
            "### Maintainer reply template",
            f"Copy this YAML into a new comment, edit it there, and add `{PROMOTE_COMMAND}` in that same comment. GitHub's code-block Copy button returns plain YAML text, and that plain YAML is accepted.",
            REPLY_TEMPLATE_START_MARKER,
            "```yaml",
            dump_reply_yaml(reply_fields),
            "```",
            REPLY_TEMPLATE_END_MARKER,
            "",
            PAYLOAD_START_MARKER,
            "<details>",
            "<summary>Machine payload</summary>",
            "",
            "Do not edit this block manually.",
            "",
            "```json",
            json.dumps(payload, ensure_ascii=True, indent=2),
            "```",
            "</details>",
            PAYLOAD_END_MARKER,
        ]
    ).strip()

    if len(body) > COMMENT_BODY_LIMIT:
        compact_payload = dict(payload)
        compact_parsed = dict(payload["parsed_log"])
        compact_parsed["latest_software_office_detail"] = None
        compact_parsed["log_excerpt_candidates"] = select_preferred_excerpt_candidates(
            list(compact_parsed.get("log_excerpt_candidates", [])),
            2,
        )
        compact_parsed["patch_summaries"] = compact_parsed["patch_summaries"][-3:]
        compact_parsed["phantom_corrections"] = compact_parsed["phantom_corrections"][-3:]
        compact_payload["parsed_log"] = compact_parsed
        body = "\n".join(
            [
                MANAGED_COMMENT_MARKER,
                f"Raw log triage draft for #{issue_number}. Do not edit this bot comment. Copy the `maintainer_reply` YAML into a new maintainer comment and add `{PROMOTE_COMMAND}` there when ready. Plain pasted YAML is accepted.",
                "",
                "## Draft Evidence Issue Preview",
                f"Draft title: `{preview_fields.get('title', '')}`",
                preview_body,
                "",
                "### Draft provenance",
                f"- LLM status: `{llm_status}`",
                f"- LLM detail: `{llm_detail}`",
                f"- Missing before promote: `{', '.join(combined_missing) if combined_missing else 'none'}`",
                f"- Deterministic reasoning: `{deterministic_reasoning}`",
                (
                    f"- LLM reasoning: `{llm_reasoning}`"
                    if llm_reasoning and len(llm_reasoning) <= LLM_REASONING_SUMMARY_MAX_LENGTH
                    else "- LLM reasoning: `see machine payload`"
                )
                if llm_reasoning
                else "- LLM reasoning: `not used`",
                "",
                "### Maintainer reply template",
                REPLY_TEMPLATE_START_MARKER,
                "```yaml",
                dump_reply_yaml(reply_fields),
                "```",
                REPLY_TEMPLATE_END_MARKER,
                "",
                PAYLOAD_START_MARKER,
                "<details>",
                "<summary>Machine payload</summary>",
                "",
                "Do not edit this block manually.",
                "",
                "```json",
                json.dumps(compact_payload, ensure_ascii=True, indent=2),
                "```",
                "</details>",
                PAYLOAD_END_MARKER,
            ]
        ).strip()
        payload = compact_payload

    return body, payload


def parse_managed_comment(comment_body: str) -> dict[str, Any]:
    reply_yaml = extract_fenced_block(comment_body, REPLY_TEMPLATE_START_MARKER, REPLY_TEMPLATE_END_MARKER)
    if not reply_yaml:
        reply_yaml = extract_fenced_block(comment_body, OVERRIDES_START_MARKER, OVERRIDES_END_MARKER)
    payload_json = extract_fenced_block(comment_body, PAYLOAD_START_MARKER, PAYLOAD_END_MARKER)
    try:
        parsed_payload = json.loads(payload_json) if payload_json else {}
    except json.JSONDecodeError:
        parsed_payload = {}
    parsed_reply = {field_name: "" for field_name in OVERRIDE_FIELD_ORDER}
    if reply_yaml:
        parsed_reply = parse_reply_yaml(reply_yaml)
        if not has_nonempty_reply_fields(parsed_reply):
            parsed_reply = parse_override_yaml(reply_yaml)
    return {
        "reply_template": parsed_reply,
        "overrides": parsed_reply,
        "payload": parsed_payload,
    }


def get_issue_comments(repo: str, issue_number: int, token: str) -> list[dict[str, Any]]:
    comments: list[dict[str, Any]] = []
    page = 1

    while True:
        comments_url = (
            f"{GITHUB_API_BASE_URL}/repos/{repo}/issues/{issue_number}/comments"
            f"?per_page=100&page={page}"
        )
        status, payload, raw = http_request("GET", comments_url, token=token)
        if status >= 400:
            raise AutomationError(f"Failed to fetch issue comments ({status}): {raw}")
        if not isinstance(payload, list):
            return comments

        comments.extend(item for item in payload if isinstance(item, dict))
        if len(payload) < 100:
            return comments

        page += 1


def get_issue(repo: str, issue_number: int, token: str) -> dict[str, Any]:
    issue_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/issues/{issue_number}"
    status, payload, raw = http_request("GET", issue_url, token=token)
    if status >= 400:
        raise AutomationError(f"Failed to fetch issue ({status}): {raw}")
    if not isinstance(payload, dict):
        raise AutomationError("Failed to fetch issue: invalid payload.")
    return payload


def create_issue_comment(repo: str, issue_number: int, body: str, token: str) -> dict[str, Any]:
    comments_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/issues/{issue_number}/comments"
    status, payload, raw = http_request("POST", comments_url, token=token, payload={"body": body})
    if status >= 400:
        raise AutomationError(f"Failed to create issue comment ({status}): {raw}")
    return payload


def update_issue_comment(repo: str, comment_id: int, body: str, token: str) -> dict[str, Any]:
    comment_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/issues/comments/{comment_id}"
    status, payload, raw = http_request("PATCH", comment_url, token=token, payload={"body": body})
    if status >= 400:
        raise AutomationError(f"Failed to update issue comment ({status}): {raw}")
    return payload


def find_managed_comment(comments: list[dict[str, Any]]) -> dict[str, Any] | None:
    managed_comments = [comment for comment in comments if MANAGED_COMMENT_MARKER in comment.get("body", "")]
    if not managed_comments:
        return None
    return sorted(managed_comments, key=lambda item: item.get("updated_at", item.get("created_at", "")))[-1]


def upsert_managed_comment(repo: str, issue_number: int, body: str, token: str) -> dict[str, Any]:
    comments = get_issue_comments(repo, issue_number, token)
    managed_comment = find_managed_comment(comments)
    if managed_comment:
        return update_issue_comment(repo, int(managed_comment["id"]), body, token)
    return create_issue_comment(repo, issue_number, body, token)

def update_issue_state(repo: str, issue_number: int, *, state: str, token: str) -> dict[str, Any]:
    issue_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/issues/{issue_number}"
    status, payload, raw = http_request("PATCH", issue_url, token=token, payload={"state": state})
    if status >= 400:
        raise AutomationError(f"Failed to update issue state ({status}): {raw}")
    return payload


def create_issue(repo: str, title: str, body: str, labels: list[str], token: str) -> dict[str, Any]:
    issues_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/issues"
    status, payload, raw = http_request(
        "POST",
        issues_url,
        token=token,
        payload={"title": title, "body": body, "labels": labels},
    )
    if status >= 400:
        raise AutomationError(f"Failed to create issue ({status}): {raw}")
    return payload


def ensure_label(repo: str, name: str, color: str, description: str, token: str) -> None:
    label_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/labels/{urllib.parse.quote(name, safe='')}"
    status, _, _ = http_request("GET", label_url, token=token)
    if status == 200:
        return

    create_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/labels"
    status, _, raw = http_request(
        "POST",
        create_url,
        token=token,
        payload={"name": name, "color": color, "description": description},
    )
    if status not in (201, 422):
        raise AutomationError(f"Failed to ensure label `{name}` exists ({status}): {raw}")


def search_repo_issues(repo: str, query: str, token: str, *, per_page: int = 10) -> list[dict[str, Any]]:
    params = urllib.parse.urlencode({"q": f"repo:{repo} is:issue {query}", "per_page": str(per_page)})
    url = f"{GITHUB_API_BASE_URL}/search/issues?{params}"
    status, payload, raw = http_request("GET", url, token=token)
    if status >= 400:
        raise AutomationError(f"Failed to search repo issues ({status}): {raw}")
    if not isinstance(payload, dict):
        return []

    items = payload.get("items", [])
    return [item for item in items if isinstance(item, dict)]


def find_existing_promoted_issue(repo: str, raw_issue_number: int, token: str) -> dict[str, Any] | None:
    marker = SOURCE_RAW_ISSUE_MARKER.format(issue_number=raw_issue_number)
    for issue in search_repo_issues(repo, f'"{marker}"', token):
        if "pull_request" in issue:
            continue
        body = issue.get("body", "")
        if marker in body:
            return issue
    return None


def normalize_symptom_fields(symptom_classification: str, custom_symptom_classification: str) -> tuple[str, str]:
    label = symptom_classification.strip()
    custom_label = custom_symptom_classification.strip()
    if custom_label and not label:
        label = "other"
    if label and label not in SOFTWARE_EVIDENCE_CANONICAL_CLASSIFICATIONS and label != "other":
        custom_label = custom_label or label
        label = "other"
    return label, custom_label


def display_symptom_label(fields: dict[str, str]) -> str:
    label, custom_label = normalize_symptom_fields(
        fields.get("symptom-classification", ""),
        fields.get("custom-symptom-classification", ""),
    )
    if label == "other" and custom_label:
        return custom_label
    return label


def build_preview_artifacts_text(issue_number: int, log_source: dict[str, Any]) -> str:
    lines = [f"- raw intake issue: #{issue_number}", "- triage draft comment: this managed triage comment"]
    if log_source.get("url"):
        lines.append(f"- raw log attachment: {sanitize_url(log_source['url'])}")
    return "\n".join(lines)


def build_preview_evidence_fields(
    issue_number: int,
    issue_fields: dict[str, str],
    log_source: dict[str, Any],
    parsed_log: dict[str, Any],
    deterministic_draft: dict[str, Any],
    llm_draft: dict[str, Any] | None,
    reply_fields: dict[str, str],
) -> dict[str, str]:
    latest_observation = parsed_log.get("latest_observation") or {}

    def merged_text(field_name: str, raw_value: str = "") -> str:
        return choose_field_value(
            reply_fields.get(field_name, ""),
            raw_value,
            (llm_draft or {}).get(field_name, ""),
            deterministic_draft.get(field_name, ""),
        )

    symptom_classification, custom_symptom_classification = normalize_symptom_fields(
        merged_text("symptom_classification"),
        merged_text("custom_symptom_classification"),
    )
    confounders = choose_confounders_value(
        reply_fields.get("confounders", ""),
        deterministic_draft.get("confounders", ""),
        (llm_draft or {}).get("confounders", ""),
    )
    return {
        "title": merged_text("title", deterministic_draft.get("title", "")),
        "game-version": issue_fields.get("game_version", ""),
        "mod-version": issue_fields.get("mod_version", ""),
        "mod-ref": merged_text("mod_ref"),
        "settings": latest_observation.get("settings_raw", ""),
        "patch-state": latest_observation.get("patch_state", ""),
        "platform-notes": merged_text("platform_notes", issue_fields.get("platform_notes", "")),
        "other-mods": choose_field_value(issue_fields.get("other_mods", ""), "not recorded in this evidence log"),
        "scenario-label": merged_text("scenario_label", issue_fields.get("save_or_city_label", "")),
        "scenario-type": merged_text(
            "scenario_type",
            "existing save" if issue_fields.get("save_or_city_label") else "",
        ),
        "reproduction-conditions": merged_text("reproduction_conditions", issue_fields.get("what_happened", "")),
        "observation-window": latest_observation.get("observation_window_raw", ""),
        "comparison-baseline": merged_text("comparison_baseline"),
        "symptom-classification": symptom_classification,
        "custom-symptom-classification": custom_symptom_classification,
        "diagnostic-counters": latest_observation.get("diagnostic_counters_raw", ""),
        "log-excerpt": merged_text("log_excerpt"),
        "evidence-summary": merged_text("evidence_summary"),
        "confidence": merged_text("confidence", "medium"),
        "confounders": confounders or "none known",
        "analysis-basis": merged_text("analysis_basis"),
        "artifacts": build_preview_artifacts_text(issue_number, log_source),
        "notes": merged_text("notes"),
    }


def build_log_excerpt(payload: dict[str, Any]) -> str:
    llm_draft = payload.get("llm_draft") or {}
    deterministic_draft = payload.get("deterministic_draft") or {}
    log_excerpt = choose_field_value(
        llm_draft.get("log_excerpt", ""),
        deterministic_draft.get("log_excerpt", ""),
    )
    if log_excerpt:
        return log_excerpt

    excerpt_candidates = payload.get("parsed_log", {}).get("log_excerpt_candidates", [])
    if excerpt_candidates:
        return "\n\n".join(candidate.get("markdown", "") for candidate in excerpt_candidates if candidate.get("markdown"))

    latest_detail = payload["parsed_log"].get("latest_software_office_detail")
    if latest_detail:
        return "\n".join(["```text", truncate_text(latest_detail["values"], 4000), "```"])
    latest_observation = payload["parsed_log"].get("latest_observation")
    if latest_observation:
        return "\n".join(
            [
                "```text",
                truncate_text(latest_observation.get("diagnostic_counters_raw", ""), 4000),
                "```",
            ]
        )
    return ""


def build_artifacts_text(
    payload: dict[str, Any],
    triage_comment_url: str,
    raw_issue_url: str,
    maintainer_reply_url: str = "",
) -> str:
    lines = [f"- raw intake issue: {raw_issue_url}", f"- triage draft comment: {triage_comment_url}"]
    if maintainer_reply_url:
        lines.append(f"- maintainer promote reply: {maintainer_reply_url}")
    if payload["log_source"].get("url"):
        lines.append(f"- raw log attachment: {payload['log_source']['url']}")
    return "\n".join(lines)


def extract_required_issue_fields(template_path: str) -> list[str]:
    required_fields: list[str] = []
    current_id = ""
    inside_validations = False

    for raw_line in read_text_file(template_path).replace("\r\n", "\n").split("\n"):
        stripped = raw_line.strip()

        if stripped.startswith("id: "):
            current_id = stripped.split(":", 1)[1].strip()
            inside_validations = False
            continue

        if stripped == "validations:":
            inside_validations = True
            continue

        if inside_validations and stripped == "required: true" and current_id:
            required_fields.append(current_id)
            inside_validations = False
            continue

        if stripped.startswith("- type: "):
            inside_validations = False

    return required_fields


def choose_field_value(*values: str) -> str:
    for value in values:
        if value and value.strip():
            return value.strip()
    return ""


def merge_evidence_fields(
    payload: dict[str, Any],
    overrides: dict[str, str],
    triage_comment_url: str,
    raw_issue_url: str,
    maintainer_reply_url: str = "",
) -> dict[str, str]:
    raw_fields = payload["raw_issue"]["fields"]
    parsed_log = payload["parsed_log"]
    latest_observation = parsed_log.get("latest_observation") or {}
    deterministic = payload.get("deterministic_draft") or {}
    llm_draft = payload.get("llm_draft") or {}

    def merged_text(field_name: str, raw_value: str = "") -> str:
        return choose_field_value(
            overrides.get(field_name, ""),
            raw_value,
            llm_draft.get(field_name, ""),
            deterministic.get(field_name, ""),
        )

    symptom_classification = merged_text("symptom_classification")
    custom_symptom_classification = merged_text("custom_symptom_classification")
    symptom_classification, custom_symptom_classification = normalize_symptom_fields(
        symptom_classification,
        custom_symptom_classification,
    )
    merged_confounders = choose_confounders_value(
        overrides.get("confounders", ""),
        deterministic.get("confounders", ""),
        llm_draft.get("confounders", ""),
    )
    return {
        "title": merged_text("title", deterministic.get("title", "")),
        "game-version": raw_fields.get("game_version", ""),
        "mod-version": raw_fields.get("mod_version", ""),
        "mod-ref": merged_text("mod_ref"),
        "settings": latest_observation.get("settings_raw", ""),
        "patch-state": latest_observation.get("patch_state", ""),
        "platform-notes": merged_text("platform_notes", raw_fields.get("platform_notes", "")),
        "other-mods": choose_field_value(raw_fields.get("other_mods", ""), "not recorded in this evidence log"),
        "scenario-label": merged_text("scenario_label", raw_fields.get("save_or_city_label", "")),
        "scenario-type": merged_text(
            "scenario_type", "existing save" if raw_fields.get("save_or_city_label") else ""
        ),
        "reproduction-conditions": merged_text(
            "reproduction_conditions", raw_fields.get("what_happened", "")
        ),
        "observation-window": latest_observation.get("observation_window_raw", ""),
        "comparison-baseline": merged_text("comparison_baseline"),
        "symptom-classification": symptom_classification,
        "custom-symptom-classification": custom_symptom_classification,
        "diagnostic-counters": latest_observation.get("diagnostic_counters_raw", ""),
        "log-excerpt": merged_text("log_excerpt", build_log_excerpt(payload)),
        "evidence-summary": merged_text("evidence_summary"),
        "confidence": merged_text("confidence", "medium"),
        "confounders": merged_confounders or "none known",
        "analysis-basis": merged_text("analysis_basis"),
        "artifacts": build_artifacts_text(
            payload,
            triage_comment_url,
            raw_issue_url,
            maintainer_reply_url,
        ),
        "notes": merged_text("notes"),
    }


def find_missing_required_fields(fields: dict[str, str], required_fields: list[str]) -> list[str]:
    missing: list[str] = []
    for field_name in required_fields:
        value = fields.get(field_name, "")
        if not value or not value.strip():
            missing.append(field_name)
    return missing


def render_evidence_issue_body(
    raw_issue_number: int,
    draft_comment_id: int,
    fields: dict[str, str],
) -> str:
    source_marker = SOURCE_RAW_ISSUE_MARKER.format(issue_number=raw_issue_number)
    comment_marker = SOURCE_RAW_COMMENT_MARKER.format(comment_id=draft_comment_id)
    lines = [
        source_marker,
        comment_marker,
        "",
    ]

    ordered_sections = [
        ("Game version", fields["game-version"]),
        ("Mod version", fields["mod-version"]),
        ("Mod ref", fields["mod-ref"]),
        ("Settings", fields["settings"]),
        ("Patch state", fields["patch-state"]),
        ("Platform notes", fields["platform-notes"]),
        ("Other mods", fields["other-mods"]),
        ("Scenario label", fields["scenario-label"]),
        ("Scenario type", fields["scenario-type"]),
        ("Reproduction conditions", fields["reproduction-conditions"]),
        ("Observation window", fields["observation-window"]),
        ("Comparison baseline", fields["comparison-baseline"]),
        ("Symptom classification", display_symptom_label(fields)),
        ("Diagnostic counters", fields["diagnostic-counters"]),
        ("Log excerpt", fields["log-excerpt"]),
        ("Evidence summary", fields["evidence-summary"]),
        ("Confidence", fields["confidence"]),
        ("Confounders", fields["confounders"]),
        ("Analysis basis", fields["analysis-basis"]),
        ("Artifacts", fields["artifacts"]),
        ("Notes", fields["notes"]),
    ]

    optional_sections = {
        "Mod ref",
        "Platform notes",
        "Comparison baseline",
        "Log excerpt",
        "Analysis basis",
        "Artifacts",
        "Notes",
    }
    scalar_sections = {
        "Game version",
        "Mod version",
        "Mod ref",
        "Patch state",
        "Other mods",
        "Scenario label",
        "Scenario type",
        "Symptom classification",
        "Confidence",
    }

    for heading, value in ordered_sections:
        if not value:
            if heading in optional_sections:
                continue
            value = "_No response_"
        lines.append(f"## {heading}")
        if heading == "Observation window":
            lines.extend(["```text", format_observation_window_readable(value), "```"])
        elif heading == "Settings":
            lines.extend(["```text", format_settings_readable(value), "```"])
        elif heading == "Diagnostic counters":
            lines.extend(["```text", format_diagnostic_counters_readable(value), "```"])
        elif heading == "Log excerpt":
            if "```" in value or value.lstrip().startswith("### "):
                lines.append(value)
            else:
                lines.extend(["```text", value, "```"])
        elif heading in scalar_sections and value != "_No response_":
            lines.append(value if value.startswith("`") and value.endswith("`") else f"`{value}`")
        else:
            lines.append(value)
        lines.append("")

    return "\n".join(lines).strip() + "\n"


def render_structured_text_block(canonical_value: str, readable_value: str) -> list[str]:
    lines = ["Readable view", "```text", readable_value or canonical_value, "```"]
    if readable_value and normalize_multiline_value(readable_value) != normalize_multiline_value(canonical_value):
        lines.extend(["Raw string", "```text", canonical_value, "```"])
    return lines


def render_raw_text_block(value: str) -> list[str]:
    return ["Raw string", "```text", value, "```"]


def format_observation_window_readable(raw: str) -> str:
    parts = split_top_level_delimited(raw, ",")
    if not parts:
        return raw
    return ",\n".join(parts)


def format_settings_readable(raw: str) -> str:
    parts = split_top_level_delimited(raw, ",")
    if not parts:
        return raw
    return "\n".join(parts)


def format_diagnostic_counters_readable(raw: str) -> str:
    groups = split_group_entries(raw)
    if not groups:
        return raw
    return "\n".join(f"{name}({contents})" for name, contents in groups)


def build_missing_fields_comment(missing_fields: list[str]) -> str:
    formatted = ", ".join(f"`{field_name}`" for field_name in missing_fields)
    follow_up = (
        f"Post a maintainer reply comment that copies the `maintainer_reply` block, paste the YAML directly or wrap it in fences, fill those fields, "
        f"and include `{PROMOTE_COMMAND}` in that same comment."
    )
    return (
        "Promotion was blocked because the evidence issue still has missing required fields: "
        f"{formatted}. {follow_up}"
    )


def build_missing_reply_comment() -> str:
    return (
        "Promotion was blocked because the triggering comment did not include a valid `maintainer_reply` YAML block. "
        "Copy the template from the managed triage comment into a new maintainer comment, paste the YAML directly or wrap it in fences, edit it, and include "
        f"`{PROMOTE_COMMAND}` in that same comment."
    )


def build_invalid_reply_comment() -> str:
    return (
        "Promotion was blocked because the triggering comment did not contain any non-empty `maintainer_reply` fields. "
        "Copy the template from the managed triage comment, paste the YAML directly or wrap it in fences, edit at least the maintainer-owned fields, and try again."
    )


def build_attachment_failure_comment(error_message: str) -> str:
    return (
        "Raw log triage could not read the attached log file. "
        f"Reason: `{truncate_text(error_message, 500)}`. "
        "Please paste the relevant `softwareEvidenceDiagnostics` log text directly into the `Raw log` field, "
        "or re-attach a plain-text `.log` file and edit the issue."
    )


def build_raw_log_triage_failure_comment(error_message: str) -> str:
    return (
        "Raw log triage hit an automation failure before it could update the managed comment. "
        f"Reason: `{truncate_text(error_message, 500)}`. "
        "Retry the issue edit or rerun the workflow after the GitHub/network problem is resolved."
    )


def build_promotion_failure_comment(error_message: str) -> str:
    return (
        "Evidence promotion hit an automation failure before it could finish. "
        f"Reason: `{truncate_text(error_message, 500)}`. "
        "Retry the maintainer promote comment or rerun the workflow after the GitHub/network problem is resolved."
    )


def build_existing_evidence_comment(existing_issue: dict[str, Any]) -> str:
    return (
        "An evidence issue already exists for this raw log intake: "
        f"#{existing_issue['number']} ({existing_issue['html_url']}). "
        "No duplicate evidence issue was created."
    )
