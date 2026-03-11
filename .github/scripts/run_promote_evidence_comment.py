import os

from raw_log_automation import (
    AutomationError,
    MANAGED_COMMENT_MARKER,
    build_evidence_issue_title,
    build_existing_evidence_comment,
    build_invalid_reply_comment,
    build_missing_fields_comment,
    build_missing_reply_comment,
    build_promotion_failure_comment,
    comment_has_promote_command,
    create_issue,
    create_issue_comment,
    ensure_label,
    extract_first_named_yaml_block,
    extract_required_issue_fields,
    find_existing_promoted_issue,
    find_managed_comment,
    get_issue,
    get_issue_comments,
    has_nonempty_reply_fields,
    is_bot_comment,
    is_maintainer_comment,
    is_raw_log_issue,
    load_event_payload,
    merge_evidence_fields,
    parse_managed_comment,
    parse_reply_comment,
    render_evidence_issue_body,
    update_issue_state,
)


PROMOTED_SOURCE_LABEL = "source: raw-log-promoted"

def try_post_issue_comment(repo: str, issue_number: int, body: str, github_token: str) -> None:
    try:
        create_issue_comment(repo, issue_number, body, github_token)
    except AutomationError as error:
        print(f"Failed to post comment on raw issue #{issue_number}: {error}")


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
        print(f"Failed to refresh raw issue #{issue_number}: {error}")
        return

    issue_title = issue.get("title", "")
    issue_body = issue.get("body", "")

    if issue.get("state") != "open":
        print(f"Issue #{issue_number} is not open. Skipping.")
        return

    try:
        if is_bot_comment(comment):
            print(f"Comment #{comment['id']} is bot-authored. Skipping.")
            return

        if not is_maintainer_comment(comment):
            print(f"Comment #{comment['id']} is not maintainer-authored. Skipping.")
            return

        if not is_raw_log_issue(issue_body, issue_title):
            print(f"Issue #{issue_number} is not a raw-log intake issue. Skipping.")
            return

        if not comment_has_promote_command(comment_body):
            print(f"Comment #{comment['id']} does not request promotion. Skipping.")
            return

        reply_yaml = extract_first_named_yaml_block(comment_body, "maintainer_reply")
        if not reply_yaml:
            try_post_issue_comment(repo, issue_number, build_missing_reply_comment(), github_token)
            return

        overrides = parse_reply_comment(comment_body)
        if not has_nonempty_reply_fields(overrides):
            try_post_issue_comment(repo, issue_number, build_invalid_reply_comment(), github_token)
            return

        existing_issue = find_existing_promoted_issue(repo, issue_number, github_token)
        if existing_issue:
            update_issue_state(repo, issue_number, state="closed", token=github_token)
            try_post_issue_comment(repo, issue_number, build_existing_evidence_comment(existing_issue), github_token)
            print(f"Existing evidence issue found for raw issue #{issue_number}: #{existing_issue['number']}")
            return

        comments = get_issue_comments(repo, issue_number, github_token)
        managed_comment = find_managed_comment(comments)
        if not managed_comment or MANAGED_COMMENT_MARKER not in managed_comment.get("body", ""):
            try_post_issue_comment(
                repo,
                issue_number,
                "Promotion was blocked because the managed triage comment was not found. Run raw-log triage again, then retry with a new maintainer reply comment.",
                github_token,
            )
            return

        parsed_comment = parse_managed_comment(managed_comment["body"])
        payload = parsed_comment["payload"]

        if not payload.get("parsed_log", {}).get("latest_observation"):
            try_post_issue_comment(
                repo,
                issue_number,
                "Promotion was blocked because the raw log draft does not contain a current-branch `softwareEvidenceDiagnostics observation_window(...)` entry. Update the raw log and re-run triage before promoting.",
                github_token,
            )
            return

        fields = merge_evidence_fields(
            payload,
            overrides,
            managed_comment["html_url"],
            issue["html_url"],
            comment["html_url"],
        )
        required_fields = extract_required_issue_fields(".github/ISSUE_TEMPLATE/software_evidence.yml")
        missing_fields = [field_name for field_name in required_fields if not fields.get(field_name, "").strip()]
        if missing_fields:
            try_post_issue_comment(
                repo,
                issue_number,
                build_missing_fields_comment(missing_fields),
                github_token,
            )
            return

        ensure_label(
            repo,
            PROMOTED_SOURCE_LABEL,
            "1f6feb",
            "Evidence issue created from a raw-log intake issue.",
            github_token,
        )
        evidence_title = build_evidence_issue_title(fields, issue["title"], issue_number)
        evidence_body = render_evidence_issue_body(issue_number, int(comment["id"]), fields)
        evidence_issue = create_issue(
            repo,
            evidence_title,
            evidence_body,
            ["investigation", "area: software-track", PROMOTED_SOURCE_LABEL],
            github_token,
        )

        update_issue_state(repo, issue_number, state="closed", token=github_token)
        create_issue_comment(
            repo,
            issue_number,
            f"Promoted into evidence issue #{evidence_issue['number']} ({evidence_issue['html_url']}). This raw-log intake issue is now closed.",
            github_token,
        )
        print(f"Created evidence issue #{evidence_issue['number']} from raw issue #{issue_number}.")
    except AutomationError as error:
        try_post_issue_comment(repo, issue_number, build_promotion_failure_comment(str(error)), github_token)
        print(f"Promotion failed for raw issue #{issue_number}: {error}")


if __name__ == "__main__":
    main()
