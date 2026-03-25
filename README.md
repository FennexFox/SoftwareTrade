# No Office Demand Fix

`No Office Demand Fix` is a Cities: Skylines II mod with shipped office fixes
plus a separate experimental software-troubleshooting toolset:

- `Phantom Vacancy`: occupied properties that are still counted as market listings
- `Office AI chunk iteration`: office stock consumption and virtual export should
  not stop at the first low-stock office in a chunk
- office-resource / `software` instability: experimental import seller and buyer fallbacks plus optional diagnostics for remaining virtual-resource stalls

The current release ships the confirmed `Signature` phantom-vacancy fix and
an always-on office AI hotfix, and also includes enabled-by-default
experimental software import seller and buyer fallbacks. Diagnostics remain available for
troubleshooting but are disabled by default. Optional coarse performance
telemetry is also available for before/after steady-state and stall
comparisons and is disabled by default. It keeps the broader `software`
track under investigation.

This release also restores the older `2x` office resource-demand baseline
with a direct Harmony patch instead of keeping the newer vanilla `3x`
multiplier, so office-demand comparisons remain compatible with earlier
evidence gathered before that vanilla change.

## Current Release

What the current code does:

- fixes stale `PropertyOnMarket` and `PropertyToBeOnMarket` state on occupied `Signature` office and industrial properties before demand and property search evaluate them
- hotfixes the vanilla office AI loop so one low-stock office no longer prevents later offices in the same chunk from consuming output and queuing virtual exports
- restores the pre-hotfix office demand baseline for office resources with a direct Harmony patch, so office-demand comparisons stay on the older `2x` basis rather than vanilla's newer `3x`
- includes an experimental Harmony-based outside-connection virtual seller correction that appends active outside connections reporting stock for office virtual-resource imports when the vanilla seller pass filtered them out because the prefab storage mask does not list that virtual resource
- includes an experimental virtual office buyer timing correction that adds a narrow post-vanilla fallback buyer for zero-weight office inputs when a company still has no buyer, path, trip, or current-trading state
- keeps diagnostics available for office demand, phantom vacancy, and `software` producer/consumer office state when you turn them on for troubleshooting
- retires the earlier office-resource storage patch experiment because zero-weight office resources do not fit the current vanilla virtual-resource architecture

What it does not claim:

- it is not a proven fix for every `No Office Demand` case
- broader software-related office/resource stalls are still under investigation and are not presented as a confirmed fix
- non-signature phantom vacancy is still monitored, not ruled out

## Settings

Current defaults from [Setting.cs](./NoOfficeDemandFix/Setting.cs):

| Setting | Default | Purpose |
| --- | --- | --- |
| `EnablePhantomVacancyFix` | `true` | Enables the shipped guard that removes stale market state from occupied `Signature` office and industrial properties. Applies immediately to future simulation ticks; disabling it stops future corrections but does not restore already cleaned-up market state. |
| `EnableOutsideConnectionVirtualSellerFix` | `true` | Enables the default experimental software import seller fallback. Takes effect on the next game launch. It only appends active outside connections that already report stock for the requested office virtual-resource import but were filtered out by the prefab storage mask, and it does not change cargo or storage definitions. |
| `EnableVirtualOfficeResourceBuyerFix` | `true` | Enables the default experimental software import buyer fallback. It adds a narrow fallback `ResourceBuyer` for zero-weight office inputs such as `Software` when a company is below the low-stock threshold but still has no buyer/path/trip/current-trading state. |
| `EnableOfficeDemandDirectPatch` | `true` | Restores the pre-1.5.6f1 office demand baseline with the shipped direct Harmony patch. Keep it on for the current release unless you are intentionally comparing against the newer vanilla `3x` baseline. |
| `EnableDemandDiagnostics` | `false` | Logs office-demand, phantom-vacancy, and `software` office-state details when the simulation looks suspicious. Disabled by default; turn it on for troubleshooting, or leave it off for quieter logs. |
| `DiagnosticsSamplesPerDay` | `2` | Sets how many scheduled diagnostic samples run per displayed in-game day while diagnostics are enabled. Higher values produce denser logs. |
| `CaptureStableEvidence` | `false` | Keeps scheduled software diagnostics running even when the city looks stable. Use it only when you want baseline logs for troubleshooting. |
| `VerboseLogging` | `false` | Adds noisier correction traces and supplemental office-trade detail lines, and forces diagnostics output at the configured cadence while it is on. Use it only when you want detailed troubleshooting logs. |
| `EnablePerformanceTelemetry` | `false` | Captures coarse in-memory performance summaries and stall events, then writes `perf_summary.csv` and `perf_stalls.csv` when the current session ends. Keep it off unless you are intentionally measuring performance. |
| `PerformanceTelemetrySamplingIntervalSec` | `1.0` | Controls the coarse wall-clock summary window for performance telemetry in seconds. Lower values produce denser CSV output and slightly more telemetry overhead. |
| `PerformanceTelemetryStallThresholdMs` | `250` | Defines the render-latency threshold used to start and end coarse stall-event tracking. |

