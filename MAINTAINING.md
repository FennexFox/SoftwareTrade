# Maintaining

## Purpose

This document is the entry point for maintainer and repo-operator workflows.

Use it when you are operating releases, running `software`-track investigations,
or updating repository metadata and process documents. It is not a replacement
for [CONTRIBUTING.md](./CONTRIBUTING.md); that document remains the main guide
for contributor-facing branch, commit, PR, and testing expectations.

## Release Operations

- Treat [`.github/workflows/release.yml`](./.github/workflows/release.yml) as the authoritative release definition.
- There is no dedicated `scripts/release.ps1` helper script in the current repo state.
- For maintainer releases and local dry-runs, follow the same inputs and sequence defined in the release workflow.

## Software Investigation

For `software`-track investigation work, use these documents together:

- raw-log onboarding: [LOG_REPORTING.md](./LOG_REPORTING.md)
- evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- evidence entry form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- umbrella investigation form: [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./.github/ISSUE_TEMPLATE/software_investigation.yml)

Runtime investigation logs use the `softwareEvidenceDiagnostics` vocabulary.

Recommended operating flow:

- release defaults: keep `EnableTradePatch`, `EnableDemandDiagnostics`, `CaptureStableEvidence`, and `VerboseLogging` off unless you are deliberately collecting software-track evidence
- default collection: enable `EnableDemandDiagnostics` and keep `CaptureStableEvidence` and `VerboseLogging` off when you only want suspicious-state evidence
- baseline or no-symptom collection: turn on `CaptureStableEvidence` to emit bounded observation windows at the configured per-day cadence without the extra verbose trace noise
- escalation: turn on `VerboseLogging` only when you also need the noisier correction and patch traces
- if diagnostics emit `patch_state=unknown`, keep that in the evidence entry unless you can name the exact local deviations for that run
- promote only reusable bounded runs into the software evidence form
- use the `Raw log report` intake form for raw diagnostics, then copy the managed `maintainer_reply` YAML block into a new maintainer comment, paste the YAML directly or wrap it in fences, edit it there, and add `/promote-evidence` in that same comment when the intake issue is ready to become reusable evidence
- treat the initial symptom classification on raw-log-promoted evidence issues as provisional; later synthesis may revise it after re-reading counters, excerpts, and comparison context
- keep one software investigation umbrella issue per hypothesis or investigation line, and record comparison summaries there
- use vanilla decompiled game code for claims about base-game trade lifecycle, virtual-resource handling, and update behavior; use this mod's code for claims about diagnostics output, local patches, and release defaults
- treat software-office distress and office-demand response as separate observed outcomes; do not infer falling office demand from `software` consumer efficiency collapse alone

Repository automation notes:

- raw-log triage runs on raw-log intake issue open and edit events
- evidence promotion runs primarily from maintainer reply comments that include `/promote-evidence`
- optional LLM drafting uses GitHub Models through the workflow `GITHUB_TOKEN`; keep `models: read` permission on the triage workflow, and treat deterministic parsing as the source of truth if GitHub Models access is unavailable or the call fails

## Contributor Process

For branching strategy, commit style, pull request expectations, and testing
expectations, use [CONTRIBUTING.md](./CONTRIBUTING.md).

## Repo Metadata

Operational repository metadata lives under [`.github/`](./.github/), including:

- issue templates
- pull request templates
- instruction files
- workflow definitions
