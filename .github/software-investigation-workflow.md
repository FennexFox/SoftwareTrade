# Software Investigation Workflow

This document defines the practical maintainer workflow for collecting, promoting, comparing, and summarizing `software`-track evidence without flooding the issue tracker.

It is the maintainer-facing companion to [`.github/software-evidence-schema.md`](./software-evidence-schema.md). The schema defines what a normalized evidence entry must contain. This workflow defines when to keep something as raw data, when to promote it into a reusable evidence issue, and where comparisons and conclusions should live.

## Inputs

The workflow uses four inputs:

- raw `softwareEvidenceDiagnostics` logs emitted by diagnostics
- local artifacts such as logs, saves, screenshots, or videos
- the reusable evidence issue form at [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./ISSUE_TEMPLATE/software_evidence.yml)
- the reusable umbrella investigation issue form at [`.github/ISSUE_TEMPLATE/software_investigation.yml`](./ISSUE_TEMPLATE/software_investigation.yml)

## Working Levels

Treat the investigation as three levels of material:

1. Raw capture
   Diagnostics logs, temporary notes, saves, screenshots, and ad hoc observations. Do not create a new issue for every raw capture.

2. Promoted evidence entry
   A bounded observation window that is worth reusing later. This is when you open a `software evidence` issue.

3. Umbrella investigation
   The tracker for one hypothesis or investigation line. This is where evidence issues are linked, checkpoint comparisons are summarized, and the current conclusion is maintained.

## When To Open A Software Evidence Issue

Open a `software evidence` issue only when the run is evidence-worthy:

- the observation window is bounded and specific
- the run has enough context to stand on its own later
- the run is likely to be reused in a later comparison
- the run is more than a temporary note or raw sample

If those conditions are not met, keep the material in local artifacts or summarize it in the umbrella investigation issue instead of opening a new evidence issue.

## Log-To-Evidence Capture

When promoting a run into a `software evidence` issue:

1. copy `softwareEvidenceDiagnostics observation_window(...)`
2. copy `settings=...`
3. copy `patch_state=...`
4. copy `diagnostic_counters(...)`
5. store relevant `softwareEvidenceDiagnostics detail(...)` lines in artifacts or notes when property-level or office-level input state matters
6. add only the minimum investigator-written context needed to make the run reusable

Capture guidance:

- prefer copying counters directly from logs rather than paraphrasing them
- keep the full bounded observation window string when possible so `session_id`, `run_id`, `start_day`, `end_day`, `sample_index`, and `sample_slot` fields stay available for later comparisons
- treat `diagnostic_counters` as factual capture
- treat `diagnostic_counters` as the sampled end-of-window state unless you explicitly note a wider aggregation method
- when the claim touches office-demand response, preserve `officeDemand(...)` alongside the software counters instead of summarizing demand behavior in prose only
- keep `evidence_summary` short and descriptive, not argumentative
- use `confidence` and `confounders` only for uncertainty that cannot be represented as counters or metadata
- do not treat `symptom_classification` as proof of root cause
- use `analysis_basis` only when code reading actually informed the interpretation, and say whether the relevant claim came from vanilla decompile, mod code, or both
- if runtime emits `patch_state=unknown`, keep that value unless you can replace it with an exact known local deviation set
- when differentiating upstream input pressure from downstream software-consumer shortage or office-resource trade and storage gating, prefer preserving `electronics(...)`, `software(...)`, `softwareProducerOffices(...)`, `softwareConsumerOffices(...)`, and any relevant `detail_type=softwareOfficeStates` lines together
- `sample_count` now counts configured per-day samples rather than whole in-game days, so use it as a density hint rather than a replacement for the day fields

The current diagnostics vocabulary is:

- `softwareEvidenceDiagnostics observation_window(...)`
- `environment(settings=..., patch_state=...)`
- `diagnostic_counters(...)`, including `software(...)`, `electronics(...)`, `softwareProducerOffices(...)`, and `softwareConsumerOffices(...)` when those counter groups are emitted
- `diagnostic_context(...)`
- `softwareEvidenceDiagnostics detail(...)`, including `detail_type=softwareOfficeStates` when office-level input state is captured for software producers or software consumers; those detail lines may also include trade-cost-entry, active-buyer, trip-needed, current-trading, and path-state cues for software consumers

`diagnostic_context` is not itself a required top-level evidence field, but it can be copied into `notes` or `log_excerpt` when it adds useful non-primary context such as `topFactors`.

