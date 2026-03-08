# SoftwareTrade

`SoftwareTrade` is a Cities: Skylines II mod project focused on investigating and fixing the office-demand collapse commonly described as the `No Office Demand` bug.

## Problem Statement

The project now tracks two active bug paths.

### 1. Office-resource supply instability

- `software` is an office resource required by a large part of the office economy
- when `software` supply fails, affected office companies can lose `LackResources`
- that pushes total efficiency to `0`
- production collapses
- office demand can collapse as a downstream effect

### 2. Phantom free office properties

Current diagnostics also show a separate issue:

- some office properties remain flagged with `PropertyOnMarket`
- those same properties can already have an active company renter
- `IndustrialDemandSystem` still counts them as free office supply
- office building demand can stay at `0` even when no truly vacant office building exists

The current live examples are `SignatureOffice` properties, so the repo now distinguishes:

- generic office or industrial hypotheses
- currently confirmed `SignatureOffice` cases

## Current Repository Status

This repository now contains:

- a first gameplay patch that augments office resources on outside connection and cargo station storage prefabs
- a diagnostics system that logs office demand factors, software office efficiency, and occupied-on-market office properties
- documentation that treats software supply and stale `PropertyOnMarket` as separate but potentially interacting bug tracks

Important interpretation:

- the current trade patch is best treated as a partial mitigation or validation experiment
- it is not yet the single confirmed fix path for the office-demand collapse

## Current Implementation

The current v0 patch does this:

1. register a one-shot ECS system during `MainLoop`
2. find prefab entities with `StorageCompanyData` plus `OutsideConnectionData` or `CargoTransportStationData`
3. add `Software`, `Telecom`, `Financial`, and `Media` to `m_StoredResources`
4. expose toggles for the trade patch and diagnostics
5. log office-demand diagnostics including:
   - `onMarketOfficeProperties(total, activelyVacant, occupied, staleRenterOnly)`
   - free software office property detail lines
   - on-market office property detail lines

## Findings Summary

Confirmed so far:

- `software` is an office resource
- `LackResources == 0` can collapse building efficiency to `0`
- the base game already contains partial virtual handling for zero-weight office resources
- that office-resource handling is inconsistent across trade, provider, and storage systems
- `PropertyOnMarket` is a market-listing component, not a guaranteed vacancy state
- occupied office properties can remain on market and still be counted as free office supply

Refined interpretation:

- software starvation is still a real office-economy problem
- but phantom free office properties can independently hold office demand at zero
- the currently observed live stale-listing cases are `SignatureOffice` properties

## Planned Fix Strategy

The work is now split into two tracks.

### Track A: office-resource trade and storage consistency

1. validate whether prefab augmentation materially improves software availability
2. patch import seller discovery directly if needed
3. patch provider discovery directly if needed
4. patch storage-transfer consistency only if still required

### Track B: stale `PropertyOnMarket` on occupied office or industrial properties

1. identify where occupied properties keep or regain `PropertyOnMarket`
2. compare `PropertyProcessingSystem`, `RentAdjustSystem`, `CompanyMoveAwaySystem`, and signature-related property flow
3. confirm whether the issue is limited to `SignatureOffice` or also affects non-signature office or industrial properties
4. choose between:
   - fixing stale listing creation or removal
   - hardening office or industrial demand counting against stale on-market properties

## Docs

- [Analysis](./docs/softwaretrade-analysis.md)
- [Patch Plan](./docs/patch-plan.md)

## Project Layout

- [SoftwareTrade/Mod.cs](./SoftwareTrade/Mod.cs): mod entry point
- [SoftwareTrade/Setting.cs](./SoftwareTrade/Setting.cs): settings and localization
- [SoftwareTrade/Systems/OfficeResourceStoragePatchSystem.cs](./SoftwareTrade/Systems/OfficeResourceStoragePatchSystem.cs): one-shot prefab storage patch
- [SoftwareTrade/Systems/OfficeDemandDiagnosticsSystem.cs](./SoftwareTrade/Systems/OfficeDemandDiagnosticsSystem.cs): office demand and property-market diagnostics
- [docs/softwaretrade-analysis.md](./docs/softwaretrade-analysis.md): code-backed diagnosis
- [docs/patch-plan.md](./docs/patch-plan.md): implementation plan

## Non-Goals For The First Version

- faking office demand directly
- blanket vacancy overrides
- large balance changes unrelated to the confirmed bug tracks

## Next Step

Use the current diagnostics to determine whether the stale listing issue is:

- limited to `SignatureOffice`
- also present in non-signature office properties
- also present in non-office industrial properties

That result should decide whether the next fix belongs in property-listing lifecycle handling, demand-count safeguards, or office-resource trade consistency.
