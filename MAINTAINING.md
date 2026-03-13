# Maintaining

This document is the entry point for maintainer and repo-operator workflows.
Use it for release operations, `software`-track investigation intake, and repo
metadata maintenance.

It is not a replacement for [CONTRIBUTING.md](./CONTRIBUTING.md). Contributor
branching, commit, PR, and testing expectations stay there.

## Release Operations

- Treat [`.github/workflows/release.yml`](./.github/workflows/release.yml) as the authoritative release definition.
- For maintainer releases and local dry-runs, use the same inputs and sequence defined in the release workflow.
- Keep release-version and player-facing release-copy decisions in the release or merge-specific change, not in investigation docs.

## Software Investigation Quickstart

Runtime investigation logs use the `softwareEvidenceDiagnostics` vocabulary.

Settings:

- default capture: enable `EnableDemandDiagnostics`; keep `CaptureStableEvidence` and `VerboseLogging` off
- baseline capture: also enable `CaptureStableEvidence` when you need bounded scheduled observation windows even while the city looks stable
- escalation capture: enable `VerboseLogging` only when you also need noisier correction traces plus supplemental `detail_type=softwareTradeLifecycle` lines and, for discussion-`#63` follow-up checks, `detail_type=softwareVirtualResolutionProbe` lines
- treat historical `EnableTradePatch` values in old logs as legacy run context only; the storage-patch path is retired and should not be reintroduced as the default fix direction

Promotion flow:

1. Use the `Raw log report` issue for raw diagnostics intake.
2. Wait for the managed triage comment with the normalized draft and `maintainer_reply` YAML block.
3. Copy that `maintainer_reply` block into a new maintainer comment, paste it directly or wrap it in fences, edit it there, and include `/promote-evidence` in that same comment.
4. Promote only reusable bounded runs into a `Software evidence` issue.
5. Keep one `Software investigation` issue per hypothesis or investigation line and record comparison summaries there.

Review defaults:

- treat the latest bounded observation plus the latest anchored consumer excerpt and latest anchored producer excerpt as the default evidence view when both roles exist
- include at most the immediately previous distinct sample when short chronology materially improves the evidence entry
- treat copied observation anchors, counters, and selected detail excerpts as the hard evidence
- keep `patch_state=unknown` unless you can replace it with an exact known local deviation set
- treat missing producer-side trade-cost fields in the concise `input1(...)` / `input2(...)` formatter as intentional; use verbose `detail_type=softwareTradeLifecycle` lines when seller-state or buyer-lifecycle detail is the active question, and `detail_type=softwareVirtualResolutionProbe` when you are checking whether a zero-weight virtual fast-path really resolved
- keep the current software investigation split explicit: office-demand/global-sales undercount is the primary line, while virtual import seller/path inconsistency is a narrower secondary line

Detailed capture rules, interpretation rules, and comparison checkpoints live in
[LOG_REPORTING.md](./LOG_REPORTING.md),
[`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md),
and [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md).

## Automation Notes

- raw-log triage runs on raw-log intake issue open and edit events
- evidence promotion runs primarily from maintainer comments that include `/promote-evidence` and a non-empty `maintainer_reply` YAML block
- optional LLM drafting uses GitHub Models through the workflow `GITHUB_TOKEN`; keep `models: read` permission on the triage workflow
- deterministic automation is responsible for redaction, anchor extraction, excerpt-candidate bounds, validation, and conservative fallbacks
- if GitHub Models access is unavailable or the call fails, keep the deterministic fallback wording conservative and re-read the preserved anchors, counters, and excerpts before promoting

## References

- raw-log onboarding: [LOG_REPORTING.md](./LOG_REPORTING.md)
- evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- evidence entry form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- umbrella investigation form: [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./.github/ISSUE_TEMPLATE/software_investigation.yml)
- contributor process: [CONTRIBUTING.md](./CONTRIBUTING.md)
- repo metadata under [`.github/`](./.github/) includes issue templates, pull request templates, instruction files, and workflows
