# No Office Demand Fix Analysis

## Scope

This document summarizes the current state of the `No Office Demand Fix` investigation in `Cities: Skylines II`.

The project currently covers two tracks:

- office-resource / `software` instability
- `Phantom Vacancy`

The goal of this document is to separate:

- what is confirmed by code
- what is confirmed by live diagnostics
- what is solved in the current version
- what is still open

## Track A: Office-Resource / `software` Instability

### Confirmed causal chain

The broad office-resource failure chain is still supported by code.

1. `software` is an office resource.
2. office companies depend on resource supply to keep the `LackResources` efficiency factor above `0`.
3. `ProcessingCompanySystem` can set `LackResources` to `0`.
4. `BuildingUtils` multiplies efficiency factors together, so `LackResources == 0` can collapse total efficiency to `0`.
5. if an office company's efficiency becomes `0`, production also becomes `0`.
6. office demand is derived from company production and resource demand counters, so a sustained `software` supply failure can collapse office demand indirectly.

### Confirmed special handling

The base game already contains partial virtual handling for office resources.

Confirmed examples from decompiled code:

- `TradeSystem` special-cases office resources even when they are not present in `StorageCompanyData.m_StoredResources`
- `ResourceBuyerSystem` supports virtual purchase behavior for zero-weight goods
- `ResourceExporterSystem` supports virtual export behavior
- company-side virtual storage behavior exists for office resources

This means the base game does appear to intend office-resource trade or buffering to work through partial exceptions.

### Where the implementation is still inconsistent

The office-resource path still appears inconsistent across systems.

The strongest current mismatches are:

- import seller discovery can still be gated by normal `m_StoredResources` checks
- provider visibility can still be gated by normal storage membership
- storage-company logic can still assume normal stored-resource participation
- storage-transfer flow can still assume office resources belong to ordinary storage membership rules

Current best interpretation:

- office resources were intended to work through special handling
- that handling is incomplete or inconsistently applied
- the current `software` issue is therefore still a plausible independent bug track

### Current status of the trade patch

The current prefab-level trade patch:

- augments outside connection and cargo station storage definitions with office resources
- remains useful as a mitigation or validation experiment
- is **not** yet a confirmed full fix for the `software` track

Current conclusion for Track A:

- `software` instability remains open
- current version does not claim to have solved it

## Track B: Phantom Vacancy

### Confirmed live observation

The currently reproduced save had occupied `Signature` properties that were still treated as market listings.

Confirmed corrected entities from the mod log on March 8, 2026:

- `EE_OfficeSignature01`
- `EE_OfficeSignature02`
- `IndustrialManufacturingSignature03`
- `IndustrialManufacturingSignature06`
- `IndustrialManufacturingSignature07`

Observed stale state:

- 4 occupied `Signature` properties still had `PropertyOnMarket`
- 1 occupied `Signature` industrial property still had `PropertyToBeOnMarket`

After the guard patch removed those stale market components, diagnostics reported:

- `onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0)`
- `phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=5)`
- `office building demand = 57`

Current interpretation:

- the inaccurate `Unoccupied Buildings` symptom in the reproduced save was caused by stale market state on occupied `Signature` properties
- the current patch fixed that reproduced symptom
- no non-signature phantom-vacancy case has been observed yet in current logs

### What the components mean

`PropertyOnMarket` is a market-listing component.

- it means the property is listed
- it does **not** guarantee that the property is truly vacant

`PropertyToBeOnMarket` is a staging component.

- it marks a property to be converted into `PropertyOnMarket`
- if it survives on an already occupied property, it can recreate the stale listing on the next property-processing pass

### Code-backed phantom-vacancy chain

The currently confirmed phantom-vacancy path is:

1. a `Signature` office or industrial property regains `PropertyToBeOnMarket` or keeps `PropertyOnMarket`
2. `PropertyProcessingSystem` converts `PropertyToBeOnMarket` into `PropertyOnMarket`
3. company property search still rejects the property if it already has a company renter
4. `IndustrialDemandSystem` counts the on-market office or industrial property as free supply anyway
5. office demand or industrial demand can be suppressed by vacancy that does not actually exist

### Relevant decompiled code

`PropertyOnMarket` is listing state, not vacancy state:

- `Game/Buildings/PropertyOnMarket.cs`

Occupied properties are not truly rentable:

- `Game/Buildings/PropertyUtils.cs`
- company property search checks the renter buffer and skips candidates that already have a company renter

Office and industrial demand counting trust on-market state too much:

- `Game/Simulation/IndustrialDemandSystem.cs`
- the free-property counting loop increments office or industrial free supply from `PropertyOnMarket`
- it does not apply the same renter guard that property search uses

`Signature` buildings already use a separate property lifecycle:

- `Game/Simulation/PropertyRenterSystem.cs`
- normal automatic relisting explicitly excludes `Signature` chunks with `!chunk.Has<Signature>()`

`Signature`-specific relisting paths can recreate stale market state:

- `Game/Serialization/RequiredComponentSystem.cs`
- `m_OldSignatureBuildingQuery` adds `PropertyToBeOnMarket` to old `Signature` buildings during deserialize without checking occupancy

- `Game/Tools/ApplyObjectsSystem.cs`
- `Signature` buildings can also regain `PropertyToBeOnMarket` there without checking current occupancy

### Why this still looks like a bug

The code strongly suggests that `Signature` buildings were intentionally given special handling.

That part looks deliberate.

What does **not** look deliberate is the final outcome:

- an occupied property is listed on the market
- actual company property search still rejects it
- demand systems still count it as free supply

That combination does not create real availability.
It only creates false vacancy.

The most defensible interpretation is:

- `Signature`-specific lifecycle handling was intentional
- occupancy invariant enforcement was incomplete across all `Signature` relisting paths
- the stale listing state is therefore more likely an implementation gap than a design goal

### Current patch interpretation

The implemented fix does not replace the base-game `Signature` lifecycle.

Instead it enforces one invariant after the known writers and before office or industrial property search and demand evaluation:

- an occupied `Signature` office or industrial property must not keep `PropertyOnMarket`
- an occupied `Signature` office or industrial property must not keep `PropertyToBeOnMarket`

This is a narrow safety correction, not a new market system.

## Current Best Interpretation

The two tracks are not equivalent in current release status.

Current ranking:

1. the current version successfully addresses the confirmed `Phantom Vacancy` case on occupied `Signature` office and industrial properties
2. `software` remains a plausible independent office-demand failure mode that is still unresolved
3. non-signature phantom-vacancy reproduction remains unconfirmed rather than disproven

## What Is Solved vs What Is Still Open

Solved in the current version:

- reproduced `Signature` phantom vacancy on occupied office and industrial properties

Still open:

- whether non-signature office properties can enter the same stale market state
- whether non-signature industrial properties can enter the same stale market state
- whether `software` trade or storage consistency is sufficient to resolve its track without additional runtime patches
- whether the prefab-level trade patch is enough or whether runtime trade/provider/storage patches are still needed
