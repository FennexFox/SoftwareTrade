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
| `VerboseLogging` | `false` | Logs every prefab patched, every phantom-vacancy correction, and forces daily diagnostics while diagnostics are enabled. |

## Implementation

The runtime behavior maps directly to three systems:

1. [OfficeResourceStoragePatchSystem](./NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs)
   Patches outside connection and cargo station `StorageCompanyData` to include office resources such as `Software`, `Telecom`, `Financial`, and `Media`.
2. [SignaturePropertyMarketGuardSystem](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs)
   Removes stale market-listing components from occupied `Signature` office and industrial properties.
3. [OfficeDemandDiagnosticsSystem](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs)
   Logs demand factors, on-market and occupied property counts, phantom-vacancy counters, guard corrections, and `software` office efficiency signals.

## Current Interpretation

Current evidence supports two distinct tracks:

- `Signature` phantom vacancy is a confirmed bug and the shipped guard fixes the reproduced case
- `software` instability is still plausible, still tracked, and still best treated as experimental mitigation rather than solved

That means the safest way to describe this release is:

- confirmed fix for the reproduced `Signature` phantom-vacancy symptom
- optional `software` trade patch
- built-in diagnostics for follow-up investigation

## Documentation

- [Analysis](./docs/no-office-demand-fix-analysis.md)
- [Patch Plan](./docs/no-office-demand-fix-plan.md)

## Maintainer Notes

This repository uses a local self-hosted runner for release automation. See `actions-runner/README.md`.

## Project Layout

- [NoOfficeDemandFix/Mod.cs](./NoOfficeDemandFix/Mod.cs): mod entry point and system registration
- [NoOfficeDemandFix/Setting.cs](./NoOfficeDemandFix/Setting.cs): settings and localization
- [NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs](./NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs): optional office-resource trade patch
- [NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs): shipped `Signature` phantom-vacancy fix
- [NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs): office-demand, phantom-vacancy, and `software` diagnostics

## Non-Goals

- faking office demand directly
- blanket vacancy overrides across every property type
- claiming the `software` track is solved without stronger evidence
