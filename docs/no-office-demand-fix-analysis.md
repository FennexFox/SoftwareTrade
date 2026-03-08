# Phantom Vacancy Analysis

## Scope

This document summarizes the currently confirmed `Phantom Vacancy` issue in `Cities: Skylines II`.

This revision is intentionally scoped to:

- stale `PropertyOnMarket` or `PropertyToBeOnMarket` state on occupied office or industrial properties
- the currently confirmed `Signature` building cases
- the observed effect on office-demand and "unoccupied buildings" style diagnostics

This revision intentionally does **not** cover the separate office-resource or `software` supply track.

## Confirmed Live Observation

The currently reproduced save had occupied `Signature` properties that were still treated as market listings.

Confirmed corrected entities from the mod log on March 8, 2026:

- `EE_OfficeSignature01`
- `EE_OfficeSignature02`
- `IndustrialManufacturingSignature03`
- `IndustrialManufacturingSignature06`
- `IndustrialManufacturingSignature07`

Observed corrections:

- 4 occupied `Signature` properties still had `PropertyOnMarket`
- 1 occupied `Signature` industrial property still had `PropertyToBeOnMarket`

After the guard patch removed those stale market components, diagnostics reported:

- `onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0)`
- `phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=5)`
- `office building demand = 57`

Current interpretation:

- the inaccurate `Unoccupied Buildings` symptom in the reproduced save was caused by stale market state on occupied `Signature` properties
- the patch fixed that reproduced symptom
- no non-signature phantom vacancy case has been observed yet in current logs

## What The Components Mean

`PropertyOnMarket` is a market-listing component.

- it means the property is listed
- it does **not** guarantee that the property is truly vacant

`PropertyToBeOnMarket` is a staging component.

- it marks a property to be converted into `PropertyOnMarket`
- if it survives on an already occupied property, it can recreate the stale listing on the next property-processing pass

## Code-Backed Causal Chain

The currently confirmed phantom vacancy path is:

1. a `Signature` office or industrial property regains `PropertyToBeOnMarket` or keeps `PropertyOnMarket`
2. `PropertyProcessingSystem` converts `PropertyToBeOnMarket` into `PropertyOnMarket`
3. company property search still rejects the property if it already has a company renter
4. `IndustrialDemandSystem` counts the on-market office or industrial property as free supply anyway
5. office demand or industrial demand can be suppressed by vacancy that does not actually exist

## Relevant Decompiled Code

### `PropertyOnMarket` is listing state, not vacancy state

- `Game/Buildings/PropertyOnMarket.cs`

### Occupied properties are not truly rentable

- `Game/Buildings/PropertyUtils.cs`
- company property search checks the renter buffer and skips candidates that already have a company renter

### Office and industrial demand counting trust on-market state too much

- `Game/Simulation/IndustrialDemandSystem.cs`
- the free-property counting loop increments office or industrial free supply from `PropertyOnMarket`
- it does not apply the same renter guard that property search uses

### `Signature` buildings already use a separate property lifecycle

- `Game/Simulation/PropertyRenterSystem.cs`
- normal automatic relisting explicitly excludes `Signature` chunks with `!chunk.Has<Signature>()`

This means the base game already intended `Signature` buildings to be handled differently from normal properties.

### `Signature`-specific relisting paths can recreate stale market state

- `Game/Serialization/RequiredComponentSystem.cs`
- `m_OldSignatureBuildingQuery` adds `PropertyToBeOnMarket` to old `Signature` buildings during deserialize
- that path does not check whether the property is already occupied

- `Game/Tools/ApplyObjectsSystem.cs`
- when applying object changes, `Signature` buildings can also regain `PropertyToBeOnMarket`
- that path also does not check current occupancy before re-adding market state

### `PropertyProcessingSystem` turns staging state into live market state

- `Game/Simulation/PropertyProcessingSystem.cs`
- if a property has `PropertyToBeOnMarket`, the system can convert it to `PropertyOnMarket`

## Why This Looks Like A Bug, Not Intended Gameplay

The code strongly suggests that `Signature` buildings were intentionally given special handling.

That part looks deliberate.

What does **not** look deliberate is the final outcome:

- an occupied property is listed on the market
- actual company property search still rejects it
- demand systems still count it as free supply

That combination gives the game no useful behavior.

It does not create real availability.
It only creates false vacancy.

The most defensible interpretation is:

- `Signature`-specific lifecycle handling was intentional
- the occupancy invariant was not consistently enforced across all `Signature` relisting paths
- the stale listing state is therefore more likely an implementation gap than a design goal

## Current Patch Interpretation

The implemented fix does not replace the base-game `Signature` lifecycle.

Instead it enforces one invariant after the known writers and before office or industrial property search and demand evaluation:

- an occupied `Signature` office or industrial property must not keep `PropertyOnMarket`
- an occupied `Signature` office or industrial property must not keep `PropertyToBeOnMarket`

This is a narrow safety correction, not a new market system.

## Current Confidence

High confidence:

- the reproduced `SignatureOffice` phantom vacancy issue was real
- the same stale state also appeared on `SignatureIndustrial`
- removing stale market state resolved the inaccurate vacancy symptom in the reproduced save
- no non-signature phantom vacancy case has appeared in current diagnostics

Not yet fully confirmed:

- whether non-signature office properties can enter the same stale state
- whether non-signature industrial properties can enter the same stale state
- whether deserialize is the dominant source in all saves, or whether runtime object-apply paths matter just as often

## Practical Conclusion

Current best conclusion:

- `Phantom Vacancy` is confirmed for occupied `Signature` office and industrial properties
- the observed symptom is caused by stale market-listing state, not true vacancy
- the current patch fixes the confirmed `Signature` cases without widening behavior to non-signature properties that have not yet been observed
