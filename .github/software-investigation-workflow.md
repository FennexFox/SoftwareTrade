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
- use `analysis_basis` only when code reading actually informed the interpretation, and say whether the claim comes from vanilla decompile, mod code, or both

### 4. Preserve The Right Counter Bundle For The Question

When the active question is:

- office-demand response: preserve `officeDemand(...)` together with the relevant software counters instead of describing demand movement only in prose
- buyer-state anomaly on software consumers: preserve `softwareConsumerBuyerState(...)` together with the relevant `softwareNeed(...)`, `softwareTradeCost(...)`, `softwareBuyerState(...)`, and `softwareTrace(...)` blocks
- upstream input pressure vs downstream office-resource gating: preserve `electronics(...)`, `software(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and any relevant `detail_type=softwareOfficeStates` lines together

Producer-side trade-cost metadata is a special case:

- keep the concise producer `input1(...)` / `input2(...)` formatter stock-only by default
- if producer-side trade-cost metadata becomes part of the active question, add or use a separate verbose diagnostic path instead of re-expanding the concise formatter

### 5. Keep Sampling Metadata Intact

When diagnostics are sampled more than once per day, preserve the emitted sampling metadata rather than flattening it away.

- treat `sample_count` as emitted observation density inside the current run, not as a replacement for the day fields
- treat `skipped_sample_slots` as honest reporting for missed scheduled slots, not as backfilled observations

## Diagnostics Vocabulary And Contract

The current diagnostics vocabulary is:

- `softwareEvidenceDiagnostics observation_window(...)`, including `observation_kind`, `skipped_sample_slots`, and `clock_source` when those fields are emitted
- `environment(settings=..., patch_state=...)`
- `diagnostic_counters(...)`, including `software(...)`, `electronics(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and `softwareConsumerBuyerState(...)` when those counter groups are emitted
- `diagnostic_context(...)`
- `softwareEvidenceDiagnostics detail(...)`, including `detail_type=softwareOfficeStates` when office-level input state is captured for software producers or software consumers; these detail lines may include `softwareNeed(...)`, `softwareTradeCost(...)`, `softwareBuyerState(...)`, and `softwareTrace(...)` for software consumers

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

- improvement after `EnableTradePatch` does not prove upstream input pressure was absent
- a large pre/post improvement can still be a downstream bypass of a remaining upstream problem
- persistent producer-side `Electronics(stock=0)` or buyer pressure in `detail_type=softwareOfficeStates` suggests upstream starvation may still be active
- persistent consumer-side `softwareInputZero=true` or repeated `Software(stock=0)` in `detail_type=softwareOfficeStates` suggests downstream software shortage may still be active
- do not infer an active buyer, in-flight trip, or current trading state from `tradeCostEntry=True` alone
- if `softwareNeed(selected=true)` appears together with `softwareBuyerState(buyerActive=false, tripNeededCount=0, currentTradingCount=0, pathState=none)`, record that as a buyer-lifecycle anomaly rather than assuming the empty trade state is expected
- widespread consumer-side `efficiency=0`, `lackResources=0`, or `softwareInputZero=true` does not by itself prove office demand will fall
- if software-consumer distress persists while `officeDemand(...)` stays flat or rises, record that as contradictory to the original direct software-to-demand assumption
- keep root-cause interpretation in `confounders`, `notes`, or the umbrella summary rather than inventing root-cause `symptom_classification` labels

## Observation Windows And Capture Modes

### Recommended Window Lengths

Use day count as the primary rule for reusable windows:

- `3 days`: minimum reusable bounded window for a promoted evidence entry
- `5 days`: preferred for `EnableTradePatch` off/on comparisons on the same save lineage
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
- baseline capture: enable `CaptureStableEvidence=true` to emit bounded observation windows at the configured per-day cadence even while the city looks stable
- escalation capture: enable `VerboseLogging=true` only when you also need noisier correction and patch traces beyond the normalized evidence lines

## Standard Comparison Checkpoints

Use the checkpoint that matches the active question. Do not force every investigation through every checkpoint.

### 1. Trade Patch Toggle On The Same Save

- question: did toggling `EnableTradePatch` change the symptom on the same save lineage?
- hold constant: same game version, same mod ref or equivalent release, same save lineage, same phantom-vacancy setting, same diagnostics setting, comparable observation window
- primary evidence: `settings`, `symptom_classification`, `software(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and `softwareConsumerBuyerState(...)` when buyer-lifecycle detail matters
- invalid when: unrelated save changes occurred, multiple settings changed together, or session-boundary effects were mixed in without being noted

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

### 4. Outside-Connection Availability State

- question: does a materially different outside-connection state explain the change?
- hold constant: same save lineage, same game/mod/settings as far as possible, same general hypothesis under test
- primary evidence: `software(resourceProduction, resourceDemand, companies, propertyless)`, `softwareProducerOffices(... lackResourcesZero ...)`, and `softwareConsumerOffices(... softwareInputZero ...)`
- invalid when: outside-connection changes are confounded with patch toggles or unrelated city growth

### 5. Starvation Or Recovery Transition Within A Run

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

- question: are zero-software consumers failing before buyer creation, during a transient buyer/path lifecycle, or after a path resolves without visible trade state?
- hold constant: same game/mod/settings except the variable under test, comparable observation window, and preserved consumer detail with `softwareNeed(...)`, `softwareTradeCost(...)`, `softwareBuyerState(...)`, and `softwareTrace(...)`
- primary evidence: `softwareConsumerBuyerState(...)` plus relevant `softwareEvidenceDiagnostics detail(...)` lines with `detail_type=softwareOfficeStates`
- interpretation note: prefer this checkpoint over simply extending an off/on window when the active question is why `tradeCostEntry=True` coexists with `buyerActive=false`, `tripNeededCount=0`, `currentTradingCount=0`, and `pathState=none`
- invalid when: consumer detail was not preserved, `tradeCostEntry` was read as buyer proof, or the windows were too sparse to distinguish same-sample buyer state from a transient trace

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
- checkpoint: trade_patch_toggle_same_save
  baseline_ref: #21
  comparison_ref: #22
  invariant_status: same save lineage, same game/mod/settings except EnableTradePatch
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
