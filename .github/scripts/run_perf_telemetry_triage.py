import os

from perf_telemetry_automation import (
    AttachmentDownloadError,
    AutomationError,
    build_attachment_failure_comment,
    build_perf_telemetry_triage_failure_comment,
    build_triage_analysis,
    create_issue_comment,
    is_performance_telemetry_issue,
    load_event_payload,
    parse_issue_form_sections,
    render_managed_comment,
    upsert_perf_telemetry_managed_comment,
)


def try_post_issue_comment(repo: str, issue_number: int, body: str, github_token: str) -> None:
    try:
        create_issue_comment(repo, issue_number, body, github_token)
    except AutomationError as error:
        print(f"Failed to post comment on performance issue #{issue_number}: {error}")


def run_triage_for_issue(
    repo: str,
    issue_number: int,
    issue_title: str,
    issue_body: str,
    github_token: str,
) -> dict[str, str]:
    if not is_performance_telemetry_issue(issue_body, issue_title):
        raise AutomationError(f"Issue #{issue_number} is not a performance telemetry intake issue.")

    issue_fields = parse_issue_form_sections(issue_body)
    triage = build_triage_analysis(issue_number, issue_fields)
    comment_body = render_managed_comment(triage)
    updated_comment = upsert_perf_telemetry_managed_comment(repo, issue_number, comment_body, github_token)
    print(
        f"Managed performance telemetry triage comment upserted for issue #{issue_number}: "
        f"{updated_comment.get('html_url', '')}"
    )
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

    if not is_performance_telemetry_issue(issue_body, issue_title):
        print(f"Issue #{issue_number} is not a performance telemetry intake issue. Skipping.")
        return

    try:
        run_triage_for_issue(repo, issue_number, issue_title, issue_body, github_token)
    except AttachmentDownloadError as error:
        try_post_issue_comment(repo, issue_number, build_attachment_failure_comment(str(error)), github_token)
        print(f"Attachment download failed for performance issue #{issue_number}: {error}")
    except AutomationError as error:
        try_post_issue_comment(repo, issue_number, build_perf_telemetry_triage_failure_comment(str(error)), github_token)
        print(f"Performance telemetry triage failed for issue #{issue_number}: {error}")


if __name__ == "__main__":
    main()
