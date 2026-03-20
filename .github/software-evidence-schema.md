# Software Evidence Schema

This schema defines the minimum context required for a normalized `software`-track evidence entry.

Its purpose is comparability:

- maintainers should be able to tell whether two entries describe the same kind of situation
- missing critical context should be obvious
- issue reports, ad hoc notes, and diagnostics work should use the same field vocabulary

## What This Schema Is

This is an evidence-entry schema, not the full raw diagnostics schema emitted by the mod.

It exists one layer above the logs:

- raw diagnostics record counters and runtime details
- an evidence entry combines those counters with scenario metadata and a bounded observation window
- comparison and conclusion rules operate on evidence entries, not on isolated log lines

That means this file defines the normalized structure used for promoted evidence entries that are worth comparing later, not every possible field that may appear in future diagnostics output.

## Field Groups

### 1. Environment

Environment fields describe the runtime and patch context.

Required:

- `game_version`: Cities: Skylines II game version
- `mod_version`: released mod version, or `unreleased`
- `settings`: state of `EnablePhantomVacancyFix`, `EnableOutsideConnectionVirtualSellerFix`, `EnableVirtualOfficeResourceBuyerFix`, `EnableOfficeDemandDirectPatch`, `EnableDemandDiagnostics`, `DiagnosticsSamplesPerDay`, `CaptureStableEvidence`, and `VerboseLogging`; older logs may also include retired legacy fields such as `EnableTradePatch`
- `patch_state`: any local deviations from a normal release build, including extra logging, local patches, or disabled systems; use `unknown` when the runtime cannot determine them reliably

Optional:

- `mod_ref`: branch name and commit SHA, or another exact source reference for dev or local builds
- `platform_notes`: anything relevant about platform or install layout
- `other_mods`: only if they may affect office demand, trade, property, or company behavior

### 2. Scenario

Scenario fields describe what was being observed.

Required:

- `scenario_label`: save identity, test city name, or a stable scenario label
- `scenario_type`: existing save, fresh city, reproduced test case, or another short classification
- `reproduction_conditions`: what the tester did or what state the city was already in
- `observation_window`: the bounded time span observed, preferably copied from `softwareEvidenceDiagnostics observation_window(...)` when diagnostics are available; keep `sample_index` fields when present because the current diagnostics cadence is per displayed in-game day and configurable

Optional:

- `expected_behavior`: what should have happened instead
- `comparison_baseline`: another save, version, settings state, or prior evidence entry used for comparison

### 3. Observation

Observation fields describe the actual evidence collected.

Required:

- `symptom_classification`: the main observed symptom, using a stable label
- `diagnostic_counters`: the relevant counter groups captured during the observation window; include all groups needed for the hypothesis under test, such as `software(...)`, `electronics(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and `softwareConsumerBuyerState(...)` when present. If the claim is about office-demand response, preserve `officeDemand(...)` instead of paraphrasing it away. When buyer-lifecycle or zero-weight virtual-resolution behavior is part of the claim, preserve any emitted `softwareConsumerBuyerState(...)` subfields that separate corrective versus vanilla buyers, short-gap versus persistent buyerless states, and virtual-resolution summaries instead of collapsing them into prose
- `evidence_summary`: the short factual summary of what was observed
- `confidence`: low, medium, or high
- `confounders`: known uncertainties, competing explanations, or `none known`; use this for uncertainty that is not already represented directly by counters or metadata

Optional:

- `log_excerpt`: only short excerpts or references to attached logs, including relevant `softwareEvidenceDiagnostics detail(...)` lines when office-level state matters; when both roles exist, prefer the latest anchored consumer excerpt plus the latest anchored producer excerpt, then, when short chronology matters, also include the immediately previous distinct sample for each role (one older consumer excerpt and one older producer excerpt)
- `artifacts`: links or filenames for logs, saves, screenshots, or videos; may include relevant `softwareEvidenceDiagnostics detail(...)` lines such as `detail_type=softwareOfficeStates` for concise office state, verbose `detail_type=softwareTradeLifecycle` for lifecycle transitions and seller snapshots, `detail_type=softwareVirtualResolutionProbe` for zero-weight fast-path checks, `detail_type=softwareBuyerTimingProbe` for below-threshold buyer-cadence ambiguity, `virtualOfficeBuyerFixProbe summary(...)` for buyer-fix volume and override sizing, or `outsideConnectionVirtualSellerProbe summary(...)` / verbose `outsideConnectionVirtualSellerProbe sample(...)` when the active question is office-import seller eligibility rather than buyer cadence
- `analysis_basis`: when code reading influenced interpretation, note whether the reasoning came from vanilla decompiled game code, this mod's code, or both, and what each source established
- `notes`: anything useful that does not fit the structured fields

## Raw Versus Normalized Fields

The following fields are expected to come from raw diagnostics with little or no interpretation:

- `diagnostic_counters`
- `log_excerpt`
- `settings`
- parts of `patch_state` when local code or logging differs from the normal build, or the literal `unknown` when the runtime cannot name those deviations

Raw-log automation may draft summaries and symptom labels with LLM assistance,
but the normalized evidence entry should still treat copied counters,
observation-window anchors, and selected detail excerpts as the primary factual
payload.

When the active question is upstream input pressure versus downstream office-resource gating, prefer preserving the matching raw counter groups and any relevant `softwareEvidenceDiagnostics detail(...)` lines together rather than paraphrasing them into prose.
That usually means keeping `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and the shared `detail_type=softwareOfficeStates` lines with their role context.
When seller-state or buyer-lifecycle chronology matters, keep `detail_type=softwareTradeLifecycle` only as supplemental artifact material rather than replacing the concise `softwareOfficeStates` anchors.

