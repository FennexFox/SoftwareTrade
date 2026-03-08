# No Office Demand Fix

`No Office Demand Fix` is a Cities: Skylines II mod project focused on investigating and fixing the office-demand collapse commonly described as the `No Office Demand` bug.

The project still has two goals:

- solve `Phantom Vacancy`
- investigate and fix the office-resource / `software` supply problem

The current version's main accomplishment is the `Phantom Vacancy` fix.
The `software` track remains active, documented, and unresolved.

## Project Goals

The repository currently tracks two work streams.

### Track A: office-resource / `software` trade and storage consistency

- `software` is an office resource required by a large part of the office economy
- when `software` supply fails, affected office companies can lose `LackResources`
- that can push total efficiency to `0`
- production collapses
- office demand can collapse as a downstream effect

### Track B: `Phantom Vacancy` on occupied office or industrial properties

- some office or industrial properties can keep `PropertyOnMarket` or `PropertyToBeOnMarket` even though they already have an active company renter
- `IndustrialDemandSystem` can still count those stale market listings as free supply
- that can suppress office or industrial building demand even when no true vacancy exists

## Current Version Status

The current release status is:

- confirmed `Signature` `Phantom Vacancy` is fixed by the shipped patch
- current logs show no confirmed non-signature phantom-vacancy reproduction yet
- the `software` track remains a separate ongoing investigation

Confirmed current phantom-vacancy result:

- stale market state was corrected on `EE_OfficeSignature01`
- stale market state was corrected on `EE_OfficeSignature02`
- stale market state was also corrected on multiple occupied `SignatureIndustrial` properties
- after those corrections, phantom-vacancy counters dropped to zero in the reproduced save

Important interpretation:

- the current version fixes the confirmed `Signature` phantom-vacancy case
- it does **not** claim that the `software` supply issue is solved
- it does **not** claim that non-signature phantom vacancy is impossible

## Current Implementation

The current implementation includes:

1. `OfficeResourceStoragePatchSystem`
   - a one-shot prefab patch that augments outside connection and cargo station storage definitions for office resources
   - this is still best treated as a trade-path mitigation or validation experiment
2. `SignaturePropertyMarketGuardSystem`
   - the shipped gameplay fix for confirmed `Signature` phantom vacancy
   - removes stale `PropertyOnMarket` and `PropertyToBeOnMarket` from occupied `Signature` office or industrial properties before demand and property search evaluate them
3. `OfficeDemandDiagnosticsSystem`
   - logs office demand factors, software office efficiency, phantom-vacancy counters, and market-state details when the state looks suspicious

Current settings surface:

- `EnableTradePatch`
- `EnablePhantomVacancyFix`
- `EnableDemandDiagnostics`
- `VerboseLogging`

Current defaults from [Setting.cs](./NoOfficeDemandFix/Setting.cs):

- `EnableTradePatch = false`
- `EnablePhantomVacancyFix = true`
- `EnableDemandDiagnostics = false`
- `VerboseLogging = false`

## Findings Summary

### Confirmed current fix result: `Phantom Vacancy`

Confirmed so far:

- `PropertyOnMarket` is a market-listing component, not a guaranteed vacancy state
- `PropertyToBeOnMarket` can stage a stale market listing back into `PropertyOnMarket`
- occupied `Signature` office and industrial properties could remain in stale market state
- those stale listings created false free-property supply for office or industrial demand
- the current `SignaturePropertyMarketGuardSystem` fixes the confirmed reproduced `Signature` cases

Current open question:

- whether non-signature office or industrial properties can reproduce the same stale market state

### Unresolved current interpretation: `software`

Confirmed so far:

- `software` is an office resource
- `LackResources == 0` can collapse building efficiency to `0`
- the base game already contains partial virtual handling for zero-weight office resources
- that office-resource handling is inconsistent across trade, provider, and storage systems

Current interpretation:

- `software` starvation is still a plausible independent office-demand failure mode
- the current trade patch is still a mitigation or validation experiment
- it is not yet a confirmed full fix

## Future Plans

### Phantom Vacancy follow-up

- keep monitoring `nonSignatureOccupiedOnMarketOffice` and `nonSignatureOccupiedOnMarketIndustrial`
- determine whether new `Signature` corrections happen only after load or also during longer runtime simulation
- distinguish deserialize-driven relisting from runtime object-apply relisting if new cases appear
- widen the phantom-vacancy fix only if concrete non-signature evidence appears

### Software / office-resource follow-up

- validate whether the current prefab augmentation materially improves software availability in gameplay
- patch import seller discovery directly if office resources are still blocked there
- patch provider visibility directly if office-resource sellers are still hidden there
- patch storage-transfer or storage-company logic only if evidence still shows blocked office-resource buffering

## Docs

- [Analysis](./docs/no-office-demand-fix-analysis.md)
- [Patch Plan](./docs/no-office-demand-fix-plan.md)
- [Phantom Vacancy Summary](./docs/phantom-vacancy-summary.md)

## Project Layout

- [NoOfficeDemandFix/Mod.cs](./NoOfficeDemandFix/Mod.cs): mod entry point
- [NoOfficeDemandFix/Setting.cs](./NoOfficeDemandFix/Setting.cs): settings and localization
- [NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs](./NoOfficeDemandFix/Systems/OfficeResourceStoragePatchSystem.cs): one-shot prefab office-resource storage patch
- [NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs](./NoOfficeDemandFix/Systems/SignaturePropertyMarketGuardSystem.cs): shipped `Signature` phantom-vacancy fix
- [NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs](./NoOfficeDemandFix/Systems/OfficeDemandDiagnosticsSystem.cs): office demand, software, and phantom-vacancy diagnostics
- [docs/no-office-demand-fix-analysis.md](./docs/no-office-demand-fix-analysis.md): dual-track analysis
- [docs/no-office-demand-fix-plan.md](./docs/no-office-demand-fix-plan.md): current roadmap
- [docs/phantom-vacancy-summary.md](./docs/phantom-vacancy-summary.md): community-facing phantom-vacancy summary

## Non-Goals For The First Version

- faking office demand directly
- blanket vacancy overrides
- large balance changes unrelated to the confirmed bug tracks
