# SoftwareTrade Analysis

## Scope

This document summarizes what has been confirmed from the decompiled base game code in `..\CSL2_Decompiled` about the `No Office Demand` bug and the `software` supply chain.

The goal is to separate:

- what is clearly confirmed by code
- what was initially assumed but is not directly supported by code
- what now looks like a likely implementation bug in the game's virtual trade or virtual storage handling

## Confirmed Causal Chain

The broad causal chain behind the office-demand collapse is supported by code.

1. Office and industrial companies use `buildingEfficiency` as a direct multiplier for production.
2. `ProcessingCompanySystem` can set the `LackResources` efficiency factor to `0`.
3. `BuildingUtils` multiplies all efficiency factors together, so `LackResources == 0` makes total efficiency `0`.
4. If an office company's efficiency becomes `0`, its production also becomes `0`.
5. Office demand is derived from company production and resource demand counters, so a software supply failure can collapse office demand indirectly.

### Relevant code

- `Game/Simulation/ProcessingCompanySystem.cs:166`
  - reads efficiency without `LackResources`
- `Game/Simulation/ProcessingCompanySystem.cs:198`
  - sets `EfficiencyFactor.LackResources` to `0` or `1`
- `Game/Buildings/BuildingUtils.cs:124`
  - total efficiency is the product of all factors
- `Game/Economy/EconomyUtils.cs:1477`
  - company production per day uses `buildingEfficiency`
- `Game/Simulation/CountCompanyDataSystem.cs:368`
  - converts efficiency into per-company production
- `Game/Simulation/IndustrialDemandSystem.cs:189`
  - office resource demand is handled as a separate branch
- `Game/Simulation/IndustrialDemandSystem.cs:413`
  - accumulates office company demand
- `Game/Simulation/IndustrialDemandSystem.cs:444`
  - accumulates office building demand

## What Was Correct In The Original Hypothesis

The following parts of the initial interpretation match the code reasonably well:

- `software` is an office resource
- `software` shortages can zero out office efficiency through `LackResources`
- once efficiency drops to `0`, office production collapses
- that collapse can feed into the office-demand systems and produce the visible `No Office Demand` symptom

### Relevant code

- `Game/Economy/EconomyUtils.cs:1292`
  - `IsOfficeResource`
- `Game/Economy/EconomyUtils.cs:186`
  - `Software` is upstream input for office outputs such as `Telecom`, `Financial`, and `Media`

## What Did Not Match The Original Hypothesis Exactly

Two parts of the original explanation do not hold as stated.

### 1. `Efficiency == 0` is not directly treated as `Vacant`

I did not find code that directly marks a building as vacant just because efficiency is `0`.

The UI-side vacant state is tied to whether the building actually has a company renter, not to efficiency alone.

### Relevant code

- `Game/UI/InGame/CompanyUIUtils.cs:12`
  - checks whether a company exists for the building
- `Game/UI/InGame/CompanySection.cs:147`
  - shows `VacantOffice` when no company entity is present
- `Game/Simulation/IndustrialDemandSystem.cs:292`
  - free office properties are counted from `PropertyOnMarket`
- `Game/Simulation/CountCompanyDataSystem.cs:399`
  - tracks propertyless companies separately

### Practical interpretation

The safer interpretation is:

- `software shortage -> efficiency 0 -> production/profit collapse`
- that collapse may later increase propertyless or actually vacant office situations
- but the code does not show a simple direct rule saying `efficiency 0 == vacant`

### 2. `software` is not simply "non-tradable and non-storable"

The game already contains special handling for zero-weight and office resources.

There is virtual-trade or virtual-storage logic for them, but the implementation is only partial and appears inconsistent across systems.

## New Finding: Virtual Trade Or Virtual Storage Is Probably Implemented Inconsistently

This is the most important follow-up result.

The code suggests that office resources such as `software` were intended to be tradable or bufferable through special virtual paths, but only some systems honor that rule.

### Confirmed special handling

#### TradeSystem special-cases office resources