## Analysis Source Guidance

When code reading is part of the interpretation, keep the source of the claim explicit.

- use vanilla decompiled game code for claims about the base-game trade lifecycle, virtual-resource handling, `TripNeeded` / `CurrentTrading` semantics, and company update behavior
- use this mod's code for claims about emitted diagnostics, local patches, release defaults, and any deviations that belong in `patch_state`
- if one conclusion depends on both, say so explicitly in `analysis_basis`, `notes`, or the umbrella investigation summary

## Interpretation Guidance

Mixed-cause interpretations are allowed and should be recorded explicitly rather than collapsed into one presumed root cause.

- improvement after `EnableTradePatch` does not prove upstream input pressure was absent
- a large pre/post improvement can still be a downstream bypass of a remaining upstream problem
- persistent producer-side `Electronics(stock=0)` or buyer pressure in `detail_type=softwareOfficeStates` after a trade-patch comparison suggests upstream starvation is still active
- persistent consumer-side `softwareInputZero=true` or repeated `Software(stock=0)` in `detail_type=softwareOfficeStates` suggests downstream software shortage is still active
- widespread consumer-side `efficiency=0`, `lackResources=0`, or `softwareInputZero=true` does not by itself prove office demand will fall
- if software-consumer distress persists while `officeDemand(...)` stays flat or rises, record that as contradictory to the original direct software-to-demand assumption rather than hand-waving it away
- keep root-cause interpretation in `confounders`, `notes`, or the umbrella investigation summary rather than inventing new root-cause `symptom_classification` labels

## Observation Window Guidance

Default minimum window guidance:

- `3 days`: minimum reusable bounded window for a promoted evidence entry
- `5 days`: preferred for `EnableTradePatch` off/on comparison on the same save lineage
- `7 days`: preferred when outside-connection state, persistence, or recovery is under review

At the default `DiagnosticsSamplesPerDay=2` cadence, those windows will usually yield roughly:

- `3 days`: about `6` samples
- `5 days`: about `10` samples
- `7 days`: about `14` samples

Use the day-count recommendation as the primary rule. Treat the higher `sample_count` as denser evidence inside the same day-count window, not as a replacement for the day count itself. If `DiagnosticsSamplesPerDay` is set differently, scale the expected `sample_count` accordingly.

These day-count recommendations remain valid under time-scaling mods such as `RealisticTrips` / `Time2Work`.
When such a mod lengthens the in-game day, the same reported day count spans more simulation frames and therefore more trade, storage, and company update cycles.
The current sampling code follows the displayed in-game day via patched time-of-day state, so `DiagnosticsSamplesPerDay=2` still means roughly two samples per reported day rather than two samples per vanilla-length day.
That keeps the `3` / `5` / `7` day guidance conservative rather than weaker.

Comparability guidance:

- evidence gathered with materially different time-scaling settings is weaker to compare directly, even if the observation window shows the same day count or `sample_count`
- when a time-scaling mod is active, record the mod and any relevant factor in `other_mods`, `platform_notes`, or `notes`

## Capture Modes

- default diagnostics: enable `EnableDemandDiagnostics=true`, keep `CaptureStableEvidence=false`, and let suspicious-state runs emit evidence only when the state looks interesting
- baseline capture: enable `CaptureStableEvidence=true` to emit bounded observation windows at the configured per-day cadence even while the city looks stable
- escalation capture: enable `VerboseLogging=true` only when you also need the noisier correction and patch traces beyond the normalized evidence lines

## Standard Comparison Checkpoints

### Core Checkpoints

#### 1. Trade patch toggle on the same save

- baseline entry: evidence entry from a save with `EnableTradePatch=false`
- comparison entry: evidence entry from the same save lineage after switching to `EnableTradePatch=true`
- required invariants: same game version, same mod ref or equivalent release, same save/scenario lineage, same phantom-vacancy setting, same diagnostics setting, comparable observation window
- variable under test: `EnableTradePatch`
- primary fields / counters: `settings`, `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareProducerOffices(...)`, `diagnostic_counters.softwareConsumerOffices(...)`
- invalid comparison cases: save changed in unrelated ways, multiple settings changed together, session-boundary effects mixed in without being noted

#### 2. Short-run vs long-run observation window

