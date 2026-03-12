# Log Reporting

Use this guide when you want to share a raw diagnostics log and let maintainers
triage it into a normalized `software evidence` draft.

## Before You Capture Logs

- turn on `EnableDemandDiagnostics`
- leave `CaptureStableEvidence` off if you only want suspicious-state samples
- turn on `CaptureStableEvidence` when you need a bounded baseline window
- turn on `VerboseLogging` only when you also need the noisier patch and correction traces

Current setting defaults are documented in [README.md](./README.md).

## How To Submit A Raw Log

1. Open a new issue using the `Raw log report` form.
2. Fill in the game version, mod version, save or city label, and a short
   summary of what happened.
3. Add `Platform notes` when install layout, platform, or environment details
   could matter for later comparison.
4. In the `Raw log` field, either:
   - paste the relevant `softwareEvidenceDiagnostics` lines directly, or
   - drag-and-drop a plain-text `.log` file into the field
5. Submit the issue.

The automation will:

- read the raw log
- redact obvious local filesystem paths before optional GitHub Models drafting
- extract the latest `softwareEvidenceDiagnostics observation_window(...)`
- preserve recent anchored `softwareEvidenceDiagnostics detail(...)` lines, usually the newest relevant sample and the immediately previous distinct sample when chronology matters
- post a managed triage comment with a normalized draft and a copy-ready
  `maintainer_reply` YAML block

The draft is LLM-first for semantic framing, but the automation still treats the
copied counters, observation window, and anchored detail excerpts as the hard
evidence that excerpts and later validation must stay aligned to.

## Privacy Notes

- local filesystem paths can appear in mod logs
- attached logs may include your local username inside those paths
- the automation redacts obvious local paths before optional GitHub Models drafting, but
  you should still review logs before posting them publicly

## Raw Log Issue vs Software Evidence Issue

Use a `Raw log report` issue when you have raw intake material that still needs
triage.

Use a `Software evidence` issue only for a bounded run that is already worth
keeping as reusable evidence.

Raw-log intake issues are not the final comparable record. Maintainers may
promote one into a `Software evidence` issue after filling any missing
maintainer-only fields. When a `Software evidence` issue is created from raw-log
promotion, its initial symptom classification should be treated as provisional
until later evidence synthesis reviews the counters and excerpts together.

## After Submission

- a managed triage comment will be added or updated on the raw-log issue
- maintainers should copy the `maintainer_reply` YAML block into a new comment,
  paste the YAML directly or wrap it in fences, edit it there, and include
  `/promote-evidence` in that same comment
- when the managed triage comment shows multiple excerpt candidates, prefer the
  newest anchored excerpt unless the immediately previous sample adds important
  chronology for the final evidence entry
- the automation creates a plain-Markdown `Software evidence` issue, links it
  back to the raw-log issue, and closes the raw-log intake issue

If the automation cannot download an attached log file, it will leave a comment
asking for the relevant log text to be pasted directly into the issue.
