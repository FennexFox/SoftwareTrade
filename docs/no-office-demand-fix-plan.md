# Phantom Vacancy Patch Plan

## Goal

Keep the current `Phantom Vacancy` fix narrow, verified, and easy to extend only if new evidence appears.

The current plan is no longer an investigation split across multiple unrelated tracks.
It is now specifically about the confirmed stale market-state problem on occupied `Signature` office and industrial properties.

## Implemented Patch

The current implementation adds `SignaturePropertyMarketGuardSystem`.

It is registered so that it runs:

- after `PropertyProcessingSystem`
- after `RentAdjustSystem`
- after `CompanyMoveAwaySystem`
- before `IndustrialFindPropertySystem`
- before `IndustrialDemandSystem`

The system checks:

- `Signature`
- `OfficeProperty` or `IndustrialProperty`
- `PropertyOnMarket` or `PropertyToBeOnMarket`

Excluded states:

- `Abandoned`
- `Destroyed`
- `Deleted`
- `Temp`
- `Condemned`

Correction rule:

- if the property itself already has an active company renter, remove `PropertyOnMarket`
- if the property itself already has an active company renter, remove `PropertyToBeOnMarket`

This is an invariant guard, not a replacement lifecycle.

## Current Validation Status

The confirmed reproduced save now shows:

- `EE_OfficeSignature01` and `EE_OfficeSignature02` corrected from stale `PropertyOnMarket`
- three occupied `SignatureIndustrial` properties corrected from stale market state
- `guardCorrections=5` on the first diagnostic pass after load
- zero remaining phantom vacancy counters on the next diagnostic samples
- `officeBuildingDemand` recovering to a non-zero value

Current status summary:

- the inaccurate `Unoccupied Buildings` style symptom is resolved in the reproduced save
- the confirmed problem is not currently visible on non-signature office or industrial properties

## What To Keep Watching

The current diagnostics should remain focused on these questions:

- does `nonSignatureOccupiedOnMarketOffice` ever become non-zero
- does `nonSignatureOccupiedOnMarketIndustrial` ever become non-zero
- do new `Signature` corrections happen only immediately after load, or also later during normal gameplay
- does `signatureOccupiedToBeOnMarket` reappear during long-running simulation

These answers determine whether the next step should remain a narrow `Signature` fix or be widened.

## Next Actions If New Evidence Appears

If new `Signature` corrections continue to appear during normal gameplay:

- instrument the most recent writer path more aggressively
- distinguish deserialize-driven cases from runtime relisting cases

If non-signature phantom vacancy is observed:

- keep the current `Signature` fix in place
- add a separate analysis pass before widening behavior
- do not widen the guard blindly without a concrete reproduced case

## Not Planned Right Now

These changes are intentionally out of scope for the current patch:

- widening the guard to all office or industrial properties without evidence
- patching `IndustrialDemandSystem` to harden free-property counting for every case
- patching `RequiredComponentSystem` or `ApplyObjectsSystem` directly
- mixing the phantom vacancy documentation back together with unrelated office-resource topics

## Definition Of Done

For the current patch stage, the work is considered complete when:

- occupied `Signature` office or industrial properties no longer remain on market in the reproduced save
- diagnostics stay at zero for `Signature` phantom vacancy counters after the initial corrections
- non-signature counters remain under observation instead of being assumed safe
- documentation clearly states that `Signature` phantom vacancy is confirmed, while non-signature reproduction is still unconfirmed
