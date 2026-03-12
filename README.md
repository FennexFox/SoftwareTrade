# No Office Demand Fix

`No Office Demand Fix` is a Cities: Skylines II mod that provides one shipped
office-demand fix and one separately tracked investigation path:

- `Phantom Vacancy`: occupied properties that are still counted as market listings
- office-resource / `software` instability: a separate producer/consumer resource-flow investigation that can still collapse office-company efficiency

The current release ships the confirmed `Phantom Vacancy` fix for `Signature` buildings and keeps the `software` path available as optional investigation tooling rather than a claimed full fix.

## Current Release

What the current code does:

- removes stale `PropertyOnMarket` and `PropertyToBeOnMarket` state from occupied `Signature` office and industrial properties before demand and property search evaluate them
- includes an opt-in office-resource trade patch for outside connections and cargo stations when you want comparison data on the `software` track
- includes opt-in diagnostics for office demand, phantom vacancy, and `software` producer/consumer state when you are collecting evidence

What it does not claim:

- it is not a proven fix for every `No Office Demand` symptom
- the `software` track is still under investigation and is not presented as a confirmed fix
- `software` producer or consumer distress is not treated as direct proof of lower office demand by itself
- non-signature phantom vacancy is still monitored, not ruled out

## Settings

Current defaults from [Setting.cs](./NoOfficeDemandFix/Setting.cs):

| Setting | Default | What it does | When it applies |
| --- | --- | --- | --- |
| `EnablePhantomVacancyFix` | `true` | Removes stale market state from occupied `Signature` office and industrial properties. | Applies immediately to future simulation ticks. Disabling it stops future corrections but does not restore already cleaned-up market state. |
| `EnableTradePatch` | `false` | Adds office resources to outside connection and cargo station storage definitions for `software`-track comparison runs. | Applies on the next city/save load in the normal case. Restart first for the cleanest comparison if other mods may patch the same storage definitions. |
| `EnableDemandDiagnostics` | `false` | Emits office-demand, phantom-vacancy, and `software` producer/consumer diagnostics. | Live-applies while the game is running. Leave it off unless you are collecting evidence. |
| `DiagnosticsSamplesPerDay` | `2` | Sets how many scheduled diagnostic sample slots exist per displayed in-game day. | Matters only when diagnostics are enabled. Higher values produce denser logs. |
| `CaptureStableEvidence` | `false` | Keeps bounded scheduled `softwareEvidenceDiagnostics observation_window(...)` lines flowing even when the city looks stable. | Matters only when diagnostics are enabled. Use it for baseline or no-symptom evidence collection. |
| `VerboseLogging` | `false` | Adds the noisier correction and patch traces and forces diagnostics output at the configured cadence. | Applies immediately for ongoing diagnostics. Use it only for investigation. |

## Implementation

- `Signature` phantom-vacancy fix: [SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs)
- optional trade patch: [OfficeResourceStoragePatchSystem.cs](./NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs)
- diagnostics: [OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs)

## Current Position

The safest repository-facing summary of the current release is:

- confirmed fix for the reproduced `Signature` phantom-vacancy symptom
- optional `software` trade patch for controlled comparison runs
- optional diagnostics for follow-up investigation and evidence collection

Current evidence does not support treating `software` producer or consumer
distress as direct proof of lower office demand by itself. The `software` path
remains investigational, and the trade patch is best treated as a comparison
switch rather than a claimed full fix.

## Non-Goals

- faking office demand directly
- blanket vacancy overrides across every property type
- claiming the `software` track is solved without stronger evidence

## Reporting Logs

If you want to submit a raw diagnostics log for maintainer triage or later
promotion into a normalized evidence issue, start with
[LOG_REPORTING.md](./LOG_REPORTING.md).

## Contributor And Maintainer Docs

- Contributor workflow: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Maintainer and release workflow: [MAINTAINING.md](./MAINTAINING.md)
- Software evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- Software investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- Software evidence issue form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- Software investigation issue form: [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./.github/ISSUE_TEMPLATE/software_investigation.yml)
