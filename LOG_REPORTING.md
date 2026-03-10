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
3. In the `Raw log` field, either:
   - paste the relevant `softwareEvidenceDiagnostics` lines directly, or
   - drag-and-drop a plain-text `.log` file into the field
4. Submit the issue.

The automation will:

- read the raw log
- redact obvious local filesystem paths before optional GitHub Models drafting
- extract the latest `softwareEvidenceDiagnostics observation_window(...)`
- post a managed triage comment with a normalized draft and a
  `maintainer_overrides` block

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
maintainer-only fields.

## After Submission

- a managed triage comment will be added or updated on the raw-log issue
- maintainers can edit the `maintainer_overrides` block in that comment
- when the issue is ready, a maintainer adds the `promote: evidence` label
- the automation creates a plain-Markdown `Software evidence` issue, links it
  back to the raw-log issue, and closes the raw-log intake issue

If the automation cannot download an attached log file, it will leave a comment
asking for the relevant log text to be pasted directly into the issue.
