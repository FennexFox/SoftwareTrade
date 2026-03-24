import os

from perf_telemetry_automation import (
    AttachmentDownloadError,
    AutomationError,
    build_attachment_failure_comment,
    build_perf_telemetry_triage_failure_comment,
    comment_has_retriage_command,
    create_issue_comment,
    get_issue,
    get_parser_version,
    is_bot_comment,
    is_maintainer_comment,
    is_performance_telemetry_issue,
    load_event_payload,
)
from run_perf_telemetry_triage import run_triage_for_issue


def try_post_issue_comment(repo: str, issue_number: int, body: str, github_token: str) -> None:
    try:
        create_issue_comment(repo, issue_number, body, github_token)
    except AutomationError as error:
        print(f"Failed to post comment on performance issue #{issue_number}: {error}")


def main() -> None:
    event = load_event_payload(os.environ["GITHUB_EVENT_PATH"])
    repo = os.environ["GITHUB_REPOSITORY"]
    github_token = os.environ["GITHUB_TOKEN"]

    issue = event["issue"]
    if "pull_request" in issue:
        print("Issue comment event is for a pull request. Skipping.")
        return

    comment = event["comment"]
    comment_body = comment.get("body", "")
    issue_number = int(issue["number"])
    try:
        issue = get_issue(repo, issue_number, github_token)
    except AutomationError as error:
        print(f"Failed to refresh performance issue #{issue_number}: {error}")
        return

    issue_title = issue.get("title", "")
    issue_body = issue.get("body", "")

    if issue.get("state") != "open":
        print(f"Issue #{issue_number} is not open. Skipping.")
        return

    if is_bot_comment(comment):
        print(f"Comment #{comment['id']} is bot-authored. Skipping.")
        return

    if not is_maintainer_comment(comment):
        print(f"Comment #{comment['id']} is not maintainer-authored. Skipping.")
        return

    if not is_performance_telemetry_issue(issue_body, issue_title):
        print(f"Issue #{issue_number} is not a performance telemetry intake issue. Skipping.")
        return

    if not comment_has_retriage_command(comment_body):
        print(f"Comment #{comment['id']} does not request retriage. Skipping.")
        return

    try:
        updated_comment = run_triage_for_issue(repo, issue_number, issue_title, issue_body, github_token)
        try_post_issue_comment(
            repo,
            issue_number,
            (
                f"Re-triaged performance telemetry issue with parser `{get_parser_version()}` and updated "
                f"the managed triage comment: {updated_comment.get('html_url', '')}"
            ),
            github_token,
        )
        print(f"Re-triaged performance issue #{issue_number}: {updated_comment.get('html_url', '')}")
    except AttachmentDownloadError as error:
        try_post_issue_comment(repo, issue_number, build_attachment_failure_comment(str(error)), github_token)
        print(f"Attachment download failed for performance issue #{issue_number}: {error}")
    except AutomationError as error:
        try_post_issue_comment(repo, issue_number, build_perf_telemetry_triage_failure_comment(str(error)), github_token)
        print(f"Retriage failed for performance issue #{issue_number}: {error}")


if __name__ == "__main__":
    main()