- baseline entry: short bounded observation window on a stable save
- comparison entry: longer bounded observation window on the same save and settings
- required invariants: same game version, same mod ref, same settings, same save/scenario lineage, no intentional patch changes between windows
- variable under test: observation window duration
- primary fields / counters: persistence or disappearance of `software(...)`, `softwareProducerOffices(...)`, and `softwareConsumerOffices(...)` counters, plus any repeated `symptom_classification`
- invalid comparison cases: windows that include unrelated interventions, reloads, or major simulation changes not present in both windows

### Situational Checkpoints

#### 3. Session boundary effect on the same save

- baseline entry: evidence entry before reload or restart
- comparison entry: evidence entry from the same save after reload or restart with no intended behavior change
- required invariants: same game version, same mod ref, same settings, same save/scenario lineage, comparable observation window
- variable under test: session boundary transition
- primary fields / counters: `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareProducerOffices(...)`, `diagnostic_counters.softwareConsumerOffices(...)`, relevant `softwareEvidenceDiagnostics detail(...)` lines if property state changes matter
- invalid comparison cases: any patch toggle or code change between runs, or materially different city state before capture

#### 4. Outside-connection availability state

- baseline entry: evidence entry under one outside-connection availability state
- comparison entry: evidence entry under a materially different outside-connection state
- required invariants: same save lineage, same game/mod/settings as far as possible, same general hypothesis under test
- variable under test: outside-connection availability state
- primary fields / counters: `diagnostic_counters.software(resourceProduction, resourceDemand, companies, propertyless)`, `diagnostic_counters.softwareProducerOffices(... lackResourcesZero ...)`, `diagnostic_counters.softwareConsumerOffices(... softwareInputZero ...)`
- invalid comparison cases: outside-connection change confounded with patch toggles or unrelated city growth

#### 5. Starvation or recovery transition within a run

- baseline entry: bounded window before counters indicate starvation or recovery
- comparison entry: bounded window after the change is visible in diagnostics
- required invariants: same session or tightly bounded same-save sequence, same settings, same game/mod state
- variable under test: observed starvation/recovery transition
- primary fields / counters: `diagnostic_counters.software(...)`, `diagnostic_counters.softwareProducerOffices(...)`, `diagnostic_counters.softwareConsumerOffices(...)`, `diagnostic_context(topFactors=...)` when relevant
- invalid comparison cases: windows too loose to attribute the change to one transition, or evidence mostly anecdotal rather than diagnostics-backed

#### 6. Upstream input pressure vs downstream office-resource gating

- baseline entry: evidence entry from a run where the `software` symptom is present and the relevant raw counters or detail lines were preserved
- comparison entry: a matched evidence entry on the same save lineage or tightly controlled scenario where downstream office-resource availability changed enough to test separation, without intentionally changing the rest of the scenario more than necessary
- required invariants: same game/mod/settings except the variable under test, comparable observation window, and no unrelated city change large enough to dominate the signal
- variable under test: whether the observed shortage is better explained by upstream input pressure, downstream office-resource gating, or a mixed state
- primary fields / counters: `diagnostic_counters.software(...)`, `diagnostic_counters.electronics(...)`, `diagnostic_counters.softwareProducerOffices(...)`, `diagnostic_counters.softwareConsumerOffices(...)`, and relevant `softwareEvidenceDiagnostics detail(...)` lines with `detail_type=softwareOfficeStates`
- interpretation guidance: use `notes` or the umbrella summary to record whether the comparison looks like `mitigated downstream shortage`, `upstream pressure still present`, or `no clear separation`
- invalid comparison cases: upstream and downstream conditions changed together, office-level detail was needed but not preserved, or the windows are too loose to attribute the shift

#### 7. Software consumer distress vs office-demand response

- baseline entry: evidence entry from a run where `softwareConsumerOffices(...)` indicates clear consumer distress and `officeDemand(...)` was preserved
- comparison entry: a matched evidence entry or later bounded window on the same save lineage where office-demand response was checked directly rather than inferred
- required invariants: same game/mod/settings except the variable under test, comparable observation window, and no unrelated city change large enough to dominate office demand
- variable under test: whether software-consumer distress aligns with lower office demand, no material demand shift, or rising demand
- primary fields / counters: `diagnostic_counters.officeDemand(...)`, `diagnostic_counters.softwareConsumerOffices(...)`, and relevant `softwareEvidenceDiagnostics detail(...)` lines with `detail_type=softwareOfficeStates`
- interpretation guidance: treat `softwareConsumerOffices` distress with flat or rising `officeDemand(...)` as contradictory to the original direct-demand assumption unless another direct demand mechanism is separately evidenced
- invalid comparison cases: office-demand counters were not preserved, unrelated interventions dominated the city state, or the demand claim was inferred only from software counters

