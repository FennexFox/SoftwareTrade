# Log Reporting

Use this guide when you want to share a raw diagnostics log and let maintainers
triage it into a normalized `software evidence` draft.

## Before You Capture Logs

- leave `EnableDemandDiagnostics` on
- leave `EnableOutsideConnectionVirtualSellerFix` at its current default state unless you are intentionally running an outside-connection virtual seller comparison
- leave `EnableVirtualOfficeResourceBuyerFix` at its current default state unless you are intentionally running a buyer-cadence comparison
- leave `CaptureStableEvidence` off if you only want suspicious-state samples
- turn on `CaptureStableEvidence` when you need a bounded baseline window
- turn on `VerboseLogging` only when you also need the noisier correction traces and supplemental `softwareTradeLifecycle` detail lines
- if you are submitting an older historical log, keep any retired `EnableTradePatch` field exactly as captured in the `settings=...` snapshot; it is legacy context, not a current setting

Current setting defaults are documented in [README.md](./README.md).
When you intentionally change either experimental software fix setting, keep the exact `settings=...` snapshot because later comparisons use that logged state as the source of truth.

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

## What The Automation Does

- read the raw log
- redact obvious local filesystem paths before optional GitHub Models drafting
- extract the latest `softwareEvidenceDiagnostics observation_window(...)`
- preserve recent anchored `softwareEvidenceDiagnostics detail(...)` lines, using the latest consumer excerpt plus the latest producer excerpt as the default pair when both roles exist and adding at most one immediately previous distinct sample only when short chronology materially affects interpretation
- keep verbose `detail_type=softwareTradeLifecycle` lines as supplemental artifacts when buyer/seller lifecycle transitions or seller snapshots matter; they do not replace the scheduled observation window or the concise `softwareOfficeStates` detail anchors
- post a managed triage comment with a normalized draft and a copy-ready
  `maintainer_reply` YAML block

The draft is LLM-first for semantic framing, but the automation still treats the
observation window, copied counters, and anchored detail excerpts as the hard
evidence that excerpts and later validation must stay aligned to. Keep
`start_day` / `end_day` as the primary window bounds; treat `sample_count` as
emitted observation density and `skipped_sample_slots`, when present, as
supporting gap context.

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

## What Maintainers Do After Submission

- a managed triage comment will be added or updated on the raw-log issue
- maintainers should copy the `maintainer_reply` YAML block into a new comment,
  paste the YAML directly or wrap it in fences, edit it there, and include
  `/promote-evidence` in that same comment
- when the managed triage comment shows multiple excerpt candidates, prefer the
  latest anchored consumer excerpt plus the latest anchored producer excerpt
  when both exist; use an immediately previous sample only when it adds
  important chronology for the final evidence entry (for example, when it
  shows the onset of a condition that persists in the latest sample)
- keep the copied observation window, counters, and selected detail excerpts aligned; do not swap in older detail lines unless the chronology is explicitly the point of the final evidence entry
- the automation creates a plain-Markdown `Software evidence` issue, links it
  back to the raw-log issue, and closes the raw-log intake issue

If the automation cannot download an attached log file, it will leave a comment
asking for the relevant log text to be pasted directly into the issue.
