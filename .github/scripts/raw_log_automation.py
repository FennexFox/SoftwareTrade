import json
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

GITHUB_API_BASE_URL = "https://api.github.com"
GITHUB_MODELS_CHAT_COMPLETIONS_URL = "https://models.github.ai/inference/chat/completions"
DEFAULT_GITHUB_MODELS_MODEL = "openai/gpt-4.1-mini"
COMMENT_BODY_LIMIT = 60000
DETAIL_PREVIEW_LIMIT = 1800
LLM_DETAIL_EXCERPT_LIMIT = 700
LLM_SUMMARY_LINE_LIMIT = 220
LLM_MAX_PATCH_SUMMARIES = 2
LLM_MAX_PHANTOM_CORRECTIONS = 2

RAW_LOG_FORM_LABELS = {
    "game version": "game_version",
    "mod version": "mod_version",
    "save or city label": "save_or_city_label",
    "what happened": "what_happened",
    "other mods": "other_mods",
    "raw log": "raw_log",
}

OVERRIDE_FIELD_ORDER = [
    "scenario_label",
    "scenario_type",
    "reproduction_conditions",
    "mod_ref",
    "symptom_classification",
    "evidence_summary",
    "confounders",
    "notes",
]

SOFTWARE_EVIDENCE_CANONICAL_CLASSIFICATIONS = {
    "software_office_propertyless",
    "software_office_efficiency_zero",
    "software_office_lack_resources_zero",
    "software_demand_mismatch",
    "software_track_unclear",
}


class AutomationError(RuntimeError):
    pass


class AttachmentDownloadError(AutomationError):
    pass


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
    required_fields = {"game_version", "mod_version", "save_or_city_label", "what_happened", "raw_log"}
    return required_fields.issubset(parsed_sections.keys())


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
) -> tuple[int, dict[str, Any], str]:
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


def download_attachment(url: str, token: str | None) -> str:
    request_headers = {
        "User-Agent": "NoOfficeDemandFixRawLogAutomation/1.0",
        "Accept": "*/*",
    }
    if token:
        request_headers["Authorization"] = f"Bearer {token}"

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


