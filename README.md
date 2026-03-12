# No Office Demand Fix

`No Office Demand Fix` is a Cities: Skylines II mod focused on one confirmed office-demand failure track and one separate software/resource investigation track:

- `Phantom Vacancy`: occupied properties that are still counted as market listings
- office-resource / `software` instability: a separate producer/consumer resource-flow investigation that can still collapse office-company efficiency

The current release ships a confirmed fix for the reproduced `Signature` phantom-vacancy case and keeps the `software` track available as opt-in investigation tooling rather than a finished end-user feature.

## Current Release

What the current code does:

- fixes stale `PropertyOnMarket` and `PropertyToBeOnMarket` state on occupied `Signature` office and industrial properties before demand and property search evaluate them
- includes an opt-in prefab-level office-resource trade patch for outside connections and cargo stations when maintainers need software-track comparison data; setting changes are picked up on the next city/save load without a full restart in the normal case, but a restart is recommended for clean comparison runs if other mods may also modify the same storage resource definitions
- includes opt-in diagnostics:
  - covers office demand, phantom vacancy, and `software` producer/consumer office health
  - uses need-selection and buyer-lifecycle signal to help distinguish upstream input pressure, downstream software-consumer shortage, and consumer buyer-state anomalies

What it does not claim:

- it is not a proven fix for every `No Office Demand` case
- the `software` track is still under investigation
- non-signature phantom vacancy is still monitored, not ruled out

## Settings

Current defaults from [Setting.cs](./NoOfficeDemandFix/Setting.cs):

| Setting | Default | Purpose |
| --- | --- | --- |
| `EnableTradePatch` | `false` | Adds office resources to outside connection and cargo station storage definitions. Changes apply on the next city/save load without a full game restart in the normal case. For clean comparison runs, restart first if other mods may also modify the same storage resource definitions. |
| `EnablePhantomVacancyFix` | `true` | Enables the shipped guard that removes stale market state from occupied `Signature` office and industrial properties. Applies immediately to future simulation ticks; disabling it stops future corrections but does not restore already cleaned-up market state. |
| `EnableDemandDiagnostics` | `false` | Live-applies office-demand, phantom-vacancy, and `software` producer/consumer diagnostics when the state looks suspicious. Leave it off unless you are collecting evidence. |
| `DiagnosticsSamplesPerDay` | `2` | Sets how many scheduled diagnostic sample slots exist per displayed in-game day. `sample_slot` follows the runtime `TimeSystem` time-of-day path, while `sample_day` uses a logical displayed-clock day that is seeded from the runtime day value and advances when the sampled slot wraps at midnight. Emitted `observation_window(...)` lines report the slot they actually sampled, `clock_source` is normally `runtime_time_system`, `sample_count` counts emitted observations in the current run, and `skipped_sample_slots` reports scheduled gaps that were not backfilled. |
| `CaptureStableEvidence` | `false` | Keeps bounded scheduled `softwareEvidenceDiagnostics observation_window(...)` lines flowing at the configured per-day cadence while diagnostics are enabled, even when the city looks stable. Use it only for baseline or no-symptom evidence collection. |
| `VerboseLogging` | `false` | Adds the noisier correction and patch traces and also forces diagnostics output at the configured per-day cadence while diagnostics are enabled. Use it only for investigation. |

## Implementation

- `Signature` phantom-vacancy fix: [SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs)
- optional trade patch: [OfficeResourceStoragePatchSystem.cs](./NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs)
- diagnostics: [OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs)

## Current Interpretation

Current evidence supports two distinct tracks:
- `software` instability is still plausible and still tracked. Current evidence suggests it is best treated as investigation tooling with diagnostics and an optional trade patch for comparison runs rather than a solved user-facing fix.
- `Signature` phantom vacancy is a confirmed bug and the shipped guard fixes the reproduced case
Current `software`-track diagnostics are meant to help separate upstream input pressure from downstream software-consumer shortage or office-resource trade bottlenecks. In particular, the current investigation focuses on explaining why some zero-software consumers retain trade-cost cache entries while still showing no active buyer, trip, current-trading, or path state.

Current evidence also does not support treating widespread `software` consumer efficiency collapse as a direct proxy for lower office demand. Demand response still has to be captured directly from the office-demand counters rather than inferred from software-office distress alone.

Current same-save current-build evidence also does not show clear immediate trade-patch mitigation through day 22. The optional trade patch remains useful as a comparison switch, but it is not currently described as a proven fix for the sampled software-consumer buyer-state anomaly.

When code reading matters, the base-game lifecycle claims should come from vanilla decompiled game code. This mod's own code is the source of truth for runtime patch behavior and emitted diagnostics, not for every vanilla trade-path assumption.

That means the safest way to describe this release is:

- confirmed fix for the reproduced `Signature` phantom-vacancy symptom
- opt-in `software` trade patch for comparison runs
- opt-in diagnostics for follow-up investigation

## Non-Goals

- faking office demand directly
- blanket vacancy overrides across every property type
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