`TradeSystem` explicitly allows office resources even when they are not present in `StorageCompanyData.m_StoredResources`.

- `Game/Simulation/TradeSystem.cs:162`
  - `bool flag = EconomyUtils.IsOfficeResource(iterator.resource);`
- `Game/Simulation/TradeSystem.cs:163`
  - accepts a resource if it is in `m_StoredResources` **or** it is an office resource
- `Game/Simulation/TradeSystem.cs:173`
  - office resources use a target stock limit of `0`
- `Game/Simulation/TradeSystem.cs:181`
  - mutates the outside connection resource buffer for that resource

This means the game explicitly tries to support office-resource trade at outside connections, even if the prefab's stored-resource list does not include them.

#### ResourceBuyerSystem supports virtual purchases

`ResourceBuyerSystem` treats zero-weight goods as virtual.

- `Game/Simulation/ResourceBuyerSystem.cs:212`
  - if sale is virtual, bought resource is added directly to buyer resources
- `Game/Simulation/ResourceBuyerSystem.cs:473`
  - zero-weight resource check
- `Game/Simulation/ResourceBuyerSystem.cs:504`
  - sets `SaleFlags.Virtual`
- `Game/Simulation/ResourceBuyerSystem.cs:652`
  - virtual goods use `PathfindFlags.SkipPathfind`
- `Game/Simulation/ResourceBuyerSystem.cs:760`
  - same for company buyers

#### ResourceExporterSystem supports virtual exports

`ResourceExporterSystem` also has a zero-weight branch.

- `Game/Simulation/ResourceExporterSystem.cs:263`
  - detects zero-weight resource
- `Game/Simulation/ResourceExporterSystem.cs:296`
  - adds the exported resource directly to an outside connection buffer

#### Company-side virtual storage exists

Office-resource outputs do not rely on normal warehouse storage.

- `Game/Simulation/ProcessingCompanySystem.cs:227`
  - if output has no weight, storage is capped by `IndustrialAISystem.kMaxVirtualResourceStorage`
- `Game/Simulation/IndustrialAISystem.cs:290`
  - `kMaxVirtualResourceStorage = 100000`
- `Game/Simulation/OfficeAISystem.cs:119`
  - office outputs are consumed from company resource buffers
- `Game/Simulation/OfficeAISystem.cs:126`
  - high stock can trigger `ResourceExporter`

So the base game does have a virtual-buffer model for office resources.

## Where The Implementation Stops Being Consistent

The likely bug is that seller discovery, import path setup, and storage-transfer logic still use the old `m_StoredResources` gate even when office resources are supposed to be handled virtually.

### 1. ResourcePathfindSetup import seller selection is still `m_StoredResources`-gated

- `Game/Simulation/ResourcePathfindSetup.cs:116`
  - office resources are recognized with `IsOfficeResource(resource)`
- `Game/Simulation/ResourcePathfindSetup.cs:124`
  - office-resource producer companies can be considered valid sellers through output matching
- `Game/Simulation/ResourcePathfindSetup.cs:135`
  - **import sellers** are accepted only if `(m_StorageCompanyDatas[prefab].m_StoredResources & resource) != 0`

This is the key inconsistency.

For office resources:

- local producer companies are handled specially
- import sellers are **not** handled specially

So even if `TradeSystem` and the buyer or exporter systems support virtual office-resource trade, the import-side seller discovery can still reject outside connections and stations unless their prefab storage list already includes that office resource.

### 2. ResourceAvailabilitySystem has the same old gate

- `Game/Simulation/ResourceAvailabilitySystem.cs:292`
  - storage-company providers are only added if the requested resource is in `m_StoredResources`

No office-resource exception is present there.

### 3. StorageCompanySystem still uses normal storage gating

- `Game/Simulation/StorageCompanySystem.cs:502`
  - outside connection storage processing only runs if the resource is in `m_StoredResources`
- `Game/Simulation/StorageCompanySystem.cs:1059`
  - normal storage processing uses the same condition

