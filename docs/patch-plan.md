# SoftwareTrade Patch Plan

## Goal

Implement the smallest gameplay fix that addresses the real driver of the `No Office Demand` symptom in a given save.

The current investigation no longer assumes that `software` trade is the only primary cause. The mod now has two active diagnostic and fix tracks:

- Track A: office-resource trade and storage consistency
- Track B: stale `PropertyOnMarket` on occupied office or industrial properties

This plan is based on the findings in [softwaretrade-analysis.md](./softwaretrade-analysis.md).

## Current Diagnosis

Two different failure modes now look relevant.

### Track A: office-resource trade or storage inconsistency

The base game already contains partial support for virtual office-resource trade and storage, but the implementation is inconsistent.

The main mismatch is:

- some systems treat office resources as special virtual goods
- other systems still require them to exist in `StorageCompanyData.m_StoredResources`

This can still explain `software` starvation, `LackResources == 0`, and office efficiency collapse.

### Track B: stale `PropertyOnMarket` on occupied office properties

The new diagnostics show a second bug track that is separate from software supply.

Observed live examples:

- `EE_OfficeSignature01`
- `EE_OfficeSignature02`

Observed contradiction:

- `PropertyOnMarket == true`
- `propertyCount == 1`
- `active company renters == 1`
- the same entities are still counted as free office supply

This can hold `officeBuildingDemand` at `0` even when no truly vacant office building exists.

## Patch Scope

### In scope for the next investigation

- keep the current prefab-level trade patch available as a toggleable experiment
- keep diagnostics focused on both office-resource supply and property-market state
- determine whether stale `PropertyOnMarket` is currently limited to `SignatureOffice` or also affects non-signature office or industrial properties
- determine whether the correct fix belongs in listing lifecycle or in demand counting safeguards

### Out of scope for this stage

- broad economy rebalance
- fake office demand injection
- blanket vacancy overrides
- rewriting every storage or transport system before the main driver is confirmed

## Current v0 Implementation

The repository currently contains two things:

1. a prefab-level trade patch
2. office-demand diagnostics

Implemented behavior:

- a one-shot ECS system augments `m_StoredResources` with `Software`, `Telecom`, `Financial`, and `Media` on outside connection and cargo station prefabs
- diagnostics log office demand factors, software office efficiency, and occupied-on-market office properties
- both the trade patch and diagnostics can be toggled in mod settings

Interpretation:

- the trade patch is now best treated as a partial mitigation or validation tool
- it should not be treated as the single confirmed fix path while the stale `PropertyOnMarket` issue remains unresolved

## Implementation Phases

## Track A: Office-Resource Trade And Storage Consistency

### Phase A1: Validate the existing prefab augmentation

Target:

- outside connection prefabs
- cargo station prefabs

Question:

- does the current patch materially improve office-resource availability in real gameplay

Success condition:

- software imports become reachable
- software starvation is reduced
- office efficiency collapses caused by `LackResources` become less frequent

### Phase A2: Restore import reachability directly if prefab augmentation is not enough

Target:

- `Game/Simulation/ResourcePathfindSetup`

Problem:

- import sellers still require the resource to be present in `m_StoredResources`

Patch concept:

- treat office resources as valid import-seller resources under the same special-case logic already used by `TradeSystem`

### Phase A3: Restore provider visibility directly if needed

Target:

- `Game/Simulation/ResourceAvailabilitySystem`

Problem:

- provider lookup still hides office-resource sellers behind the normal storage gate

Patch concept:

- mirror the office-resource exception in availability queries

### Phase A4: Verify whether storage-transfer logic still blocks stable supply

Targets:

- `Game/Simulation/StorageCompanySystem`
- `Game/Simulation/StorageTransferSystem`

Problem:

- even if sellers become visible, storage balancing may still reject office resources through normal storage assumptions

Patch concept:

- add targeted office-resource exceptions only where the old gate still breaks buffering

## Track B: Stale `PropertyOnMarket` On Occupied Office Or Industrial Properties

### Phase B1: Confirm where occupied properties keep or regain `PropertyOnMarket`

Primary code targets:

- `Game/Simulation/PropertyProcessingSystem`
- `Game/Simulation/RentAdjustSystem`
- `Game/Simulation/CompanyMoveAwaySystem`
- signature-related property flow

Question:

- which path leaves a property on market after it already has an active renter again

Expected evidence:

- a property with active renter count equal to `CountProperties()` still retains `PropertyOnMarket`

### Phase B2: Confirm reproduction scope

Question:

- does the issue reproduce only on `SignatureOffice`, or also on:
  - non-signature office properties
  - non-office industrial properties

Current status:

- current live confirmation only covers `SignatureOffice`
- non-office reproducibility is plausible but not yet confirmed

### Phase B3: Choose the actual fix once scope is confirmed

Choose between:

- fixing stale listing creation or removal in the property lifecycle
- hardening office or industrial demand counting so stale on-market properties are ignored when they already have an active company renter

Decision rule:

- prefer lifecycle cleanup if the stale listing is clearly wrong and isolated
- prefer demand-count hardening if stale listing can still occur transiently or from multiple systems

## Logging Plan

Current evidence comes from these diagnostic fields:

- `onMarketOfficeProperties(total, activelyVacant, occupied, staleRenterOnly)`
- free software office property detail lines
- on-market office property detail lines

Recommended next diagnostics if Track B continues:

- same occupied-on-market checks for non-office industrial properties
- explicit signature vs non-signature breakdown
- which system most recently re-added `PropertyOnMarket` if practical to instrument

Recommended next diagnostics if Track A continues:

- office resource requested
- seller candidate accepted or rejected
- rejection reason: `m_StoredResources` gate
- seller type: local producer, outside connection, cargo station, storage company

## Test Scenarios

## Scenario 1: Save showing office demand stuck at zero

Check:

- are there occupied-on-market office properties
- are they currently `SignatureOffice` only or not
- does `officeBuildingDemand` stay at `0` while `officeCompanyDemand` remains positive

## Scenario 2: Software-supply stress case

Check:

- does software still oscillate or collapse
- do offices still hit `efficiencyZero` or `lackResourcesZero`
- does the trade patch change that behavior materially

## Scenario 3: Industrial comparison

Check:

- do occupied-on-market industrial properties exist under the same conditions
- if yes, does industrial building demand show the same suppression pattern

## Decision Note

If stale occupied-on-market properties are confirmed to be the main driver of office-demand collapse, demand or property handling becomes a higher priority than further trade patches.

The trade patch should then be treated as a separate mitigation for software starvation, not as the main office-demand fix.

## Definition Of Done

The next usable version of `SoftwareTrade` is ready only when all of the following are true:

- documentation and diagnostics clearly separate the software-supply bug track from the stale `PropertyOnMarket` bug track
- the mod can tell whether the current save is blocked mainly by supply failure, stale market listings, or both
- the chosen fix target is decision-complete:
  - listing lifecycle cleanup
  - demand-count hardening
  - or trade-path consistency
- non-office reproducibility is either confirmed or explicitly left documented as unconfirmed
