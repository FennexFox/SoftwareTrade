# No Office Demand Fix

`No Office Demand Fix` is a Cities: Skylines II mod focused on two office-demand failure tracks:

- `Phantom Vacancy`: occupied properties that are still counted as market listings
- office-resource / `software` instability: a separate trade and storage path that can still collapse office efficiency

The current release ships a confirmed fix for the reproduced `Signature` phantom-vacancy case and keeps the `software` track available as an optional mitigation plus diagnostics.

## Current Release

What the current code does:

- fixes stale `PropertyOnMarket` and `PropertyToBeOnMarket` state on occupied `Signature` office and industrial properties before demand and property search evaluate them
- includes an optional prefab-level office-resource trade patch for outside connections and cargo stations
- includes diagnostics for office demand, phantom vacancy, and `software` office health

What it does not claim:

- it is not a proven fix for every `No Office Demand` case
- the `software` track is still under investigation
- non-signature phantom vacancy is still monitored, not ruled out

## Settings

Current defaults from [Setting.cs](./NoOfficeDemandFix/Setting.cs):

| Setting | Default | Purpose |
| --- | --- | --- |
| `EnableTradePatch` | `false` | Adds office resources to outside connection and cargo station storage definitions. Reload or restart after changing it. |
| `EnablePhantomVacancyFix` | `true` | Enables the shipped guard that removes stale market state from occupied `Signature` office and industrial properties. Reload after changing it. |
| `EnableDemandDiagnostics` | `false` | Logs office-demand, phantom-vacancy, and `software` diagnostics when the state looks suspicious. |
| `CaptureStableEvidence` | `false` | Keeps daily bounded `softwareEvidenceDiagnostics` windows flowing while diagnostics are enabled, even when the city looks stable. Use it for baseline or no-symptom evidence. |
| `VerboseLogging` | `false` | Adds the noisier correction and patch traces and also forces daily diagnostics while diagnostics are enabled. |

## Implementation

- `Signature` phantom-vacancy fix: [SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs)
- optional trade patch: [OfficeResourceStoragePatchSystem.cs](./NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs)
- diagnostics: [OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs)

## Current Interpretation

Current evidence supports two distinct tracks:

- `Signature` phantom vacancy is a confirmed bug and the shipped guard fixes the reproduced case
- `software` instability is still plausible, still tracked, and still best treated as experimental mitigation rather than solved

That means the safest way to describe this release is:

- confirmed fix for the reproduced `Signature` phantom-vacancy symptom
- optional `software` trade patch
- built-in diagnostics for follow-up investigation

## Non-Goals

- faking office demand directly
- blanket vacancy overrides across every property type
- claiming the `software` track is solved without stronger evidence

## Docs For Contributors And Maintainers

- Contributors: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Maintainers and operators: [MAINTAINING.md](./MAINTAINING.md)
- Software evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- Software investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- Software evidence form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- Software comparison form: [`.github/ISSUE_TEMPLATE/software_comparison.yml`](./.github/ISSUE_TEMPLATE/software_comparison.yml)
