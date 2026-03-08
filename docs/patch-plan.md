# SoftwareTrade Patch Plan

## Goal

Implement the smallest gameplay patch that makes office-resource trade and buffering behave consistently enough to prevent the `software -> efficiency 0 -> office demand collapse` chain.

This plan is based on the findings in [softwaretrade-analysis.md](./softwaretrade-analysis.md).

## Current Diagnosis

The base game already contains partial support for virtual office-resource trade and storage, but the implementation is inconsistent.

The main mismatch is:

- some systems treat office resources as special virtual goods
- other systems still require them to exist in `StorageCompanyData.m_StoredResources`

The most likely failure point is not production itself, but the path from "office resource is theoretically tradable" to "buyers can actually find and use import sellers reliably".

## Patch Scope

### In scope for v0

- augment prefab stored-resource definitions for office resources on import-capable storage prefabs
- add temporary diagnostics to confirm office-resource import behavior
- keep changes as local and reversible as possible

### Out of scope for v0

- broad economy rebalance
- office demand cheats or forced demand injection
- direct vacancy overrides
- rewriting every storage or transport system

## Current v0 Implementation

The repository now contains a first implementation that takes the broader prefab-data route first.

Implemented behavior:

- a one-shot ECS system runs after the game world is available
- it finds prefab entities that have `StorageCompanyData` plus `OutsideConnectionData`
- it also finds prefab entities that have `StorageCompanyData` plus `CargoTransportStationData`
- it augments `m_StoredResources` with `Software`, `Telecom`, `Financial`, and `Media`
- the patch can be turned off, and verbose logging can be enabled from the mod settings

Why this came first:

- the current bug hypothesis points to several separate systems using the same `m_StoredResources` gate
- patching the prefab definition first is the smallest way to test all of those gates together without multiple Harmony or IL patches

## Implementation Phases

## Phase 1: Validate prefab augmentation

### Target

- outside connection prefabs
- cargo station prefabs

### Problem

Several known trade, provider, and storage systems still gate office resources through `m_StoredResources`.

### Patch concept

Augment `StorageCompanyData.m_StoredResources` for import-capable prefab entities so all of those systems see office resources as allowed.

### Success condition

- software imports become reachable in actual gameplay
- office-resource starvation is reduced without any direct seller-discovery patch

## Phase 2: Restore import reachability directly if Phase 1 is not enough

### Target

- `Game/Simulation/ResourcePathfindSetup`

### Problem

Import sellers currently require the resource to be present in `m_StoredResources`, even though `TradeSystem` already allows office resources as a special case.

### Patch concept

Treat office resources as valid import-seller resources when the candidate entity is:

- an outside connection
- a cargo station
- another storage-style import provider that already participates in import flow

### Success condition

When an office company tries to buy `software`, an outside connection should no longer be filtered out solely because the prefab does not list `software` in `m_StoredResources`.

## Phase 3: Restore provider visibility directly if Phase 2 is not enough

### Target

- `Game/Simulation/ResourceAvailabilitySystem`

### Problem

Provider lookup still hides storage-style office-resource sellers unless the resource exists in `m_StoredResources`.

### Patch concept

Mirror the same office-resource exception used in Phase 1.

### Success condition

Systems that rely on availability queries should see valid office-resource providers, especially outside connections.

## Phase 4: Verify whether storage transfer still blocks the fix

### Targets

- `Game/Simulation/StorageCompanySystem`
- `Game/Simulation/StorageTransferSystem`

### Problem

Even if sellers become discoverable, storage balancing may still reject office resources because of the same `m_StoredResources` assumptions.

### Patch concept

Apply a targeted exception for office resources only where the old storage gate blocks import buffering or outside-connection transfer.

### Success condition

Office-resource import and buffering should remain stable over time instead of oscillating between short spikes and full starvation.

## Phase 5: Optional deeper runtime patch

### Targets

- `Game/Simulation/ResourcePathfindSetup`
- `Game/Simulation/ResourceAvailabilitySystem`
- `Game/Simulation/StorageCompanySystem`
- `Game/Simulation/StorageTransferSystem`

### Use only if needed

If prefab augmentation is still too broad or not sufficient, replace it with narrower runtime exceptions in the affected systems.

## Concrete Code Tasks

1. Validate the current prefab augmentation in-game.
2. Keep the setting toggle and verbose logging while testing.
3. Patch `ResourcePathfindSetup` only if imports are still unreachable.
4. Patch `ResourceAvailabilitySystem` only if provider visibility is still wrong.
5. Re-test before touching `StorageCompanySystem`.
6. Only patch `StorageCompanySystem` and `StorageTransferSystem` if logs show sellers are found but stock flow still collapses.

## Logging Plan

The first gameplay iteration should ship with lightweight diagnostics that can be disabled later.

Current logs:

- number of outside connection prefabs patched
- number of cargo station prefabs patched
- optional per-prefab patch logs when verbose logging is enabled

Recommended next logs if Phase 1 is not sufficient:

- office resource requested
- seller candidate accepted or rejected
- rejection reason: `m_StoredResources` gate
- seller type: local producer, outside connection, cargo station, storage company
- resulting sale flags: `Virtual`, `ImportFromOC`
- buyer stock before and after purchase

## Test Scenarios

## Scenario 1: Fresh city with offices unlocked

Check:

- does office demand continue to appear after offices start consuming software
- do office buildings avoid long runs of zero efficiency caused by `LackResources`

## Scenario 2: Existing save already showing low or zero office demand

Check:

- do software imports resume
- do some offices recover efficiency without bulldozing or rezoning
- does office demand rebound after simulation catches up

## Scenario 3: Stress case with high office concentration

Check:

- whether software still oscillates violently month to month
- whether import and export both remain active without runaway stock growth

## Risks

- treating office resources as normal storage goods in too many places could create side effects in cost balancing or routing
- patching only one discovery system may produce partial improvement but still leave starvation in place
- a prefab-data workaround may mask the underlying inconsistency and make future debugging harder

## Decision Rules

Use these rules while implementing:

1. Prefer the smallest patch that restores consistency with existing office-resource special cases.
2. Avoid direct demand manipulation unless the supply-path fix clearly fails.
3. Avoid vacancy hacks unless the code later proves there is a separate vacancy-state bug.
4. Keep logging until seller discovery and stock flow are clearly behaving as expected.

## Definition Of Done

The first usable version of `SoftwareTrade` is done when all of the following are true:

- office-resource imports can be observed and are not blocked by `m_StoredResources`
- software starvation is materially reduced in normal gameplay
- affected office buildings recover from `LackResources`-driven efficiency collapse
- office demand recovers without a separate artificial demand patch
- the patch does not obviously break non-office resource trade