## Implementation

- `Signature` phantom-vacancy fix: [SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs)
- office AI hotfix: [OfficeAIHotfixPatch.cs](./NoOfficeDemandFix/Patches/OfficeAIHotfixPatch.cs), [OfficeAIHotfixSystem.cs](./NoOfficeDemandFix/Systems/OfficeAIHotfixSystem.cs)
- office demand direct patch: [IndustrialDemandOfficeBaselinePatch.cs](./NoOfficeDemandFix/Patches/IndustrialDemandOfficeBaselinePatch.cs)
- outside-connection virtual seller patch: [OutsideConnectionVirtualSellerFixPatch.cs](./NoOfficeDemandFix/Patches/OutsideConnectionVirtualSellerFixPatch.cs)
- virtual office buyer cadence fix: [VirtualOfficeResourceBuyerFixSystem.cs](./NoOfficeDemandFix/Systems/VirtualOfficeResourceBuyerFixSystem.cs)
- diagnostics: [OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs)
- performance telemetry: [PerformanceTelemetrySystem.cs](./NoOfficeDemandFix/Systems/PerformanceTelemetrySystem.cs), [PerformanceTelemetryCollector.cs](./NoOfficeDemandFix/Telemetry/PerformanceTelemetryCollector.cs)

## Current Status

The safest repository-facing summary of the current release is:

- confirmed fix for the reproduced `Signature` phantom-vacancy symptom
- confirmed fix for the office AI chunk-iteration abort on low stock
- shipped comparability rollback for the pre-hotfix office demand baseline via a direct Harmony patch
- default experimental software import seller and buyer fallbacks; diagnostics remain available but disabled by default
- retired office-resource storage patch experiment
- broader software-related office/resource stalls remain under investigation
- office-demand/global-sales undercount remains a separate follow-up line rather than part of the current runtime corrections

Current evidence does not support treating `software` producer or consumer
distress as direct proof of lower office demand by itself. The `software` path
remains investigational. The experimental settings only address a narrow
outside-connection seller fallback and a narrow buyer-timing gap; they do not
reintroduce cargo or storage physicalization.

## Non-Goals

- faking office demand directly
- blanket vacancy overrides across every property type
- pushing zero-weight office resources through cargo/storage definitions as if they were physical goods
- claiming the `software` track is solved without stronger evidence

## Planning And Tracking

Active work is tracked in the public project board [NoOfficeDemandFix Tracker](https://github.com/users/FennexFox/projects/7).
For the operating rules behind that board, start with [MAINTAINING.md](./MAINTAINING.md).

## Reporting And Telemetry

For coarse performance comparisons, start with
[PERF_REPORTING.md](./PERF_REPORTING.md). Turn on
`EnablePerformanceTelemetry`, and keep
`PerformanceTelemetrySamplingIntervalSec` and
`PerformanceTelemetryStallThresholdMs` the same across a direct baseline /
comparison pair.

If you want to submit a raw diagnostics log for maintainer triage or later
promotion into a normalized evidence issue, start with
[LOG_REPORTING.md](./LOG_REPORTING.md). Turn on
`EnableDemandDiagnostics` first, because diagnostics are not enabled by
default in the current release.

## Contributor And Maintainer Docs

- Contributor workflow: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Maintainer and release workflow: [MAINTAINING.md](./MAINTAINING.md)
- Performance telemetry reporting: [PERF_REPORTING.md](./PERF_REPORTING.md)
- Software evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- Software investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- Software evidence issue form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)
- Software investigation issue form: [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./.github/ISSUE_TEMPLATE/software_investigation.yml)
