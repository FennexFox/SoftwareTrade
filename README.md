# SoftwareTrade

`SoftwareTrade` is a Cities: Skylines II mod project focused on fixing the office-demand collapse commonly described as the `No Office Demand` bug.

## Problem Statement

The working hypothesis is:

- `software` is an office resource required by a large part of the office economy
- when `software` supply fails, affected office companies can lose `LackResources`
- that pushes their total efficiency to `0`
- production collapses
- office demand collapses as a downstream effect

The current code analysis suggests that the core issue is not simply "software cannot be traded", but that office-resource virtual trade and storage are implemented inconsistently across the base game's systems.

## Current Repository Status

This repository now has a first gameplay patch implementation.

- the mod project exists and loads as `SoftwareTrade`
- the old template settings scaffold has been replaced with mod-specific options
- a first runtime patch now augments office resources on outside connection and cargo station storage prefabs
- the next implementation target is to validate whether this prefab-level fix is sufficient or whether seller-discovery systems still need direct runtime patches

## Current Implementation

The current v0 patch does this:

1. register a one-shot ECS system during `MainLoop`
2. find prefab entities with `StorageCompanyData` plus `OutsideConnectionData` or `CargoTransportStationData`
3. add `Software`, `Telecom`, `Financial`, and `Media` to `m_StoredResources`
4. expose a setting toggle and verbose logging for this patch

This is a pragmatic first implementation because the current code analysis shows multiple game systems still gate office-resource import and storage through `m_StoredResources`.

## Planned Fix Strategy

The longer-term patch order is still:

1. validate whether the prefab augmentation alone restores stable office-resource imports
2. patch office-resource import seller discovery directly if the prefab fix is not sufficient
3. patch office-resource provider discovery directly if the prefab fix is still not sufficient
4. patch storage-transfer consistency only if the first steps still leave starvation in place

## Findings Summary

Confirmed so far:

- `software` is an office resource
- `LackResources == 0` can collapse building efficiency to `0`
- office production and demand calculations depend on that efficiency chain
- the base game already contains partial virtual handling for zero-weight office resources
- that handling appears inconsistent between trade logic and seller or provider discovery

Most likely technical cause:

- `TradeSystem` treats office resources as special
- but `ResourcePathfindSetup`, `ResourceAvailabilitySystem`, and parts of storage logic still gate them through `m_StoredResources`
- as a result, office-resource imports may exist in theory but fail in practice

## Docs

- [Analysis](./docs/softwaretrade-analysis.md)
- [Patch Plan](./docs/patch-plan.md)

## Project Layout

- [SoftwareTrade/Mod.cs](./SoftwareTrade/Mod.cs): mod entry point
- [SoftwareTrade/Setting.cs](./SoftwareTrade/Setting.cs): settings and localization
- [SoftwareTrade/Systems/OfficeResourceStoragePatchSystem.cs](./SoftwareTrade/Systems/OfficeResourceStoragePatchSystem.cs): one-shot prefab storage patch
- [docs/softwaretrade-analysis.md](./docs/softwaretrade-analysis.md): code-backed diagnosis
- [docs/patch-plan.md](./docs/patch-plan.md): implementation plan

## Non-Goals For The First Version

- faking office demand directly
- blanket vacancy overrides
- large balance changes unrelated to office-resource supply

## Next Step

Run in-game validation against office-heavy cities, then decide whether `ResourcePathfindSetup` and `ResourceAvailabilitySystem` still need direct runtime patches.