When the active question is why zero-software consumers show empty buyer state, preserve `softwareConsumerBuyerState(...)` and the consumer-side `softwareNeed(...)`, `softwareTradeCost(...)`, and `softwareAcquisitionState(...)` blocks together.
Treat `tradeCostEntry=True` as a trade-cost-cache fact, not as enough evidence by itself that an active buyer or in-flight trade exists.
In current builds, `softwareNeed.tripNeededAmount` mirrors vanilla need selection and counts only `TripNeeded` entries with `Purpose.Shopping`; use `detail_type=softwareTradeLifecycle` when you need the broader purpose split.
Treat `selected_resolved_virtual_no_tracking_expected` as a zero-weight fast-path candidate, not as an anomaly by itself.
Treat `selected_no_resource_buyer`, `selected_resource_buyer_no_path`, and `selected_resolved_no_tracking_unexpected` as the primary anomaly-side acquisition states.

When buyer-lifecycle interpretation helpers are emitted, preserve them together with `softwareAcquisitionState(...)` rather than paraphrasing them away. The most useful helpers are `deliveryMode`, `tripTrackingExpected`, `currentTradingExpected`, `pathExpected`, `buyerOrigin`, `buyerSeenThisWindow`, `lastBuyerSeenSampleAge`, `noBuyerReason`, `selectedNoBuyerConsecutiveWindows`, `selectedRequestNoPathConsecutiveWindows`, `belowThresholdConsecutiveWindows`, `pathStage`, `lastPathSeenSampleAge`, `virtualResolvedThisWindow`, `virtualResolvedAmount`, and `lastVirtualResolutionSampleAge`.
Treat `deliveryMode=virtual` plus `tripTrackingExpected=False` / `currentTradingExpected=False` as interpretation context rather than as proof of a missing trade.
Treat `selected_resource_buyer_no_path` as a pre-path intermediate state by default, especially for zero-weight virtual goods, unless the age or persistence fields show that it is lingering abnormally.
Treat `selected_no_resource_buyer` as too broad to interpret on its own; pair it with `noBuyerReason`, the relevant age fields, and any virtual-resolution helpers before escalating it to a promoted anomaly claim.

When the active question is whether a selected software need stayed below threshold long enough to justify or explain the corrective buyer pass, preserve `detail_type=softwareBuyerTimingProbe` as supplemental artifact material. Use it to distinguish short same-sample cadence gaps from repeated below-threshold windows, but do not let it replace the scheduled observation-window anchor, copied counters, or helper-rich `softwareOfficeStates` excerpts.

When the active question is whether software-office distress actually affected office demand, keep `officeDemand(...)` together with the software counters. Treat demand movement as something to observe directly, not something implied by `softwareConsumerOffices.efficiencyZero` or `softwareInputZero` alone.

If the interpretation relies on code reading, separate what came from vanilla decompiled game code from what came from this mod's code. Vanilla decompile is the source of truth for base-game trade lifecycle and virtual-resource handling; mod code explains instrumentation, local patches, and any deviations that belong in `patch_state`.

## Buyer-Lifecycle Interpretation Support

When diagnostics emit buyer-lifecycle interpretation helpers, treat them as schema-level evidence support rather than as disposable debug text.

Recommended helper fields:

