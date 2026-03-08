# No Office Demand Fix Patch Plan

## Summary

The project still has two active tracks:

- `Track A`: office-resource / `software` trade and storage consistency
- `Track B`: `Phantom Vacancy`

The important current status difference is:

- `Phantom Vacancy` is the current shipped fix
- `software` remains the main unresolved gameplay track

## Current Patch Status

The current version ships `SignaturePropertyMarketGuardSystem`.

Shipped status:

- it runs after the known market-state writers and before office or industrial property search and demand evaluation
- it removes stale `PropertyOnMarket` and `PropertyToBeOnMarket` from occupied `Signature` office or industrial properties
- it fixed the confirmed reproduced symptom in the current save

Current confirmed result:

- `SignatureOffice` phantom vacancy was corrected
- `SignatureIndustrial` phantom vacancy was also corrected
- the inaccurate `Unoccupied Buildings` style symptom is resolved in the reproduced save

Current monitoring result:

- no confirmed non-signature phantom-vacancy reproduction has appeared yet

## Track A: Software / Office-Resource Next Work

The `software` track remains an active future work stream.

Next steps:

1. validate whether the current prefab augmentation materially improves software availability in gameplay
2. patch import seller discovery directly if office-resource imports are still blocked there
3. patch provider visibility directly if office-resource sellers are still hidden there
4. patch storage-transfer or storage-company logic only if evidence still shows blocked office-resource buffering

Recommended validation signals:

- software production and demand counters
- software office `propertyless` counts
- `efficiencyZero` and `lackResourcesZero` counts for software offices
- whether long-running saves still show sustained software starvation with the trade patch enabled

Current interpretation:

- the trade patch remains useful
- it is still a mitigation or validation experiment, not a confirmed complete solution

## Track B: Phantom Vacancy Next Work

The `Phantom Vacancy` track is now in monitor-and-extend mode rather than first-fix mode.

Next steps:

1. monitor non-signature office and industrial reproduction through diagnostics
2. determine whether new `Signature` corrections happen only after load or also later during simulation
3. distinguish deserialize-driven relisting from runtime object-apply relisting if new cases appear
4. widen the phantom-vacancy fix only if concrete non-signature evidence appears

Current guidance:

- keep the current narrow `Signature` fix in place
- do not widen the guard blindly
- do not claim non-signature safety until a reproduced case set is broader

## Decision Note

The current release should be understood as:

- `Phantom Vacancy` is the currently shipped and confirmed fix track
- `software` remains the main next unresolved gameplay track

That means future gameplay work should prioritize:

1. keeping phantom-vacancy monitoring active
2. using the next iteration primarily to reduce uncertainty in the `software` trade and storage path

## Definition Of Done For The Next Stage

The next stage should be considered successful only if:

- the current `Signature` phantom-vacancy fix remains stable
- non-signature phantom-vacancy counters remain monitored and explicitly documented as confirmed or unconfirmed
- the `software` track has clearer evidence about whether prefab augmentation is enough
- any new runtime trade or storage patch is justified by direct evidence rather than guesswork
