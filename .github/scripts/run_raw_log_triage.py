import os

from raw_log_automation import (
    AutomationError,
    AttachmentDownloadError,
    build_attachment_failure_comment,
    build_deterministic_draft,
    create_issue_comment,
    find_managed_comment,
    generate_llm_suggestions,
    get_issue_comments,
    is_raw_log_issue,
    load_event_payload,
    parse_issue_form_sections,
    parse_log,
    parse_managed_comment,
    redact_log_text,
    render_managed_comment,
    select_raw_log_source,
    upsert_managed_comment,
)


def build_default_overrides(
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
        "confounders": (llm_draft or {}).get("confounders", "")
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
    if github_token and parsed_log.get("latest_observation"):
        llm_context = {
            "raw_issue": issue_fields,
            "latest_observation": parsed_log["latest_observation"],
            "latest_software_office_detail": parsed_log.get("latest_software_office_detail"),
            "latest_patch_summary": parsed_log.get("latest_patch_summary", ""),
            "phantom_corrections": parsed_log.get("phantom_corrections", []),
            "deterministic_draft": deterministic_draft,
            "redaction_notes": redaction_notes,
        }
        try:
            llm_draft = generate_llm_suggestions(llm_context, github_token)
        except AutomationError as error:
            print(f"LLM draft generation failed for issue #{issue_number}: {error}")

    comments = get_issue_comments(repo, issue_number, github_token)
    managed_comment = find_managed_comment(comments)
    if managed_comment:
        overrides = parse_managed_comment(managed_comment.get("body", ""))["overrides"]
    else:
        overrides = build_default_overrides(issue_fields, deterministic_draft, llm_draft)

    comment_body, _ = render_managed_comment(
        issue_number,
        issue_fields,
        log_source,
        parsed_log,
        deterministic_draft,
        llm_draft,
        overrides,
        redaction_notes,
    )
    updated_comment = upsert_managed_comment(repo, issue_number, comment_body, github_token)
    print(f"Managed triage comment upserted for issue #{issue_number}: {updated_comment.get('html_url', '')}")


if __name__ == "__main__":
    main()