## Canonical Comparison Summary Shape

Comparison summaries should use this small reusable shape inside the umbrella investigation issue body or a follow-up comment:

- `checkpoint`: which standard checkpoint was used
- `baseline_ref`: the baseline evidence entry or run reference
- `comparison_ref`: the comparison evidence entry or run reference
- `invariant_status`: whether the required invariants held, and if not, what broke comparability
- `observed_deltas`: the relevant changes in `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareProducerOffices(...)`, `diagnostic_counters.softwareConsumerOffices(...)`, `diagnostic_counters.officeDemand(...)` when demand response is part of the claim, office demand / vacancy counters when relevant, and any relevant `softwareEvidenceDiagnostics detail(...)` lines
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

Each valid comparison result should first be classified at the checkpoint level using one of these labels:

- `supportive`
- `contradictory`
- `no material change`
- `invalid comparison`

Per-comparison entry conditions:

- `supportive`: the comparison meaningfully supports the hypothesis under test
- `contradictory`: the comparison meaningfully undercuts the hypothesis under test
- `no material change`: the comparison is valid but does not show a meaningful directional shift for the hypothesis under test
- `invalid comparison`: the required invariants failed, so the result should not be used for directional interpretation

### Overall Conclusions

After per-comparison outcomes are recorded, the overall conclusion should use one of these categories:

- `not reproduced`
- `inconclusive`
- `plausible but weakly supported`
- `strongly supported`
- `mitigated but not solved`
- `contradicted by evidence`

Overall conclusion entry conditions:

- `not reproduced`: valid targeted comparisons fail to show the expected symptom and there is no supportive evidence
- `inconclusive`: all comparisons are `invalid comparison`, or valid comparisons point in mixed directions, or evidence is too sparse or confounded to choose a directional conclusion
- `plausible but weakly supported`: at least one valid `supportive` comparison exists, but the set is sparse, narrow, or materially confounded
- `strongly supported`: multiple valid `supportive` comparisons, or one strong targeted comparison with minimal confounders, point in the same direction
- `mitigated but not solved`: a valid intervention comparison shows improvement, but the symptom persists in the comparison set
- `contradicted by evidence`: valid comparisons directly undercut the hypothesis and there is no meaningful supportive evidence

Conservative precedence rules:

- if all comparisons are `invalid comparison`, overall is `inconclusive`
- if valid comparisons point in mixed directions, default overall is `inconclusive`
- `mitigated but not solved` is the only explicit mixed-state exception
- use `mitigated but not solved` only when a valid intervention comparison shows improvement but the symptom still persists

## Repo-Facing Wording Map

Use these phrasing examples when release notes, README text, or issue summaries need repository-facing wording:

- `not reproduced` -> `not reproduced in the current investigation scope`
- `inconclusive` -> `evidence remains inconclusive`
- `plausible but weakly supported` -> `plausible but weakly supported by current evidence`
- `strongly supported` -> `strongly supported by current evidence`
- `mitigated but not solved` -> `appears mitigated under tested conditions, not solved`
- `contradicted by evidence` -> `current evidence contradicts the hypothesis`

## Stage Workflow

1. Choose or reproduce a scenario.
   Select the save, city state, or bounded test condition that will be observed.

2. Capture raw material.
   Keep logs, saves, screenshots, and temporary notes without opening new issues for every sample.

3. Promote evidence-worthy runs.
   Open a `software evidence` issue only for bounded runs that are reusable later.

4. Link evidence under one umbrella investigation.
   Use a `software investigation` issue as the tracker for one hypothesis or investigation line.

5. Record checkpoint comparisons in the umbrella issue.
   Summarize comparisons in the umbrella issue body or follow-up comments using the canonical comparison shape.

6. Derive and update the current conclusion.
   Apply the precedence rules in the umbrella issue as evidence accumulates.

7. Record the repo-facing implication.
   Use the wording map so issue summaries, README language, and release-facing interpretation stay aligned with the underlying evidence category.

## Decision Record References

The current workflow definition was derived from these issue decisions:

- `#15` canonical evidence field set
- `#16` standard comparison checkpoints
- `#17` decision rules for conclusions
- `#18` stage workflow narrative
- `#19` reusable evidence entry form
