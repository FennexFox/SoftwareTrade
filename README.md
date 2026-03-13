# No Office Demand Fix

`No Office Demand Fix` is a Cities: Skylines II mod with one shipped fix and one
separate investigation toolset:

- `Phantom Vacancy`: occupied properties that are still counted as market listings
- office-resource / `software` instability: a separate investigation into office-demand/global-sales undercount and narrower virtual import-path inconsistencies

The current release ships the confirmed `Signature` phantom-vacancy fix and
keeps the `software` path available as diagnostics and investigation guidance
rather than a finished end-user fix.

## Current Release

What the current code does:

- fixes stale `PropertyOnMarket` and `PropertyToBeOnMarket` state on occupied `Signature` office and industrial properties before demand and property search evaluate them
- includes opt-in diagnostics for office demand, phantom vacancy, and `software` producer/consumer office state when you are collecting evidence
- retires the earlier office-resource storage patch experiment because forcing zero-weight office resources through cargo/storage definitions does not match the current vanilla virtual-resource architecture

What it does not claim:

- it is not a proven fix for every `No Office Demand` case
- the `software` track is still under investigation and is not presented as a confirmed fix
- non-signature phantom vacancy is still monitored, not ruled out

## Settings

Current defaults from [Setting.cs](./NoOfficeDemandFix/Setting.cs):

| Setting | Default | Purpose |
| --- | --- | --- |
| `EnablePhantomVacancyFix` | `true` | Enables the shipped guard that removes stale market state from occupied `Signature` office and industrial properties. Applies immediately to future simulation ticks; disabling it stops future corrections but does not restore already cleaned-up market state. |
| `EnableDemandDiagnostics` | `false` | Live-applies office-demand, phantom-vacancy, and `software` producer/consumer diagnostics when the state looks suspicious. Leave it off unless you are collecting evidence. |
| `DiagnosticsSamplesPerDay` | `2` | Sets how many scheduled diagnostic samples are taken per displayed in-game day while diagnostics are enabled. Higher values produce denser logs for comparison and troubleshooting. |
| `CaptureStableEvidence` | `false` | Keeps bounded scheduled `softwareEvidenceDiagnostics observation_window(...)` lines flowing at the configured per-day cadence while diagnostics are enabled, even when the city looks stable. Use it only for baseline or no-symptom evidence collection. |
| `VerboseLogging` | `false` | Adds the noisier correction traces and supplemental office/trade detail lines and also forces diagnostics output at the configured per-day cadence while diagnostics are enabled. Use it only for investigation. |

## Implementation

- `Signature` phantom-vacancy fix: [SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs)
- diagnostics: [OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs)

## Current Position

The safest way to describe this release is:

- confirmed fix for the reproduced `Signature` phantom-vacancy symptom
- optional diagnostics for follow-up investigation
- retired office-resource storage patch experiment
- current `software` investigation is split between office-demand/global-sales undercount and narrower virtual import seller/path inconsistencies

Current evidence does not support treating `software` producer or consumer
distress as direct proof of lower office demand by itself. The `software` path
remains investigational, and physicalizing zero-weight office resources through
storage or cargo definitions is no longer treated as the right default fix
direction.

## Non-Goals

- faking office demand directly
- blanket vacancy overrides across every property type
- pushing zero-weight office resources through cargo/storage definitions as if they were physical goods
- claiming the `software` track is solved without stronger evidence

## Reporting Logs

If you want to submit a raw diagnostics log for maintainer triage or later
promotion into a normalized evidence issue, start with
[LOG_REPORTING.md](./LOG_REPORTING.md).

## Docs for Contributors and Maintainers

- Contributors: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Maintainers and operators: [MAINTAINING.md](./MAINTAINING.md)
- Software evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- Software investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- Software evidence form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- Software investigation umbrella form: [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./.github/ISSUE_TEMPLATE/software_investigation.yml)
