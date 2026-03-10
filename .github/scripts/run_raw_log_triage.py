import os

from raw_log_automation import (
    AutomationError,
    AttachmentDownloadError,
    build_attachment_failure_comment,
    build_deterministic_draft,
    build_llm_context,
    create_issue_comment,
    generate_llm_suggestions,
    is_raw_log_issue,
    join_unique_lines,
    load_event_payload,
    parse_issue_form_sections,
    parse_log,
    redact_log_text,
    render_managed_comment,
    sanitize_llm_detail,
    select_raw_log_source,
    upsert_managed_comment,
    DEFAULT_GITHUB_MODELS_MODEL,
)


def build_default_reply_fields(
    issue_fields: dict[str, str],
    deterministic_draft: dict[str, str],
    llm_draft: dict[str, str] | None,
) -> dict[str, str]:
    return {
        "scenario_label": issue_fields.get("save_or_city_label", "") or deterministic_draft.get("scenario_label", ""),
        "scenario_type": deterministic_draft.get("scenario_type", ""),
        "reproduction_conditions": issue_fields.get("what_happened", ""),
        "mod_ref": "",
        "symptom_classification": (llm_draft or {}).get("symptom_classification", "")
        or deterministic_draft.get("symptom_classification", ""),
        "evidence_summary": (llm_draft or {}).get("evidence_summary", "")
        or deterministic_draft.get("evidence_summary", ""),
        "confounders": join_unique_lines(
            deterministic_draft.get("confounders", ""),
            (llm_draft or {}).get("confounders", ""),
        )
        or deterministic_draft.get("confounders", ""),
        "notes": (llm_draft or {}).get("notes", "") or deterministic_draft.get("notes", ""),
    }


def main() -> None:
    event_path = os.environ["GITHUB_EVENT_PATH"]
    repo = os.environ["GITHUB_REPOSITORY"]
    github_token = os.environ["GITHUB_TOKEN"]

    event = load_event_payload(event_path)
    issue = event["issue"]
    issue_number = int(issue["number"])
    issue_title = issue.get("title", "")
    issue_body = issue.get("body", "")

    if not is_raw_log_issue(issue_body, issue_title):
        print(f"Issue #{issue_number} is not a raw-log intake issue. Skipping.")
        return

    issue_fields = parse_issue_form_sections(issue_body)
    try:
        log_source = select_raw_log_source(issue_fields, github_token)
    except AttachmentDownloadError as error:
        create_issue_comment(repo, issue_number, build_attachment_failure_comment(str(error)), github_token)
        print(f"Attachment download failed for issue #{issue_number}: {error}")
        return

    redacted_log, redaction_notes = redact_log_text(log_source["text"])
    parsed_log = parse_log(redacted_log)
    deterministic_draft = build_deterministic_draft(
        issue_number, issue_fields, parsed_log, log_source, redaction_notes
    )

    llm_draft = None
    llm_status = "skipped"
    llm_detail = "no eligible observation"
    if not github_token:
        llm_detail = "missing token"
    elif parsed_log.get("latest_observation"):
        llm_context = build_llm_context(
            issue_fields,
            parsed_log,
            deterministic_draft,
            redaction_notes,
        )
        try:
            llm_draft = generate_llm_suggestions(llm_context, github_token)
            llm_status = "enabled"
            llm_detail = DEFAULT_GITHUB_MODELS_MODEL
        except AutomationError as error:
            llm_status = "failed"
            llm_detail = sanitize_llm_detail(str(error))
            print(f"LLM draft generation failed for issue #{issue_number}: {error}")
    reply_fields = build_default_reply_fields(issue_fields, deterministic_draft, llm_draft)

    comment_body, _ = render_managed_comment(
        issue_number,
        issue_fields,
        log_source,
        parsed_log,
        deterministic_draft,
        llm_draft,
        reply_fields,
        redaction_notes,
        llm_status,
        llm_detail,
    )
    updated_comment = upsert_managed_comment(repo, issue_number, comment_body, github_token)
    print(f"Managed triage comment upserted for issue #{issue_number}: {updated_comment.get('html_url', '')}")


if __name__ == "__main__":
    main()
