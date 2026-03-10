# No Office Demand Fix

`No Office Demand Fix` is a Cities: Skylines II mod focused on one confirmed office-demand failure track and one separate software/resource investigation track:

- `Phantom Vacancy`: occupied properties that are still counted as market listings
- office-resource / `software` instability: a separate producer/consumer resource-flow investigation that can still collapse office-company efficiency

The current release ships a confirmed fix for the reproduced `Signature` phantom-vacancy case and keeps the `software` track available as opt-in investigation tooling rather than a finished end-user feature.

## Current Release

What the current code does:

- fixes stale `PropertyOnMarket` and `PropertyToBeOnMarket` state on occupied `Signature` office and industrial properties before demand and property search evaluate them
- includes an opt-in prefab-level office-resource trade patch for outside connections and cargo stations when maintainers need software-track comparison data
- includes opt-in diagnostics for office demand, phantom vacancy, and `software` producer/consumer office health, including enough signal to help distinguish upstream input pressure, downstream software-consumer shortage, and consumer trade-state anomalies

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
| `EnableDemandDiagnostics` | `false` | Live-applies office-demand, phantom-vacancy, and `software` producer/consumer diagnostics when the state looks suspicious. Leave it off unless you are collecting evidence. |
| `DiagnosticsSamplesPerDay` | `2` | Sets how many `softwareEvidenceDiagnostics` samples are emitted per displayed in-game day while diagnostics are active. |
| `CaptureStableEvidence` | `false` | Keeps bounded `softwareEvidenceDiagnostics` windows flowing at the configured per-day cadence while diagnostics are enabled, even when the city looks stable. Use it only for baseline or no-symptom evidence collection. |
| `VerboseLogging` | `false` | Adds the noisier correction and patch traces and also forces diagnostics output at the configured per-day cadence while diagnostics are enabled. Use it only for investigation. |

## Implementation

- `Signature` phantom-vacancy fix: [SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs)
- optional trade patch: [OfficeResourceStoragePatchSystem.cs](./NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs)
- diagnostics: [OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs)

## Current Interpretation

Current evidence supports two distinct tracks:

- `Signature` phantom vacancy is a confirmed bug and the shipped guard fixes the reproduced case
- `software` instability is still plausible, still tracked, and still best treated as investigation tooling plus experimental mitigation rather than solved

Current `software`-track diagnostics are meant to help separate upstream input pressure from downstream software-consumer shortage or office-resource trade bottlenecks, but the track remains investigational rather than proven.

Current evidence also does not support treating widespread `software` consumer efficiency collapse as a direct proxy for lower office demand. Demand response still has to be captured directly from the office-demand counters rather than inferred from software-office distress alone.

When code reading matters, the base-game lifecycle claims should come from vanilla decompiled game code. This mod's own code is the source of truth for runtime patch behavior and emitted diagnostics, not for every vanilla trade-path assumption.

That means the safest way to describe this release is:

- confirmed fix for the reproduced `Signature` phantom-vacancy symptom
- opt-in `software` trade patch for comparison runs
- opt-in diagnostics for follow-up investigation

## Non-Goals

- faking office demand directly
- blanket vacancy overrides across every property type
- claiming the `software` track is solved without stronger evidence

## Docs for Contributors and Maintainers

- Contributors: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Maintainers and operators: [MAINTAINING.md](./MAINTAINING.md)
- Software evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- Software investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- Software evidence form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- Software investigation umbrella form: [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./.github/ISSUE_TEMPLATE/software_investigation.yml)
