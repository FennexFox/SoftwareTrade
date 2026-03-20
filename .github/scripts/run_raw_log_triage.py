import os

from raw_log_automation import (
    AutomationError,
    AttachmentDownloadError,
    build_attachment_failure_comment,
    build_raw_log_triage_failure_comment,
    build_deterministic_draft,
    build_llm_context,
    choose_confounders_value,
    create_issue_comment,
    generate_validated_llm_draft,
    is_raw_log_issue,
    load_event_payload,
    parse_issue_form_sections,
    parse_log,
    redact_log_text,
    render_managed_comment,
    sanitize_llm_detail,
    select_raw_log_source,
    upsert_managed_comment,
)


def try_post_issue_comment(repo: str, issue_number: int, body: str, github_token: str) -> None:
    try:
        create_issue_comment(repo, issue_number, body, github_token)
    except AutomationError as error:
        print(f"Failed to post comment on raw issue #{issue_number}: {error}")


def build_default_reply_fields(
    issue_fields: dict[str, str],
    deterministic_draft: dict[str, str],
    llm_draft: dict[str, str] | None,
) -> dict[str, str]:
    return {
        "title": (llm_draft or {}).get("title", "") or deterministic_draft.get("title", ""),
        "scenario_label": issue_fields.get("save_or_city_label", "") or deterministic_draft.get("scenario_label", ""),
        "scenario_type": deterministic_draft.get("scenario_type", ""),
        "reproduction_conditions": issue_fields.get("what_happened", ""),
        "mod_ref": "",
        "platform_notes": issue_fields.get("platform_notes", "") or deterministic_draft.get("platform_notes", ""),
        "comparison_baseline": (llm_draft or {}).get("comparison_baseline", "")
        or deterministic_draft.get("comparison_baseline", ""),
        "symptom_classification": (llm_draft or {}).get("symptom_classification", "")
        or deterministic_draft.get("symptom_classification", ""),
        "custom_symptom_classification": (llm_draft or {}).get("custom_symptom_classification", "")
        or deterministic_draft.get("custom_symptom_classification", ""),
        "evidence_summary": (llm_draft or {}).get("evidence_summary", "")
        or deterministic_draft.get("evidence_summary", ""),
        "confidence": (llm_draft or {}).get("confidence", "") or deterministic_draft.get("confidence", "medium"),
        "confounders": choose_confounders_value(
            "",
            deterministic_draft.get("confounders", ""),
            (llm_draft or {}).get("confounders", ""),
        )
        or deterministic_draft.get("confounders", ""),
        "analysis_basis": (llm_draft or {}).get("analysis_basis", "")
        or deterministic_draft.get("analysis_basis", ""),
        "log_excerpt": (llm_draft or {}).get("log_excerpt", "")
        or deterministic_draft.get("log_excerpt", ""),
        "notes": (llm_draft or {}).get("notes", "") or deterministic_draft.get("notes", ""),
    }


def run_triage_for_issue(
    repo: str,
    issue_number: int,
    issue_title: str,
    issue_body: str,
    github_token: str,
) -> dict[str, str]:
    if not is_raw_log_issue(issue_body, issue_title):
        raise AutomationError(f"Issue #{issue_number} is not a raw-log intake issue.")

    issue_fields = parse_issue_form_sections(issue_body)
    log_source = select_raw_log_source(issue_fields)
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
            llm_result = generate_validated_llm_draft(
                llm_context,
                issue_fields,
                parsed_log,
                deterministic_draft,
                github_token,
            )
            llm_draft = llm_result["draft"]
            llm_status = str(llm_result["status"])
            llm_detail = str(llm_result["detail"])
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
    return updated_comment


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

    try:
        run_triage_for_issue(repo, issue_number, issue_title, issue_body, github_token)
    except AttachmentDownloadError as error:
        try_post_issue_comment(repo, issue_number, build_attachment_failure_comment(str(error)), github_token)
        print(f"Attachment download failed for issue #{issue_number}: {error}")
    except AutomationError as error:
        try_post_issue_comment(repo, issue_number, build_raw_log_triage_failure_comment(str(error)), github_token)
        print(f"Raw log triage failed for issue #{issue_number}: {error}")


if __name__ == "__main__":
    main()