### 4. StorageTransferSystem also assumes normal stored-resource membership

- `Game/Simulation/StorageTransferSystem.cs:141`
  - storage limits are derived from `CountResources(data.m_StoredResources)`
- `Game/Simulation/StorageTransferSystem.cs:163`
  - the same assumption is reused when the target is another storage entity

This is another sign that "virtual office resource support" was added on top of a system that still mostly assumes physical stored resources.

## Why This Looks Like A Real Bug

Outside connections and cargo stations build `StorageCompanyData.m_StoredResources` only from their prefab-defined `m_TradedResources`.

### Relevant code

- `Game/Prefabs/OutsideConnection.cs:59`
  - starts from `Resource.NoResource`
- `Game/Prefabs/OutsideConnection.cs:65`
  - fills stored resources from `m_TradedResources`
- `Game/Prefabs/CargoTransportStation.cs:94`
  - same pattern
- `Game/Prefabs/CargoTransportStation.cs:100`
  - same pattern

That means the engine is using two different rules at the same time:

- prefab data says what a storage or outside connection officially stores
- `TradeSystem` separately says office resources are always allowed

If the prefab data does not include `software`, then:

- `TradeSystem` may still try to push office resources through outside connections
- but `ResourcePathfindSetup`, `ResourceAvailabilitySystem`, and `StorageCompanySystem` can still reject those same entities as valid import or storage providers

That is a strong code-level candidate for the observed software supply instability.

## Current Best Interpretation

The earlier conclusion should be refined as follows:

- the problem is **not** that software has no trade or storage implementation at all
- the problem is more likely that software uses a **partially implemented virtual trade or storage model**
- some systems treat office resources as valid virtual goods
- other systems still require them to exist in normal `m_StoredResources`
- this mismatch can break seller discovery, import availability, storage balancing, and stock smoothing

## Modding Implications

For `SoftwareTrade`, the highest-value next investigation and patch area is not just "make software tradable" in a generic sense.

It is specifically:

1. make import-provider discovery treat office resources the same way `TradeSystem` already does
2. make storage-provider discovery and transfer logic treat office resources consistently
3. decide whether to patch prefab stored-resource lists, runtime logic, or both

## Current Prototype Choice

The first code implementation in this repository currently takes the prefab-data route first.

It patches `StorageCompanyData.m_StoredResources` on:

- outside connection prefabs
- cargo station prefabs

and injects the office-resource set:

- `Software`
- `Telecom`
- `Financial`
- `Media`

Why this is a reasonable first step:

- the current inconsistency is visible in several systems, not just one
- all of the currently known bad gates consult `m_StoredResources`
- prefab augmentation is the smallest runtime change that can test those gates together

This does not prove the prefab route is the cleanest final fix. It only means it is the fastest way to validate whether the shared `m_StoredResources` gate is the practical choke point in gameplay.

## Patch Direction

The current best plan is to fix the mismatch in the smallest possible order instead of rewriting every trade system at once.

### Recommended implementation order

1. patch import seller discovery first
2. patch provider discovery second
3. patch storage-transfer consistency third
4. use prefab-data augmentation only if runtime patches alone are insufficient
5. keep any "vacant handling" workaround as a last resort, not as the primary fix

### Why this order

`TradeSystem`, `ResourceBuyerSystem`, `ResourceExporterSystem`, `ProcessingCompanySystem`, and `OfficeAISystem` already contain office-resource-specific logic.

That means the most likely breakage is not the absence of all support, but the places where office resources still have to pass the normal `m_StoredResources` gate.

The smallest coherent fix is therefore to make seller and provider selection match the rules already used by `TradeSystem`.

### Primary patch targets

#### Target A: import seller discovery

Patch the import branch in `ResourcePathfindSetup` so that office resources can use the same exception rule as `TradeSystem`.

Current gate:

- `Game/Simulation/ResourcePathfindSetup.cs:135`
  - import sellers require `(m_StorageCompanyDatas[prefab].m_StoredResources & resource) != 0`

