# Log Reporting

Use this guide when you want to submit a raw diagnostics log for maintainer triage.

This is for raw intake. It is not the same thing as opening a finalized `Software evidence` issue.

## Quick Path

If you only want the shortest safe setup:

1. turn on `EnableDemandDiagnostics`
2. leave `EnableTradePatch=false` unless you are deliberately collecting a comparison run
3. leave `CaptureStableEvidence=false`
4. leave `VerboseLogging=false`
5. reproduce the problem
6. submit a `Raw log report` issue with the relevant log text or `.log` file

Use the longer options below only when you specifically need baseline windows or verbose patch traces.

## Choose Your Capture Mode

### Suspicious-State Capture

Use this for most reports:

- `EnableTradePatch=false`
- `EnableDemandDiagnostics=true`
- `CaptureStableEvidence=false`
- `VerboseLogging=false`

This keeps logging focused on suspicious-state output.

### Baseline Or Comparison Capture

Use this when you need a bounded window even while the city looks stable:

- `EnableTradePatch=false` unless the run is a deliberate trade-patch comparison
- `EnableDemandDiagnostics=true`
- `CaptureStableEvidence=true`
- `VerboseLogging=false`

### Escalation Capture

Use this only when maintainers need the noisier correction or patch traces:

- `EnableTradePatch=false` unless the run is a deliberate trade-patch comparison
- `EnableDemandDiagnostics=true`
- `VerboseLogging=true`

Current setting defaults are documented in [README.md](./README.md).

## How To Submit A Raw Log

1. Open a new issue using the `Raw log report` form.
2. Fill in the game version, mod version, save or city label, and a short summary of what happened.
3. Add `Platform notes` when install layout, platform, or environment details could matter later.
4. In the `Raw log` field, either:
   - paste the relevant `softwareEvidenceDiagnostics` lines directly
   - drag and drop a plain-text `.log` file into the field
5. Submit the issue.

## What Makes A Report Useful

The best raw-log reports usually include:

- a bounded reproduction or observation window
- the setting state used for the run
- enough city or save context to identify the scenario later
- the relevant `softwareEvidenceDiagnostics` lines, not only a prose summary

If you are unsure how much to include, err toward preserving the raw diagnostics lines and keeping your prose short.

## What The Automation Does

After submission, the automation will:

- read the raw log
- redact obvious local filesystem paths before optional GitHub Models drafting
- extract the latest `softwareEvidenceDiagnostics observation_window(...)`
- preserve recent anchored `softwareEvidenceDiagnostics detail(...)` lines
- post a managed triage comment with a normalized draft and a copy-ready `maintainer_reply` YAML block

When both producer-side and consumer-side detail exist, the automation prefers:

- the latest anchored consumer excerpt
- the latest anchored producer excerpt

It may add one immediately previous distinct sample when short chronology materially changes the interpretation.

The semantic framing in the draft is provisional. The hard evidence is still the copied observation window, counters, and selected detail excerpts.

## Privacy Notes

Before posting publicly, remember:

- local filesystem paths can appear in mod logs
- those paths may include your local username
- automation redacts obvious local paths before optional GitHub Models drafting, but you should still review logs before posting them

## Raw Log Issue vs Software Evidence Issue

Use a `Raw log report` issue when you have raw intake material that still needs triage.

Use a `Software evidence` issue only when the run is already a reusable bounded evidence entry.

Raw-log intake issues are not the final comparable record. Maintainers may promote one into a `Software evidence` issue after review and cleanup.

## What Happens Next

After you submit:

- a managed triage comment will be added or updated on the raw-log issue
- maintainers may refine the draft and promote it into a `Software evidence` issue
- the promoted evidence issue will link back to the raw-log intake issue
- the raw-log intake issue is then typically closed

If the automation cannot download an attached log file, it will leave a comment asking for the relevant log text to be pasted directly into the issue.
