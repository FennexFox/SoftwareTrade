# Software Investigation Workflow

This document defines the maintainer workflow for collecting, promoting, comparing, and summarizing `software`-track evidence without flooding the issue tracker.

It is the workflow companion to [`.github/software-evidence-schema.md`](./software-evidence-schema.md). The schema defines what a normalized evidence entry must contain. This document defines when to keep material as raw capture, when to promote it into a reusable evidence issue, and where to record comparisons and conclusions.

## Quick Start

Treat investigation material as three levels:

1. Raw capture
   Logs, saves, screenshots, videos, and temporary notes. Do not open a new issue for every sample.

2. Promoted evidence entry
   A bounded observation window that is reusable later. This is when you open a `software evidence` issue.

3. Umbrella investigation
   The tracker for one hypothesis or investigation line. This is where linked evidence, comparison summaries, and the current conclusion live.

Use the tracker this way:

- keep raw captures local unless they are bounded and reusable
- promote only evidence-worthy runs
- keep checkpoint comparisons and the current conclusion in the umbrella issue, not scattered across raw captures

## Inputs

The workflow uses four inputs:

- raw `softwareEvidenceDiagnostics` logs emitted by diagnostics
- local artifacts such as logs, saves, screenshots, or videos
- the reusable evidence issue form at [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./ISSUE_TEMPLATE/software_evidence.yml)
- the reusable umbrella investigation issue form at [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./ISSUE_TEMPLATE/software_investigation.yml)

## Workflow

1. Choose or reproduce a scenario.
   Select the save, city state, or bounded condition to observe.

2. Capture raw material.
   Keep logs and artifacts without opening issues for every sample.

3. Promote reusable runs.
   Open a `software evidence` issue only for bounded runs that will matter later.

4. Link evidence under one umbrella.
   Use one `software investigation` issue per hypothesis or investigation line.

5. Record checkpoint comparisons.
   Summarize comparisons in the umbrella issue body or follow-up comments.

6. Update the current conclusion.
   Apply the decision rules after each meaningful comparison.

7. Record repo-facing wording.
   Keep README, release-note, and issue-summary wording aligned with the evidence category.

## Promote Or Keep Raw

Open a `software evidence` issue only when the run is evidence-worthy:

- the observation window is bounded and specific
- the run has enough context to stand on its own later
- the run is likely to be reused in a later comparison
- the run is more than a temporary note or ad hoc sample

Keep the material as raw capture or summarize it only in the umbrella issue when:

- the window is too loose or poorly bounded
- important context is missing
- the run is unlikely to be reused
- the material is still only a scratch note or local observation

## Capture Checklist

Use this section when turning a raw run into a reusable evidence entry.

### 1. Preserve The Required Anchors

Always copy these items directly from the run when they exist:

1. `softwareEvidenceDiagnostics observation_window(...)`
2. `settings=...`
3. `patch_state=...`
4. `diagnostic_counters(...)`
5. only the minimum investigator-written context needed to make the run reusable

### 2. Select Detail Excerpts Deliberately

Preserve `softwareEvidenceDiagnostics detail(...)` lines only when office-level or property-level state matters to the claim.

Default excerpt rules:

- prefer the newest anchored detail sample
- add at most one or two immediately previous distinct samples only when short chronology matters
- if both consumer-side and producer-side detail are available, use the latest anchored consumer excerpt plus the latest anchored producer excerpt as the default pair

Add older distinct samples only when they clearly improve interpretation, for example:

- they show a transition between states
- they show a persistent or oscillating pattern that is central to the claim
- they confirm that a state stabilized after a change

### 3. Keep The Writeup Tight