Desired behavior:

- if `resource` is an office resource, treat outside connections, cargo stations, and other import-capable storage entities as valid import sellers even when the prefab does not list that resource in `m_StoredResources`

Expected effect:

- buyers looking for `software` should be able to select outside connections consistently
- virtual import becomes reachable instead of being blocked at target selection

#### Target B: provider discovery

Patch `ResourceAvailabilitySystem` so office resources are exposed by storage-style providers under the same rule.

Current gate:

- `Game/Simulation/ResourceAvailabilitySystem.cs:292`
  - provider added only if resource exists in `m_StoredResources`

Desired behavior:

- provider discovery should not hide office-resource sellers that `TradeSystem` already treats as valid

Expected effect:

- any system relying on resource-availability queries should see office-resource providers more consistently

#### Target C: storage-transfer and outside-connection storage flow

Patch the storage path only after import and provider discovery are aligned.

Current gates:

- `Game/Simulation/StorageCompanySystem.cs:502`
- `Game/Simulation/StorageCompanySystem.cs:1059`
- `Game/Simulation/StorageTransferSystem.cs:141`
- `Game/Simulation/StorageTransferSystem.cs:163`

Desired behavior:

- office resources should not be rejected from storage balancing or outside-connection transfer logic merely because they are absent from prefab `m_StoredResources`
- capacity calculations must avoid accidental division or allocation errors if office resources remain virtual-only

Expected effect:

- city-level buffering and transfer smoothing should stop fighting the virtual-trade path

### Runtime patch vs prefab patch

There are two broad strategies.

#### Strategy 1: runtime logic patch

Modify the systems that still gate office resources through `m_StoredResources`.

Pros:

- directly addresses the inconsistent logic
- stays aligned with the game's existing office-resource special cases
- avoids mutating asset data globally unless needed

Cons:

- touches more than one system
- requires careful consistency across query and transfer stages

#### Strategy 2: prefab stored-resource augmentation

Inject office resources into outside connection and cargo station `StorageCompanyData.m_StoredResources`.

Pros:

- can satisfy several old gates at once
- may be simpler if the runtime systems are difficult to patch safely

Cons:

- may over-broaden storage semantics for systems that were never meant to treat office resources as normal stored goods
- could hide the real inconsistency instead of fixing it cleanly

### Recommended first version

For v0, prefer:

1. runtime patch for `ResourcePathfindSetup`
2. runtime patch for `ResourceAvailabilitySystem`
3. targeted logging
4. only then decide whether `StorageCompanySystem` also needs an active patch

This is the narrowest path that can confirm whether import reachability is the main failure.

## Validation Plan

The mod should be validated against observable in-game behavior, not only against static code consistency.

### Minimum checks

1. verify that office companies can select outside connections as sellers for `software`
2. verify that `software` stock no longer collapses to zero for long periods in a stable city
3. verify that affected office buildings recover non-zero efficiency after supply recovers
4. verify that office demand starts recovering without any separate "fake demand" patch

### Useful instrumentation

- log seller selection for office-resource purchases
- log whether import targets were filtered out by `m_StoredResources`
- log resource deltas on outside connections for `software`
- log office-company efficiency and `LackResources` transitions

### Exit criteria for the first gameplay patch

The first real gameplay patch should be considered successful only if:

- office-resource imports are visibly reachable
- software shortages are reduced or eliminated in normal play
- office demand recovers without introducing obviously incorrect side effects in other resources
- no blanket vacancy workaround is required

## Confidence And Remaining Unknowns

### High confidence

- the efficiency-collapse chain is real
- office resources already have some virtual handling
- virtual handling is inconsistent across systems

### Not yet fully confirmed

- whether every shipped outside connection prefab excludes `software`
- whether the inconsistency alone is sufficient to reproduce the full `No Office Demand` bug in all cases

Those points may require either runtime inspection, logging, or prefab data inspection beyond the decompiled C# sources.