- `deliveryMode`
- `tripTrackingExpected`
- `currentTradingExpected`
- `pathExpected`
- `buyerOrigin`
- `buyerSeenThisWindow`
- `lastBuyerSeenSampleAge`
- `noBuyerReason`
- `selectedNoBuyerConsecutiveWindows`
- `selectedRequestNoPathConsecutiveWindows`
- `belowThresholdConsecutiveWindows`
- `pathStage`
- `lastPathSeenSampleAge`
- `virtualResolvedThisWindow`
- `virtualResolvedAmount`
- `lastVirtualResolutionSampleAge`

Use these helpers to distinguish:

- normal zero-weight fast-path behavior from suspicious missing-tracking states
- a short pre-path gap from a persistent buyer lifecycle stall
- a company that truly stayed buyerless from one that briefly dipped below threshold and then resolved virtually

These helper fields belong in the normalized evidence vocabulary whenever they materially improve interpretation of `softwareConsumerBuyerState(...)` or `softwareAcquisitionState(...)`.

`detail_type=softwareBuyerTimingProbe` is likewise supplemental artifact material for buyer-cadence questions. Keep it adjacent to the scheduled sample it helps interpret rather than treating it as a replacement for `softwareConsumerBuyerState(...)`, `softwareAcquisitionState(...)`, or the observation-window anchor.

High-volume fix-specific telemetry should still remain separate from the minimum evidence schema.
For example, `virtualOfficeBuyerFixProbe summary(...)` is useful supplemental artifact material for buyer-fix volume and override sizing, `outsideConnectionVirtualSellerProbe summary(...)` is useful supplemental artifact material for lightweight office-import request volume, and verbose `outsideConnectionVirtualSellerProbe sample(...)` lines are useful seller-snapshot artifacts when seller eligibility itself is under investigation. None of those probe lines should replace the stable evidence fields above unless a specific investigation line is explicitly about that probe output itself.

The following fields usually require explicit investigator input:

- `scenario_label`
- `scenario_type`
- `reproduction_conditions`
- `observation_window`
- `confounders`
- `patch_state` when the runtime emitted `unknown` but the maintainer knows the exact local deviations
- `analysis_basis` when code reading was part of the interpretation

When diagnostics are sampled more than once per day, the copied `observation_window` should usually retain `start_sample_index`, `end_sample_index`, `sample_day`, `sample_index`, `sample_slot`, `samples_per_day`, `sample_count`, and `skipped_sample_slots` so later readers can tell same-day samples apart and interpret density correctly.

Interpret those fields with the current diagnostics contract:

- `sample_slot`: the scheduled slot derived from the runtime `TimeSystem` time-of-day path
- `sample_day`: the logical displayed-clock day reconstructed from slot progression
- `sample_count`: emitted `observation_window(...)` count in the current run, not a theoretical slot count
- `skipped_sample_slots`: scheduled gaps that elapsed without a backfilled observation

The following fields should stay normalized and constrained even when they are chosen by a maintainer:

- `symptom_classification`
- `confidence`

Those fields should use stable labels rather than free-form prose wherever possible.

This schema intentionally does not define the full raw diagnostics vocabulary or the end-to-end investigation process. Those operational details belong in the investigation workflow document.

## Comparability Rules

Two entries are directly comparable only if all of the following are true:

1. `game_version` matches
2. `mod_ref` or `mod_version` matches closely enough for the comparison being made
3. `settings` and `patch_state` are equivalent for the behavior under discussion
4. `scenario_type` and `reproduction_conditions` are similar enough to support the same claim
5. `symptom_classification` refers to the same failure pattern

If one of those differs, the comparison should be treated as weaker or indirect.

## Symptom Classification Guidance

Use short stable labels instead of free-form titles where possible. Current examples:

- `software_office_propertyless`
- `software_office_efficiency_zero`
- `software_office_lack_resources_zero`
- `software_demand_mismatch`
- `software_track_unclear`

`software_demand_mismatch` is the preferred label when software-office distress is present but office-demand counters stay flat or rise, or when the expected software-to-demand relationship does not appear.

These labels are working categories, not proof of root cause.
Keep them symptom-based rather than cause-based. Do not introduce presumed root-cause labels such as `electronics_shortage`.

## Related Documents

- [`.github/software-investigation-workflow.md`](./software-investigation-workflow.md): log capture, comparison checkpoints, decision rules, and stage workflow
- [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./ISSUE_TEMPLATE/software_evidence.yml): reusable evidence entry form