- copy counters directly from logs rather than paraphrasing them
- keep the full bounded observation-window string when possible so `session_id`, `run_id`, day fields, sample-index fields, sample-slot fields, and `clock_source` remain available for later comparisons
- treat `diagnostic_counters` as factual capture
- treat `diagnostic_counters` as the sampled end-of-window state unless you explicitly note a wider aggregation method
- keep `evidence_summary` short and descriptive, not argumentative
- use `confidence` and `confounders` only for uncertainty that is not already represented by counters or metadata
- do not treat `symptom_classification` as proof of root cause
- treat raw-log automation symptom labels and prose as drafting help; the copied anchors and excerpts remain the hard evidence
- if runtime emits `patch_state=unknown`, keep that value unless you can replace it with an exact known local deviation set
- when differentiating upstream input pressure from downstream software-consumer shortage or office-resource trade and storage gating, prefer preserving `electronics(...)`, `software(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and any relevant `detail_type=softwareOfficeStates` lines together
- when the active question is why zero-software consumers keep empty buyer state, preserve `softwareConsumerBuyerState(...)` together with the relevant `softwareNeed(...)`, `softwareTradeCost(...)`, and `softwareAcquisitionState(...)` detail blocks; when emitted, keep buyer-lifecycle helper fields such as `deliveryMode`, `buyerOrigin`, `noBuyerReason`, `selectedNoBuyerConsecutiveWindows`, `selectedRequestNoPathConsecutiveWindows`, and `virtualResolved*` values with the same detail block instead of paraphrasing them away
- keep the concise producer `input1(...)` / `input2(...)` formatter stock-only by default; when seller-state or buyer-lifecycle transitions matter, use verbose `detail_type=softwareTradeLifecycle` lines instead of re-expanding the concise formatter
- treat `sample_count` as emitted `softwareEvidenceDiagnostics observation_window(...)` density inside the current run, not as a replacement for the day fields
- treat `skipped_sample_slots` as scheduled sample slots that were missed and honestly reported rather than backfilled
- if raw-log automation preserved both consumer-side and producer-side detail, treat the latest anchored consumer excerpt plus the latest anchored producer excerpt as the default pair and include older anchored samples only when they preserve short local chronology that materially improves interpretation
- treat changes to the machine-parsed log prefixes as parser-contract changes; when those prefixes are centralized in `NoOfficeDemandFix/MachineParsedLogContract.cs`, update the Python parser constants and fixtures in the same diff

The current diagnostics vocabulary is:

- `softwareEvidenceDiagnostics observation_window(...)`, including `observation_kind`, `skipped_sample_slots`, and `clock_source` when those fields are emitted
- `environment(settings=..., patch_state=...)`
- `diagnostic_counters(...)`, including `software(...)`, `electronics(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and `softwareConsumerBuyerState(...)` when those counter groups are emitted
- `diagnostic_context(...)`
- `softwareEvidenceDiagnostics detail(...)`, including `detail_type=softwareOfficeStates` when office-level input state is captured for software producers or software consumers; those detail lines may include `softwareNeed(...)`, `softwareTradeCost(...)`, and `softwareAcquisitionState(...)` for software consumers, plus buyer-lifecycle helper fields such as `deliveryMode`, `buyerOrigin`, `noBuyerReason`, persistence counters, and virtual-resolution markers when emitted
- verbose `softwareEvidenceDiagnostics detail(...)` with `detail_type=softwareTradeLifecycle` when lifecycle transitions or seller snapshots are captured, plus `detail_type=softwareVirtualResolutionProbe` when checking the discussion-`#63` zero-weight virtual fast-path hypothesis, and `detail_type=softwareBuyerTimingProbe` when checking whether a selected need stayed below threshold across enough windows to justify a corrective buyer pass; treat all three as supplemental artifacts rather than as a replacement for scheduled observation-window anchors. Keep high-volume fix-specific probes such as `virtualOfficeBuyerFixProbe` as supplemental artifact material unless the active investigation is explicitly about that probe volume or override sizing

Use these contract rules:

- `diagnostic_context` is optional supporting context, not a required top-level evidence field
- copy `diagnostic_context(...)` into `notes` or `log_excerpt` only when it adds useful non-primary context such as `topFactors`
- raw-log automation uses deterministic parsing to extract anchors and excerpt candidates, then uses LLM drafting for initial framing; review the framing, but treat the copied anchors and excerpts as the source of truth
- if machine-parsed log prefixes change, treat that as a parser-contract change; when those prefixes are centralized in `NoOfficeDemandFix/MachineParsedLogContract.cs`, update the Python parser constants and fixtures in the same diff

## Source And Interpretation Guardrails

### Source Discipline

When code reading informs the interpretation:

- use vanilla decompiled game code for claims about base-game trade lifecycle, virtual-resource handling, `TripNeeded` / `CurrentTrading` semantics, and company update behavior
- use this mod's code for claims about emitted diagnostics, local patches, release defaults, and deviations that belong in `patch_state`
- if one conclusion depends on both, say so explicitly in `analysis_basis`, `notes`, or the umbrella summary

### Interpretation Guardrails

Mixed-cause interpretations are allowed. Record them explicitly instead of forcing one root-cause story too early.

- older logs may include `EnableTradePatch` in `settings`; treat it as legacy run context rather than a current independent variable
- current logs may include `EnableOutsideConnectionVirtualSellerFix`; treat it as the explicit Bucket B test variable when you are comparing outside-connection virtual seller behavior
- current logs may include `EnableVirtualOfficeResourceBuyerFix`; treat it as the explicit buyer-cadence corrective-pass test variable when you are comparing the post-vanilla zero-weight office-input buyer top-up rather than treating it as background run noise
- improvement after a historical run with different `EnableTradePatch` state does not prove upstream input pressure was absent
- a large pre/post improvement can still be a downstream bypass of a remaining upstream problem
- persistent producer-side `Electronics(stock=0)` or buyer pressure in `detail_type=softwareOfficeStates` after a trade-patch comparison suggests upstream starvation is still active
- persistent consumer-side `softwareInputZero=true` or repeated `Software(stock=0)` in `detail_type=softwareOfficeStates` suggests downstream software shortage is still active
- do not infer an active buyer, in-flight trip, or current trading state from `tradeCostEntry=True` in `softwareTradeCost(...)` alone
- in current builds, `softwareNeed.tripNeededAmount` mirrors vanilla need selection and counts only `TripNeeded` entries with `Purpose.Shopping`; use `detail_type=softwareTradeLifecycle` when you need the purpose-split trip view
- if `softwareNeed(selected=true)` appears together with `softwareAcquisitionState(...)`, classify it from the logged acquisition fields rather than from missing buyer/trip/path counters alone
- `selected_resolved_virtual_no_tracking_expected` is a normal fast-path candidate for zero-weight software unless other fields contradict it
- `selected_no_resource_buyer`, `selected_resource_buyer_no_path`, and `selected_resolved_no_tracking_unexpected` are the primary anomaly candidates in current builds
- use `detail_type=softwareVirtualResolutionProbe` when the active question is whether a `selected_no_resource_buyer` sample is actually a quickly resolved zero-weight virtual trade rather than a pre-buyer stall
- use `detail_type=softwareBuyerTimingProbe` when the active question is whether a selected below-threshold software need remained buyerless across enough windows to justify the corrective pass or whether the sample only caught a short harmless cadence gap
- do not treat missing `TripNeeded`, `CurrentTrading`, or `path_pending` by themselves as proof of a stall before checking `virtualGood` and `tripTrackingExpected`
- treat `selected_resource_buyer_no_path` as a normal pre-path intermediate state by default, especially for zero-weight virtual goods; escalate it only when persistence or age fields show that the state is lingering abnormally
- treat `selected_no_resource_buyer` as an ambiguous result state until `noBuyerReason`, buyer-origin context, and the relevant age or persistence counters are checked
- when buyerless or pre-path states remain unexplained, prefer adding or preserving reason / age instrumentation before introducing behavior-changing experimental patches
- widespread consumer-side `efficiency=0`, `lackResources=0`, or `softwareInputZero=true` does not by itself prove office demand will fall
- if software-consumer distress persists while `officeDemand(...)` stays flat or rises, record that as contradictory to the original direct software-to-demand assumption
- keep root-cause interpretation in `confounders`, `notes`, or the umbrella summary rather than inventing root-cause `symptom_classification` labels

## Instrumentation Priority For Ambiguous Buyer States

When recurring `selected_no_resource_buyer` or `selected_resource_buyer_no_path` states remain unexplained, prefer schema-compatible instrumentation before behavior-changing experiments.

Instrumentation-first guidance:

- first preserve or add buyer-lifecycle interpretation helpers such as `deliveryMode`, `tripTrackingExpected`, `currentTradingExpected`, `buyerOrigin`, `noBuyerReason`, `selectedNoBuyerConsecutiveWindows`, `selectedRequestNoPathConsecutiveWindows`, `lastBuyerSeenSampleAge`, and `lastVirtualResolutionSampleAge`
- when emitted, preserve `detail_type=softwareBuyerTimingProbe` next to the helper-rich scheduled sample so below-threshold cadence questions stay anchored to the same observation window
- use those helpers to separate short harmless sampling gaps from persistent stalls or repeated re-entry below threshold
- only move to behavior-changing experimental patches after the logs show a stable unexplained reason pattern, or after the age and persistence fields show that a state is lasting long enough to justify intervention

This keeps instrumentation work and fix work separate, which makes later comparisons easier to interpret and keeps the promoted evidence schema stable while buyer-lifecycle analysis evolves.

## Observation Window Guidance

### Recommended Window Lengths

Use day count as the primary rule for reusable windows:

- `3 days`: minimum reusable bounded window for a promoted evidence entry
- `5 days`: preferred for long-window same-lineage acquisition-state or persistence comparison
- `7 days`: preferred when outside-connection state, persistence, or recovery is under review

At the default `DiagnosticsSamplesPerDay=2` cadence, stable-capture windows with no skipped slots usually yield roughly:

- `3 days`: about `6` emitted observation windows
- `5 days`: about `10` emitted observation windows
- `7 days`: about `14` emitted observation windows

### Reading Observation-Window Fields

Interpret the emitted fields this way:

- `sample_slot`: the scheduled slot derived from the runtime `TimeSystem` time-of-day path
- `sample_day`: the logical displayed-clock day reconstructed from slot progression
- `sample_count`: the emitted `observation_window(...)` count in the current run, not a theoretical slot count
- `skipped_sample_slots`: scheduled gaps that elapsed without a backfilled observation
- `clock_source`: normally `runtime_time_system`; older logs may show `displayed_clock` as legacy compatibility for older observation contracts

Practical rules:

- treat the day-count guidance as primary
- scale expected `sample_count` with the configured diagnostics cadence
- treat missed slots as honest gaps, not synthetic backfills

### Time-Scaling Mods

These window recommendations still work under time-scaling mods such as `RealisticTrips` or `Time2Work`, but those comparisons are lower confidence than vanilla-speed runs.

Why:

- the same reported day count can cover more simulation frames
- more simulation frames mean more trade, storage, and company update cycles inside the same nominal day span

When a time-scaling mod is active:

- record the mod and any relevant factor in `other_mods`, `platform_notes`, or `notes`
- treat direct comparisons against vanilla-speed runs as weaker even when day counts or `sample_count` look similar

### Capture Modes

- default diagnostics: enable `EnableDemandDiagnostics=true`, keep `CaptureStableEvidence=false`, and let suspicious-state runs emit evidence only when the state looks interesting
- Bucket B comparison diagnostics: change `EnableOutsideConnectionVirtualSellerFix` only when you are explicitly comparing the outside-connection virtual seller path, and preserve that exact state in the copied `settings`
- buyer-cadence comparison diagnostics: change `EnableVirtualOfficeResourceBuyerFix` only when you are explicitly comparing the corrective post-vanilla buyer pass, and preserve that exact state in the copied `settings`
- baseline capture: enable `CaptureStableEvidence=true` to emit bounded observation windows at the configured per-day cadence even while the city looks stable
- escalation capture: enable `VerboseLogging=true` only when you also need the noisier correction traces beyond the normalized evidence lines

## Standard Comparison Checkpoints

Use the checkpoint that matches the active question. Do not force every investigation through every checkpoint.

#### 1. Comparable same-save rerun

- baseline entry: evidence entry from a save lineage with the relevant counters and detail preserved
- comparison entry: a later bounded run on the same save lineage or a tightly matched rerun of the same scenario
- required invariants: same game version, same mod ref or equivalent release, same save/scenario lineage, same phantom-vacancy setting, same diagnostics setting, comparable observation window; if a historical log still includes `EnableTradePatch`, record it only as legacy run context
- variable under test: the specific hypothesis being checked, such as persistence, lifecycle interpretation, or post-reload stability
- primary fields / counters: `settings`, `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareProducerOffices(...)`, `diagnostic_counters.softwareConsumerOffices(...)`, `diagnostic_counters.softwareConsumerBuyerState(...)` when buyer-lifecycle detail is part of the claim
- invalid comparison cases: save changed in unrelated ways, multiple intended behavior changes were mixed together without a stated hypothesis, or session-boundary effects were mixed in without being noted

### 2. Short-Run Vs Long-Run Observation Window

- question: does the symptom persist or disappear when the same save is observed for longer?
- hold constant: same game version, same mod ref, same settings, same save lineage, no intentional patch changes between windows
- primary evidence: persistence or disappearance of `software(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and repeated `symptom_classification`
- invalid when: the windows include unrelated interventions, reloads, or major simulation changes not present in both windows

### 3. Session Boundary Effect On The Same Save

- question: does reload or restart change the symptom without any intended behavior change?
- hold constant: same game version, same mod ref, same settings, same save lineage, comparable observation window
- primary evidence: `symptom_classification`, `software(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and relevant `softwareEvidenceDiagnostics detail(...)` lines when property state matters
- invalid when: a patch toggle or code change happened between runs, or city state changed materially before capture

- baseline entry: evidence entry before reload or restart
- comparison entry: evidence entry from the same save after reload or restart with no intended behavior change
- required invariants: same game version, same mod ref, same settings, same save/scenario lineage, comparable observation window
- variable under test: session boundary transition
- primary fields / counters: `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareProducerOffices(...)`, `diagnostic_counters.softwareConsumerOffices(...)`, relevant `softwareEvidenceDiagnostics detail(...)` lines if property state changes matter
- invalid comparison cases: any unrelated code or settings change between runs, or materially different city state before capture

- question: does a materially different outside-connection state explain the change?
- hold constant: same save lineage, same game/mod/settings as far as possible, same general hypothesis under test
- primary evidence: `software(resourceProduction, resourceDemand, companies, propertyless)`, `softwareProducerOffices(... lackResourcesZero ...)`, and `softwareConsumerOffices(... softwareInputZero ...)`
- invalid when: outside-connection changes are confounded with patch toggles or unrelated city growth

- baseline entry: evidence entry under one outside-connection availability state
- comparison entry: evidence entry under a materially different outside-connection state
- required invariants: same save lineage, same game/mod/settings as far as possible, same general hypothesis under test
- variable under test: outside-connection availability state
- primary fields / counters: `diagnostic_counters.software(resourceProduction, resourceDemand, companies, propertyless)`, `diagnostic_counters.softwareProducerOffices(... lackResourcesZero ...)`, `diagnostic_counters.softwareConsumerOffices(... softwareInputZero ...)`
- invalid comparison cases: outside-connection change confounded with unrelated settings shifts or unrelated city growth

- question: did the same run show a bounded starvation or recovery transition?
- hold constant: same session or tightly bounded same-save sequence, same settings, same game/mod state
- primary evidence: `software(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and `diagnostic_context(topFactors=...)` when relevant
- invalid when: the windows are too loose to attribute the shift to one transition, or the evidence is mostly anecdotal

### 6. Upstream Input Pressure Vs Downstream Office-Resource Gating

- question: is the observed shortage better explained by upstream input pressure, downstream office-resource gating, or a mixed state?
- hold constant: same game/mod/settings except the variable under test, comparable observation window, no unrelated city change large enough to dominate the signal
- primary evidence: `software(...)`, `electronics(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and relevant `softwareEvidenceDiagnostics detail(...)` lines with `detail_type=softwareOfficeStates`
- interpretation note: record whether the result looks like `mitigated downstream shortage`, `upstream pressure still present`, or `no clear separation`
- invalid when: upstream and downstream conditions changed together, required office-level detail was not preserved, or the windows are too loose to attribute the shift

### 7. Software Consumer Distress Vs Office-Demand Response

- question: does software-consumer distress align with lower office demand, no material demand shift, or rising demand?
- hold constant: same game/mod/settings except the variable under test, comparable observation window, no unrelated city change large enough to dominate office demand
- primary evidence: `officeDemand(...)`, `softwareConsumerOffices(...)`, and relevant `softwareEvidenceDiagnostics detail(...)` lines with `detail_type=softwareOfficeStates`
- interpretation note: treat strong consumer distress with flat or rising `officeDemand(...)` as contradictory to the original direct-demand assumption unless another direct demand mechanism is separately evidenced
- invalid when: office-demand counters were not preserved, unrelated interventions dominated city state, or the demand claim was inferred only from software counters

### 8. Software Need Selection Vs Buyer Lifecycle State

#### 8. Software need selection vs buyer lifecycle state

- baseline entry: evidence entry from a run where `softwareConsumerBuyerState(...)` and consumer-side `softwareOfficeStates` detail were preserved
- comparison entry: a matched evidence entry or later bounded window on the same save lineage where the software-consumer anomaly was sampled again
- required invariants: same game/mod/settings except the variable under test, comparable observation window, and preserved consumer detail with `softwareNeed(...)`, `softwareTradeCost(...)`, and `softwareAcquisitionState(...)`; keep `detail_type=softwareTradeLifecycle` when transition or seller-snapshot evidence is needed
- variable under test: whether zero-software consumers are failing before buyer creation, during a transient buyer/path lifecycle, or after a path resolves without visible trade state
- primary fields / counters: `diagnostic_counters.softwareConsumerBuyerState(...)` plus relevant `softwareEvidenceDiagnostics detail(...)` lines with `detail_type=softwareOfficeStates`; use `detail_type=softwareTradeLifecycle` as supplemental evidence when seller snapshots or path-transition chronology matter, and `detail_type=softwareVirtualResolutionProbe` when the question is whether zero-weight virtual resolution left only indirect evidence
- interpretation guidance: prefer this checkpoint over simply extending the off/on window when the active question is whether selected software consumers are stuck before request creation or are taking a zero-weight virtual-resource fast path; when helper fields are available, interpret the checkpoint through `deliveryMode`, `buyerOrigin`, `noBuyerReason`, persistence counters, and virtual-resolution markers before proposing a behavior-changing patch
- invalid comparison cases: consumer detail was not preserved, `tradeCostEntry` was read as buyer proof, or the windows were too sparse to tell same-sample buyer state from a transient trace

#### 9. Buyer-cadence corrective pass on the same save

- baseline entry: evidence entry from a run with `EnableVirtualOfficeResourceBuyerFix=false` where `softwareConsumerBuyerState(...)` and consumer-side detail were preserved
- comparison entry: a matched evidence entry on the same save lineage with `EnableVirtualOfficeResourceBuyerFix=true` and otherwise comparable settings
- required invariants: same game version, same mod ref, same save/scenario lineage, same outside-connection seller state, comparable observation window, and preserved consumer detail with `softwareNeed(...)`, `softwareAcquisitionState(...)`, and `detail_type=softwareBuyerTimingProbe` when emitted
- variable under test: whether the corrective post-vanilla buyer pass changes repeated below-threshold buyerless states without conflating seller-eligibility changes
- primary fields / counters: copied `settings`, `diagnostic_counters.softwareConsumerBuyerState(...)`, relevant consumer-side `detail_type=softwareOfficeStates` excerpts, `detail_type=softwareBuyerTimingProbe` when emitted, and `virtualOfficeBuyerFixProbe` summaries when fix volume or override sizing matters
- interpretation guidance: treat this as a follow-up checkpoint after seller-eligibility comparisons rather than as a replacement for them; improvement here can indicate a residual cadence gap after outside-connection seller eligibility is addressed
- invalid comparison cases: the outside-connection seller state changed at the same time, consumer detail or timing probes were not preserved, or the comparison mixes unrelated city changes with the buyer-fix toggle

## Canonical Comparison Summary Shape

Use this small reusable shape inside the umbrella investigation body or a follow-up comment:

- `checkpoint`: which standard checkpoint was used
- `baseline_ref`: the baseline evidence entry or run reference
- `comparison_ref`: the comparison evidence entry or run reference
- `invariant_status`: whether the required invariants held, and if not, what broke comparability
- `observed_deltas`: the relevant changes in counters, symptom labels, and preserved detail lines
- `outcome`: `supportive`, `contradictory`, `no material change`, or `invalid comparison`
- `notes`: any narrow context that matters for later reuse

Example:

```md
- checkpoint: same_save_rerun
  baseline_ref: #21
  comparison_ref: #22
  invariant_status: same save lineage, same game/mod/settings except the explicitly stated variable under test; any historical `EnableTradePatch` value recorded only as legacy run context
  observed_deltas: softwareConsumerOffices.softwareInputZero dropped from 18 to 4, but softwareProducerOffices.lackResourcesZero persisted
  outcome: mitigated but not solved
  notes: compared after one reload boundary
```

## Decision Model

### Per-Comparison Outcomes

Classify each comparison first:

- `supportive`: the comparison meaningfully supports the hypothesis under test
- `contradictory`: the comparison meaningfully undercuts the hypothesis under test
- `no material change`: the comparison is valid but does not show a meaningful directional shift
- `invalid comparison`: the required invariants failed, so the result should not drive interpretation

### Overall Conclusions

After recording per-comparison outcomes, choose one overall conclusion:

- `not reproduced`
- `inconclusive`
- `plausible but weakly supported`
- `strongly supported`
- `mitigated but not solved`
- `contradicted by evidence`

Use these entry conditions:

- `not reproduced`: valid targeted comparisons fail to show the expected symptom and there is no supportive evidence
- `inconclusive`: all comparisons are `invalid comparison`, or valid comparisons point in mixed directions, or the evidence is too sparse or confounded
- `plausible but weakly supported`: at least one valid `supportive` comparison exists, but the set is sparse, narrow, or materially confounded
- `strongly supported`: multiple valid `supportive` comparisons, or one strong targeted comparison with minimal confounders, point in the same direction
- `mitigated but not solved`: a valid intervention comparison shows improvement, but the symptom persists
- `contradicted by evidence`: valid comparisons directly undercut the hypothesis and there is no meaningful supportive evidence

Conservative precedence rules:

- if all comparisons are `invalid comparison`, overall is `inconclusive`
- if valid comparisons point in mixed directions, default overall is `inconclusive`
- `mitigated but not solved` is the only explicit mixed-state exception
- use `mitigated but not solved` only when a valid intervention comparison shows improvement but the symptom still persists

## Repo-Facing Wording Map

Use these phrasing examples when issue summaries, README text, or release notes need repository-facing wording:

- `not reproduced` -> `not reproduced in the current investigation scope`
- `inconclusive` -> `evidence remains inconclusive`
- `plausible but weakly supported` -> `plausible but weakly supported by current evidence`
- `strongly supported` -> `strongly supported by current evidence`
- `mitigated but not solved` -> `appears mitigated under tested conditions, not solved`
- `contradicted by evidence` -> `current evidence contradicts the hypothesis`

## Decision Record References

The current workflow definition was derived from these issue decisions:

- `#15` canonical evidence field set
- `#16` standard comparison checkpoints
- `#17` decision rules for conclusions
- `#18` stage workflow narrative
- `#19` reusable evidence entry form
