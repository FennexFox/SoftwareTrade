# Log Reporting

Use this guide when you want to submit a raw diagnostics log for maintainer triage.

This is for raw intake. It is not the same thing as opening a finalized `Software evidence` issue.

## Before You Capture Logs

- turn on `EnableDemandDiagnostics` before you capture logs
- leave `EnableOutsideConnectionVirtualSellerFix` at its current default state unless you are intentionally running an outside-connection virtual seller comparison
- leave `EnableVirtualOfficeResourceBuyerFix` at its current default state unless you are intentionally running a buyer-cadence comparison
- leave `EnableOfficeDemandDirectPatch` at its current default state unless you are intentionally comparing against the newer vanilla `3x` office-demand baseline
- restart the game before a comparison run if you changed `EnableOutsideConnectionVirtualSellerFix` or `EnableOfficeDemandDirectPatch`, because those Harmony patches apply on launch
- leave `CaptureStableEvidence` off if you only want suspicious-state samples
- turn on `CaptureStableEvidence` when you need a bounded baseline window
- turn on `VerboseLogging` only when you also need the noisier correction traces and supplemental `softwareTradeLifecycle` detail lines
- if you are submitting an older historical log, keep any retired `EnableTradePatch` field exactly as captured in the `settings=...` snapshot; it is legacy context, not a current setting

Current setting defaults are documented in [README.md](./README.md).
When you intentionally change either experimental software fix setting, keep the exact `settings=...` snapshot because later comparisons use that logged state as the source of truth.

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
- preserve recent anchored `softwareEvidenceDiagnostics detail(...)` lines, using the latest consumer excerpt plus the latest producer excerpt as the default pair when both roles exist and adding at most one immediately previous distinct sample only when short chronology materially affects interpretation
- keep verbose `detail_type=softwareTradeLifecycle` lines as supplemental artifacts when buyer/seller lifecycle transitions or seller snapshots matter; they do not replace the scheduled observation window or the concise `softwareOfficeStates` detail anchors
- post a managed triage comment with a normalized draft and a copy-ready
  `maintainer_reply` YAML block

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
- maintainers may rerun that managed triage comment later with `/retriage` after parser or wording updates land on `master`
- maintainers may refine the draft and promote it into a `Software evidence` issue
- the promoted evidence issue will link back to the raw-log intake issue
- the raw-log intake issue is then typically closed

If the automation cannot download an attached log file, it will leave a comment asking for the relevant log text to be pasted directly into the issue.