def select_raw_log_source(issue_fields: dict[str, str], token: str | None) -> dict[str, Any]:
    raw_log_section = issue_fields.get("raw_log", "")
    attachment_urls = extract_attachment_urls(raw_log_section)

    if attachment_urls:
        preferred_url = attachment_urls[0]
        return {
            "mode": "attachment",
            "url": preferred_url,
            "text": download_attachment(preferred_url, token),
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
    for part in [piece.strip() for piece in raw.split(",") if piece.strip()]:
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


def parse_observation_line(line: str) -> dict[str, Any]:
    message = extract_log_message(line)
    markers = {
        "observation_window": "softwareEvidenceDiagnostics observation_window(",
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

    return {
        "line": line.strip(),
        "message": message,
        "observation_window_raw": observation_window_raw,
        "observation_window": parse_key_value_text(observation_window_raw, "="),
        "settings_raw": settings_raw,
        "settings": parse_key_value_text(settings_raw, ":"),
        "patch_state": patch_state.strip(),
        "diagnostic_counters_raw": diagnostic_counters_raw,
        "diagnostic_counters": parse_counter_groups(diagnostic_counters_raw),
        "diagnostic_context_raw": context_raw,
        "top_factors": [part.strip() for part in context_raw.split(",") if part.strip()],
    }


def parse_detail_line(line: str) -> dict[str, Any]:
    message = extract_log_message(line)
    prefix = "softwareEvidenceDiagnostics detail("
    start = message.index(prefix) + len(prefix)
    values_marker = ", values="
    values_index = message.index(values_marker)
    metadata_raw = message[start:values_index]
    values = message[values_index + len(values_marker) : -1]
    metadata = parse_key_value_text(metadata_raw, "=")
    return {
        "line": line.strip(),
        "message": message,
        "metadata_raw": metadata_raw,
        "metadata": metadata,
        "values": values,
    }


def parse_log(log_text: str) -> dict[str, Any]:
    observations: list[dict[str, Any]] = []
    software_office_details: list[dict[str, Any]] = []
    patch_summaries: list[str] = []
    phantom_corrections: list[str] = []

    for line in log_text.replace("\r\n", "\n").split("\n"):
        stripped = line.strip()
        if not stripped:
            continue

        if "softwareEvidenceDiagnostics observation_window(" in stripped:
            observations.append(parse_observation_line(stripped))
            continue

        if "softwareEvidenceDiagnostics detail(" in stripped and "detail_type=softwareOfficeStates" in stripped:
            software_office_details.append(parse_detail_line(stripped))
            continue

        if "Office resource storage patch applied." in stripped:
            patch_summaries.append(extract_log_message(stripped))
            continue

        if "Signature phantom vacancy guard corrected" in stripped:
            phantom_corrections.append(extract_log_message(stripped))

    latest_observation = observations[-1] if observations else None
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
        latest_detail = matching_details[-1] if matching_details else software_office_details[-1]

    return {
        "latest_observation": latest_observation,
        "latest_software_office_detail": latest_detail,
        "latest_patch_summary": patch_summaries[-1] if patch_summaries else "",
        "patch_summaries": patch_summaries[-5:],
        "phantom_corrections": phantom_corrections[-5:],
        "observation_count": len(observations),
        "detail_count": len(software_office_details),
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
    software = counter_groups.get("software", {})
    producers = counter_groups.get("softwareProducerOffices", {})
    consumers = counter_groups.get("softwareConsumerOffices", {})

    if (
        safe_int(software.get("propertyless")) > 0
        or safe_int(producers.get("propertyless")) > 0
        or safe_int(consumers.get("propertyless")) > 0
    ):
        return "software_office_propertyless"

    if (
        safe_int(producers.get("lackResourcesZero")) > 0
        or safe_int(consumers.get("lackResourcesZero")) > 0
    ):
        return "software_office_lack_resources_zero"

    if (
        safe_int(producers.get("efficiencyZero")) > 0
        or safe_int(consumers.get("efficiencyZero")) > 0
    ):
        return "software_office_efficiency_zero"

    return "software_track_unclear"


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

    if settings.get("EnableTradePatch"):
        confounder_lines.append("trade patch enabled during capture")

    if not settings.get("CaptureStableEvidence"):
        confounder_lines.append("no stable baseline capture in this raw intake")

    if safe_int(parsed_log.get("observation_count", 0)) <= 1:
        confounder_lines.append("single observation window in raw intake")

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
            "scenario_label": issue_fields.get("save_or_city_label", ""),
            "scenario_type": "existing save" if issue_fields.get("save_or_city_label") else "",
            "symptom_classification": "",
            "evidence_summary": "",
            "confounders": "",
            "notes": "",
            "missing_user_input": [
                "raw_log_with_softwareEvidenceDiagnostics",
                "scenario_label",
                "reproduction_conditions",
                "mod_ref",
            ],
            "confidence": "medium",
            "reasoning_summary": "No current-branch softwareEvidenceDiagnostics observation window was found in the raw log.",
        }

    counter_groups = latest_observation["diagnostic_counters"]
    derived_classification = derive_symptom_classification(counter_groups)
    patch_summary = parsed_log.get("latest_patch_summary", "")
    detail_values = ""
    if parsed_log.get("latest_software_office_detail"):
        detail_values = truncate_text(parsed_log["latest_software_office_detail"]["values"])

    summary_bits = [
        f"Observation window {latest_observation['observation_window_raw']}",
        f"patch_state={latest_observation['patch_state']}",
        f"suggested symptom={derived_classification}",
    ]
    if patch_summary:
        summary_bits.append(patch_summary)

    confounders = build_deterministic_confounders(issue_fields, latest_observation, parsed_log)

    note_lines = [
        f"Source raw issue: #{issue_number}",
        f"Raw log source: {log_source['mode']}",
    ]
    if log_source.get("url"):
        note_lines.append(f"Raw log attachment: {sanitize_url(log_source['url'])}")
    if redaction_notes:
        note_lines.append("Redaction notes: " + "; ".join(redaction_notes))
    if detail_values:
        note_lines.append("Latest softwareOfficeStates excerpt: " + detail_values)

    missing_user_input: list[str] = []
    if not issue_fields.get("save_or_city_label"):
        missing_user_input.append("scenario_label")
    if not issue_fields.get("what_happened"):
        missing_user_input.append("reproduction_conditions")
    missing_user_input.append("mod_ref")

    return {
        "scenario_label": issue_fields.get("save_or_city_label", ""),
        "scenario_type": "existing save" if issue_fields.get("save_or_city_label") else "",
        "symptom_classification": derived_classification,
        "evidence_summary": ". ".join(summary_bits) + ".",
        "confounders": confounders,
        "notes": "\n".join(note_lines),
        "missing_user_input": list(dict.fromkeys(missing_user_input)),
        "confidence": "medium",
        "reasoning_summary": "Derived from the latest softwareEvidenceDiagnostics observation window plus the latest matching softwareOfficeStates detail line.",
    }


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
        "other_mods": truncate_text(issue_fields.get("other_mods", ""), 300),
    }

    latest_detail = parsed_log.get("latest_software_office_detail")
    latest_detail_excerpt = ""
    if latest_detail:
        latest_detail_excerpt = truncate_text(latest_detail.get("values", ""), LLM_DETAIL_EXCERPT_LIMIT)

    return {
        "raw_issue": raw_issue_context,
        "latest_observation": parsed_log.get("latest_observation"),
        "latest_software_office_detail_excerpt": latest_detail_excerpt,
        "latest_patch_summary": truncate_text(
            parsed_log.get("latest_patch_summary", ""),
            LLM_SUMMARY_LINE_LIMIT,
        ),
        "recent_patch_summaries": [
            truncate_text(item, LLM_SUMMARY_LINE_LIMIT)
            for item in parsed_log.get("patch_summaries", [])[-LLM_MAX_PATCH_SUMMARIES:]
        ],
        "phantom_corrections": [
            truncate_text(item, LLM_SUMMARY_LINE_LIMIT)
            for item in parsed_log.get("phantom_corrections", [])[-LLM_MAX_PHANTOM_CORRECTIONS:]
        ],
        "deterministic_draft": deterministic_draft,
        "redaction_notes": redaction_notes,
    }


def build_llm_request_payload(context: dict[str, Any]) -> dict[str, Any]:
    schema = {
        "type": "object",
        "additionalProperties": False,
        "properties": {
            "symptom_classification": {"type": "string"},
            "evidence_summary": {"type": "string"},
            "confounders": {"type": "string"},
            "notes": {"type": "string"},
            "missing_user_input": {
                "type": "array",
                "items": {"type": "string"},
            },
            "reasoning_summary": {"type": "string"},
        },
        "required": [
            "symptom_classification",
            "evidence_summary",
            "confounders",
            "notes",
            "missing_user_input",
            "reasoning_summary",
        ],
    }

    instructions = textwrap.dedent(
        """
        You are drafting suggested fields for a GitHub evidence issue from a redacted game log.
        Follow these rules:
        - Do not invent numeric counters or quote counters that are not present in the provided facts.
        - Use only the provided structured facts and excerpts.
        - Leave fields empty rather than guessing when confidence is low.
        - Keep confounders short and checklist-like. Prefer 1-4 short lines about run conditions, other mods, patch state, missing baseline, or similar evidence limits.
        - Do not repeat deterministic confounders unless you are adding a distinct residual concern.
        - Use one of these labels when possible:
          software_office_propertyless, software_office_efficiency_zero,
          software_office_lack_resources_zero, software_demand_mismatch,
          software_track_unclear.
        - missing_user_input should list only fields that a maintainer or reporter still needs to supply.
        """
    ).strip()

    return {
        "model": DEFAULT_GITHUB_MODELS_MODEL,
        "messages": [
            {
                "role": "system",
                "content": instructions,
            },
            {
                "role": "user",
                "content": json.dumps(context, ensure_ascii=True, indent=2),
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


def generate_llm_suggestions(context: dict[str, Any], github_token: str | None) -> dict[str, Any] | None:
    if not github_token:
        return None

    status, response_payload, raw_text = http_request(
        "POST",
        GITHUB_MODELS_CHAT_COMPLETIONS_URL,
        token=github_token,
        payload=build_llm_request_payload(context),
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

    return json.loads(response_text)


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
    match = re.search(r"\s*```[a-zA-Z0-9_-]*\n(.*)\n\s*```", section, flags=re.DOTALL)
    return match.group(1).strip() if match else ""


def extract_first_matching_fenced_block(body: str, root_key: str) -> str:
    pattern = re.compile(r"```[a-zA-Z0-9_-]*\n(.*?)\n```", flags=re.DOTALL)
    for match in pattern.finditer(body):
        candidate = match.group(1).strip()
        if candidate.startswith(f"{root_key}:"):
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
    reply_yaml = extract_first_matching_fenced_block(comment_body, "maintainer_reply")
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
    latest_detail = parsed_log.get("latest_software_office_detail")

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
            "latest_observation": latest_observation,
            "latest_software_office_detail": latest_detail,
            "latest_patch_summary": parsed_log.get("latest_patch_summary", ""),
            "patch_summaries": parsed_log.get("patch_summaries", []),
            "phantom_corrections": parsed_log.get("phantom_corrections", []),
            "observation_count": parsed_log.get("observation_count", 0),
            "detail_count": parsed_log.get("detail_count", 0),
        },
        "deterministic_draft": deterministic_draft,
        "llm_draft": llm_draft,
        "combined_missing_user_input": combined_missing,
        "llm_model": DEFAULT_GITHUB_MODELS_MODEL if llm_draft else "",
        "llm_status": llm_status,
        "llm_detail": llm_detail,
    }

    latest_observation_raw = latest_observation["observation_window_raw"] if latest_observation else "not found"
    latest_detail_preview = truncate_text(latest_detail["values"]) if latest_detail else "not found"
    latest_patch_summary = parsed_log.get("latest_patch_summary") or "not found"
    redaction_summary = "; ".join(redaction_notes) if redaction_notes else "none"
    deterministic_reasoning = deterministic_draft.get("reasoning_summary", "not available")
    llm_reasoning = (llm_draft or {}).get("reasoning_summary", "")

    body = "\n".join(
        [
            MANAGED_COMMENT_MARKER,
            f"Raw log triage draft for #{issue_number}. Do not edit this bot comment. Copy the `maintainer_reply` block into a new maintainer comment, edit it there, and add `{PROMOTE_COMMAND}` in that same comment when the evidence issue is ready.",
            "",
            "## Normalized draft",
            f"- Latest observation window: `{latest_observation_raw}`",
            f"- Deterministic symptom suggestion: `{deterministic_draft.get('symptom_classification', '') or 'not available'}`",
            f"- LLM status: `{llm_status}`",
            f"- LLM detail: `{llm_detail}`",
            f"- Missing before promote: `{', '.join(combined_missing) if combined_missing else 'none'}`",
            f"- Raw log source: `{log_source['mode']}`",
            f"- Latest patch summary: `{latest_patch_summary}`",
            f"- Latest softwareOfficeStates excerpt: `{latest_detail_preview}`",
            f"- Redaction notes: `{redaction_summary}`",
            "",
            "### Suggested evidence fields",
            f"- `scenario_label`: `{reply_fields.get('scenario_label', '')}`",
            f"- `scenario_type`: `{reply_fields.get('scenario_type', '')}`",
            f"- `evidence_summary`: `{truncate_text(reply_fields.get('evidence_summary', ''), 600)}`",
            f"- `confounders`: `{truncate_text(reply_fields.get('confounders', ''), 600)}`",
            f"- `notes`: `{truncate_text(reply_fields.get('notes', ''), 600)}`",
            "",
            "### Draft provenance",
            f"- Deterministic reasoning: `{truncate_text(deterministic_reasoning, 400)}`",
            f"- LLM reasoning: `{truncate_text(llm_reasoning, 400) if llm_reasoning else 'not used'}`",
            "",
            "### Maintainer reply template",
            f"Copy this YAML block into a new comment, edit it there, and add `{PROMOTE_COMMAND}` in that same comment.",
            REPLY_TEMPLATE_START_MARKER,
            "```yaml",
            dump_reply_yaml(reply_fields),
            "```",
            REPLY_TEMPLATE_END_MARKER,
            "",
            "### Machine payload",
            "Do not edit this block manually.",
            PAYLOAD_START_MARKER,
            "```json",
            json.dumps(payload, ensure_ascii=True, indent=2),
            "```",
            PAYLOAD_END_MARKER,
        ]
    ).strip()

    if len(body) > COMMENT_BODY_LIMIT:
        compact_payload = dict(payload)
        compact_parsed = dict(payload["parsed_log"])
        compact_parsed["latest_software_office_detail"] = None
        compact_parsed["patch_summaries"] = compact_parsed["patch_summaries"][-3:]
        compact_parsed["phantom_corrections"] = compact_parsed["phantom_corrections"][-3:]
        compact_payload["parsed_log"] = compact_parsed
        body = "\n".join(
            [
                MANAGED_COMMENT_MARKER,
                f"Raw log triage draft for #{issue_number}. Do not edit this bot comment. Copy the `maintainer_reply` block into a new maintainer comment and add `{PROMOTE_COMMAND}` there when ready.",
                "",
                "## Normalized draft",
                f"- Latest observation window: `{latest_observation_raw}`",
                f"- Deterministic symptom suggestion: `{deterministic_draft.get('symptom_classification', '') or 'not available'}`",
                f"- LLM status: `{llm_status}`",
                f"- LLM detail: `{llm_detail}`",
                f"- Missing before promote: `{', '.join(combined_missing) if combined_missing else 'none'}`",
                "",
                "### Maintainer reply template",
                REPLY_TEMPLATE_START_MARKER,
                "```yaml",
                dump_reply_yaml(reply_fields),
                "```",
                REPLY_TEMPLATE_END_MARKER,
                "",
                "### Machine payload",
                PAYLOAD_START_MARKER,
                "```json",
                json.dumps(compact_payload, ensure_ascii=True, indent=2),
                "```",
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
    comments_url = f"{GITHUB_API_BASE_URL}/repos/{repo}/issues/{issue_number}/comments?per_page=100"
    status, payload, raw = http_request("GET", comments_url, token=token)
    if status >= 400:
        raise AutomationError(f"Failed to fetch issue comments ({status}): {raw}")
    return payload if isinstance(payload, list) else []


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


def remove_label(repo: str, issue_number: int, label_name: str, token: str) -> None:
    label_url = (
        f"{GITHUB_API_BASE_URL}/repos/{repo}/issues/{issue_number}/labels/"
        f"{urllib.parse.quote(label_name, safe='')}"
    )
    status, _, _ = http_request("DELETE", label_url, token=token)
    if status not in (200, 204, 404):
        raise AutomationError(f"Failed to remove label `{label_name}` from issue #{issue_number}.")


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


def list_repo_issues(repo: str, token: str, *, state: str = "all") -> list[dict[str, Any]]:
    url = f"{GITHUB_API_BASE_URL}/repos/{repo}/issues?state={state}&per_page=100"
    status, payload, raw = http_request("GET", url, token=token)
    if status >= 400:
        raise AutomationError(f"Failed to list repo issues ({status}): {raw}")
    return payload if isinstance(payload, list) else []


def find_existing_promoted_issue(repo: str, raw_issue_number: int, token: str) -> dict[str, Any] | None:
    marker = SOURCE_RAW_ISSUE_MARKER.format(issue_number=raw_issue_number)
    for issue in list_repo_issues(repo, token):
        if "pull_request" in issue:
            continue
        if marker in issue.get("body", ""):
            return issue
    return None


def build_log_excerpt(payload: dict[str, Any]) -> str:
    latest_detail = payload["parsed_log"].get("latest_software_office_detail")
    if latest_detail:
        return truncate_text(latest_detail["message"], 4000)
    latest_observation = payload["parsed_log"].get("latest_observation")
    if latest_observation:
        return truncate_text(latest_observation["line"], 4000)
    return ""


def build_artifacts_text(
    payload: dict[str, Any],
    triage_comment_url: str,
    raw_issue_url: str,
    maintainer_reply_url: str = "",
) -> str:
    lines = [f"raw intake issue: {raw_issue_url}", f"triage draft comment: {triage_comment_url}"]
    if maintainer_reply_url:
        lines.append(f"maintainer promote reply: {maintainer_reply_url}")
    if payload["log_source"].get("url"):
        lines.append(f"raw log attachment: {payload['log_source']['url']}")
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
    merged_confounders = choose_field_value(
        overrides.get("confounders", ""),
        join_unique_lines(
            deterministic.get("confounders", ""),
            llm_draft.get("confounders", ""),
        ),
    )
    return {
        "game-version": raw_fields.get("game_version", ""),
        "mod-version": raw_fields.get("mod_version", ""),
        "mod-ref": merged_text("mod_ref"),
        "settings": latest_observation.get("settings_raw", ""),
        "patch-state": latest_observation.get("patch_state", ""),
        "platform-notes": "",
        "other-mods": raw_fields.get("other_mods", ""),
        "scenario-label": merged_text("scenario_label", raw_fields.get("save_or_city_label", "")),
        "scenario-type": merged_text(
            "scenario_type", "existing save" if raw_fields.get("save_or_city_label") else ""
        ),
        "reproduction-conditions": merged_text(
            "reproduction_conditions", raw_fields.get("what_happened", "")
        ),
        "observation-window": latest_observation.get("observation_window_raw", ""),
        "comparison-baseline": "",
        "symptom-classification": symptom_classification,
        "custom-symptom-classification": (
            symptom_classification
            if symptom_classification and symptom_classification not in SOFTWARE_EVIDENCE_CANONICAL_CLASSIFICATIONS
            else ""
        ),
        "diagnostic-counters": latest_observation.get("diagnostic_counters_raw", ""),
        "log-excerpt": build_log_excerpt(payload),
        "evidence-summary": merged_text("evidence_summary"),
        "confidence": "medium",
        "confounders": merged_confounders or "none known",
        "analysis-basis": "",
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
    lines = [source_marker, comment_marker, ""]

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
        ("Symptom classification", fields["symptom-classification"]),
        ("Custom symptom classification", fields["custom-symptom-classification"]),
        ("Diagnostic counters", fields["diagnostic-counters"]),
        ("Log excerpt", fields["log-excerpt"]),
        ("Evidence summary", fields["evidence-summary"]),
        ("Confidence", fields["confidence"]),
        ("Confounders", fields["confounders"]),
        ("Analysis basis", fields["analysis-basis"]),
        ("Artifacts", fields["artifacts"]),
        ("Notes", fields["notes"]),
    ]

    for heading, value in ordered_sections:
        lines.append(f"### {heading}")
        lines.append(value if value else "_No response_")
        lines.append("")

    return "\n".join(lines).strip() + "\n"


def build_missing_fields_comment(missing_fields: list[str], *, via_label: bool = False) -> str:
    formatted = ", ".join(f"`{field_name}`" for field_name in missing_fields)
    if via_label:
        follow_up = (
            "Post a maintainer reply comment that copies the `maintainer_reply` block, fill those fields, "
            "then re-add the `promote: evidence` label."
        )
    else:
        follow_up = (
            f"Post a maintainer reply comment that copies the `maintainer_reply` block, fill those fields, "
            f"and include `{PROMOTE_COMMAND}` in that same comment."
        )
    return (
        "Promotion was blocked because the evidence issue still has missing required fields: "
        f"{formatted}. {follow_up}"
    )


def build_missing_reply_comment(*, via_label: bool = False) -> str:
    if via_label:
        return (
            "Promotion was blocked because no maintainer reply comment with a valid `maintainer_reply` YAML block was found. "
            "Copy the template from the managed triage comment into a new maintainer comment, edit it, then re-add the `promote: evidence` label."
        )
    return (
        "Promotion was blocked because the triggering comment did not include a valid `maintainer_reply` YAML block. "
        "Copy the template from the managed triage comment into a new maintainer comment, edit it, and include "
        f"`{PROMOTE_COMMAND}` in that same comment."
    )


def build_invalid_reply_comment() -> str:
    return (
        "Promotion was blocked because the triggering comment did not contain any non-empty `maintainer_reply` fields. "
        "Copy the template from the managed triage comment, edit at least the maintainer-owned fields, and try again."
    )


def build_attachment_failure_comment(error_message: str) -> str:
    return (
        "Raw log triage could not read the attached log file. "
        f"Reason: `{truncate_text(error_message, 500)}`. "
        "Please paste the relevant `softwareEvidenceDiagnostics` log text directly into the `Raw log` field, "
        "or re-attach a plain-text `.log` file and edit the issue."
    )


def build_existing_evidence_comment(existing_issue: dict[str, Any]) -> str:
    return (
        "An evidence issue already exists for this raw log intake: "
        f"#{existing_issue['number']} ({existing_issue['html_url']}). "
        "No duplicate evidence issue was created."
    )
