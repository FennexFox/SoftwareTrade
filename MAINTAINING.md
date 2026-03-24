# Maintaining

This document is the entry point for maintainer and repo-operator workflows.
Use it for release operations, `software`-track investigation intake, and repo
metadata maintenance.

It is not a replacement for [CONTRIBUTING.md](./CONTRIBUTING.md). Contributor
branching, commit, PR, and testing expectations stay there.

## GitHub Project Operations

Use the public project board [NoOfficeDemandFix Tracker](https://github.com/users/FennexFox/projects/7) as the issue-centric planning surface for this repo.

- All new issues start in `Inbox`.
- Triage moves an issue to exactly one of `Backlog`, `Ready`, or `Blocked`.
- `Backlog` means the item is valid but not scheduled yet.
- `Ready` means the item can be picked up without further setup.
- `Blocked` means the item needs a prerequisite such as reproduction, evidence, or a decision before work can continue.
- `In Progress` means active implementation or investigation work is underway.
- `Review` is for Delivery items awaiting human review.
- `Validate` is for Discovery items that need evidence confirmation or interpretation.
- `Done` means the work is finished and the linked issue is closed.

Type meanings:

- `Bug` for incorrect behavior or confirmed regressions
- `Feature` for intentional capability or behavior additions
- `Docs` for documentation-only changes
- `Repo Ops` for GitHub, workflow, automation, or repository maintenance work
- `Investigation` for hypothesis-driven analysis and reproduction work
- `Evidence` for normalized evidence issues promoted from raw logs or other intake
- `Performance` for telemetry, stall, and optimization analysis
- `Intake` for first-pass reports that still need triage
- `Release` for release-preparation tracking

Planning rules:

- keep `In Progress` WIP at `2` or less
- use `Milestone` as the only release-target field
- use `Iteration` for the short active set, starting on `2026-03-23` with weekly cadence
- use labels for domain tagging, not for lifecycle state
- keep Delivery and Discovery separated by `Type` and downstream status flow, not by the intake status

Project setup checklist:

- If you replace `Status` options through the API, GitHub may disable `Item added to project`, `Item closed`, and `Auto-close issue`; revisit those workflows in the UI and rebind them to the new status values.
- In the project UI, keep `Auto-add to project` enabled so new repo issues enter the board automatically.
- In the project UI, enable `Item added to project` and set `Status -> Inbox`.
- In the project UI, enable `Item closed` and set `Status -> Done`.
- In the project UI, enable `Auto-close issue` so moving an item to `Done` closes the linked issue.
- Keep PR-oriented workflows disabled so pull requests do not appear as first-class board items.
- Create these views in the project UI:
  - `Command`: board layout, grouped by `Status`, hide `Done`
  - `Inbox`: table layout, only items with `Status=Inbox`
  - `Current Iteration`: table layout, only items in the current iteration and not `Done`
  - `Discovery`: table layout, only `Investigation`, `Evidence`, `Performance`, or `Intake` items that are not `Done`
  - `Delivery`: table layout, only `Bug`, `Feature`, `Docs`, `Repo Ops`, or `Release` items that are not `Done`
  - `Blocked`: table layout, only items with `Status=Blocked`
  - `Release`: table layout, only `Release` items or items with a milestone set

Operationally, the board should stay issue-centric. PRs may still be linked to issues, but they should not be treated as first-class board items.

## Release Operations

- Treat [`.github/workflows/release.yml`](./.github/workflows/release.yml) as the authoritative release definition.
- For maintainer releases and local dry-runs, use the same inputs and sequence defined in the release workflow.
- Keep release-version and player-facing release-copy decisions in the release or merge-specific change, not in investigation docs.
- Treat `develop -> master` releases as evidence-gated when the release still ships experimental `software`-track behavior.
- Before tagging a release that includes the current software import corrections, collect at least one release-build bounded evidence run that is directly comparable to the current comparison-anchor software evidence entry on the same or a tightly matched save lineage.
- Use release-candidate settings that preserve comparison value: `EnablePhantomVacancyFix=True`, `EnableOutsideConnectionVirtualSellerFix=True`, `EnableVirtualOfficeResourceBuyerFix=True`, `EnableOfficeDemandDirectPatch=True`, `EnableDemandDiagnostics=True`, `DiagnosticsSamplesPerDay=8`, `CaptureStableEvidence=True`, `VerboseLogging=True`.
- Treat `3 days` as the minimum reusable release-gate window and prefer `5 days` when buyer-lifecycle interpretation is part of the release decision.
- Record a short validation note on the release PR before release confirming whether release-build diagnostics still preserve the current schema-level fields and verbose lifecycle artifacts needed for comparison.
- Do not tag the release until `PublishConfiguration.xml`, `README.md`, and the release evidence summary all agree that the software path remains experimental rather than solved.

## Software Investigation Quickstart

Shipped runtime lines versus investigation lines:

- shipped runtime fixes: `Signature` phantom-vacancy cleanup and the office AI chunk-iteration hotfix
- shipped comparability rollback: restore the pre-hotfix office demand baseline when you need like-for-like comparison against older `2x` office-demand runs rather than the newer vanilla `3x` multiplier
- active investigation line: outside-connection virtual import seller / buyer lifecycle instability for zero-weight office resources
- separate deferred line: office-demand / global-sales undercount remains a follow-up question rather than part of the shipped office AI fix

Runtime investigation logs use the `softwareEvidenceDiagnostics` vocabulary.

Settings:

- default capture: turn on `EnableDemandDiagnostics` before collecting logs; keep `CaptureStableEvidence=false` and `VerboseLogging=false`
- outside-connection virtual seller comparison capture: toggle `EnableOutsideConnectionVirtualSellerFix` only when you are explicitly comparing the outside-connection virtual seller path; record the exact state from `environment(settings=...)`
- buyer-cadence comparison capture: toggle `EnableVirtualOfficeResourceBuyerFix` only when you are explicitly comparing the corrective post-vanilla buyer pass; record the exact state from `environment(settings=...)`
- office-demand baseline comparison capture: toggle `EnableOfficeDemandDirectPatch` only when you are explicitly comparing the restored pre-hotfix `2x` baseline against the newer vanilla `3x` baseline; record the exact state from `environment(settings=...)`
- baseline capture: also enable `CaptureStableEvidence` when you need bounded scheduled observation windows even while the city looks stable
- escalation capture: enable `VerboseLogging` only when you also need noisier correction traces plus supplemental `detail_type=softwareTradeLifecycle` lines and, for discussion-`#63` follow-up checks, `detail_type=softwareVirtualResolutionProbe` lines
- treat historical `EnableTradePatch` values in old logs as legacy run context only; the storage-patch path is retired and should not be reintroduced as the default fix direction

Promotion flow:

1. Use the `Raw log report` issue for raw diagnostics intake.
2. Wait for the managed triage comment with the normalized draft and `maintainer_reply` YAML block.
3. If parser or draft wording changed on `master`, add a maintainer comment with `/retriage` on the raw-log issue to regenerate the managed triage comment with the latest parser.
4. Copy that `maintainer_reply` block into a new maintainer comment, paste it directly or wrap it in fences, edit it there, and include `/promote-evidence` in that same comment.
5. Promote only reusable bounded runs into a `Software evidence` issue.
6. Keep one `Software investigation` issue per hypothesis or investigation line and record comparison summaries there.

Review defaults:

- treat the latest bounded observation plus the latest anchored consumer excerpt and latest anchored producer excerpt as the default evidence view when both roles exist
- include at most the immediately previous distinct sample when short chronology materially improves the evidence entry
- treat copied observation anchors, counters, and selected detail excerpts as the hard evidence
- keep `patch_state=unknown` unless you can replace it with an exact known local deviation set
- treat missing producer-side trade-cost fields in the concise `input1(...)` / `input2(...)` formatter as intentional; use verbose `detail_type=softwareTradeLifecycle` lines when seller-state or buyer-lifecycle detail is the active question, and `detail_type=softwareVirtualResolutionProbe` when you are checking whether a zero-weight virtual fast-path really resolved
- keep the current split explicit: shipped office hotfixes can coexist with an unresolved `software` investigation line, and office-demand/global-sales undercount is still a separate deferred line
- record the pre-hotfix office demand baseline direct patch explicitly in comparison summaries because runs with this mod are no longer directly comparable to unmodded `3x` vanilla office-demand behavior

Detailed capture rules, interpretation rules, and comparison checkpoints live in
[LOG_REPORTING.md](./LOG_REPORTING.md),
[`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md),
and [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md).

## Performance Telemetry

The coarse performance telemetry CSVs are for before/after comparisons of
steady-state overhead and stall behavior.

Measured:

- coarse render-latency timing from wall-clock frame deltas
- coarse `SimulationSystem`, pathfind setup/queue, and mod update wall time
- buyer-fix inspection counts and mod-triggered path/repath request counts
- pathfind pending-request snapshots, coarse queue-length maxima, and worker backlog maxima
- merged stall-event duration, peak latency, p95 latency, and peak queue pressure

Intentionally not measured:

- GPU-only timing or render-pipeline breakdowns
- per-entity top offenders or reason-code explosions
- raw per-frame trace dumps
- asynchronous job execution time that does not block a measured `OnUpdate`

Design intent:

- keep observer effect low on already-stressed saves
- store only coarse in-memory summaries during runtime
- write CSV output on session end or final shutdown fallback rather than logging continuously
- treat telemetry as a performance-comparison artifact, not a semantic evidence artifact

Telemetry intake:

- use the `Performance telemetry report` issue for steady-state and stall regression intake
- prefer one `.zip` bundle per run containing `perf_summary.csv` and `perf_stalls.csv`
- direct comparison requires matching save/scenario identity, enabled-fix set, sampling interval, and stall threshold
- keep telemetry triage deterministic and observational; do not treat it as root-cause proof
- if a telemetry regression needs semantic interpretation, collect the matching diagnostics raw log on the same save and settings

## Automation Notes

- raw-log triage runs on raw-log intake issue open and edit events
- maintainers can rerun raw-log triage with `/retriage` after parser or prompt changes land on `master`
- performance-telemetry triage runs on performance telemetry intake issue open and edit events
- maintainers can rerun performance-telemetry triage with `/retriage`
- evidence promotion runs primarily from maintainer comments that include `/promote-evidence` and a non-empty `maintainer_reply` YAML block
- optional LLM drafting uses GitHub Models through the workflow `GITHUB_TOKEN`; keep `models: read` permission on the triage workflow
- deterministic automation is responsible for redaction, anchor extraction, excerpt-candidate bounds, validation, and conservative fallbacks
- if GitHub Models access is unavailable or the call fails, keep the deterministic fallback wording conservative and re-read the preserved anchors, counters, and excerpts before promoting

## References

- raw-log onboarding: [LOG_REPORTING.md](./LOG_REPORTING.md)
- performance telemetry onboarding: [PERF_REPORTING.md](./PERF_REPORTING.md)
- evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- evidence entry form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- umbrella investigation form: [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./.github/ISSUE_TEMPLATE/software_investigation.yml)
- contributor process: [CONTRIBUTING.md](./CONTRIBUTING.md)
- repo metadata under [`.github/`](./.github/) includes issue templates, pull request templates, instruction files, and workflows
