# Software Investigation Workflow

This document defines the operational workflow for collecting, comparing, classifying, and summarizing `software`-track evidence.

It is the maintainer-facing companion to [`.github/software-evidence-schema.md`](./software-evidence-schema.md). The schema defines what a normalized evidence entry must contain. This workflow defines how that evidence should be captured, compared, interpreted, and summarized.

## Inputs

The workflow uses four inputs:

- a normalized evidence entry that follows [`.github/software-evidence-schema.md`](./software-evidence-schema.md)
- `softwareEvidenceDiagnostics` logs emitted by diagnostics
- the reusable evidence issue form at [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./ISSUE_TEMPLATE/software_evidence.yml)
- the reusable comparison issue form at [`.github/ISSUE_TEMPLATE/software_comparison.yml`](./ISSUE_TEMPLATE/software_comparison.yml)

## Log-To-Evidence Capture

When current diagnostics logs are available:

1. copy `softwareEvidenceDiagnostics observation_window(...)`
2. copy `settings=...`
3. copy `patch_state=...`
4. copy `diagnostic_counters(...)`
5. store relevant `softwareEvidenceDiagnostics detail(...)` lines in artifacts or notes when property-level state matters
6. keep interpretation separate from capture as much as practical

Capture guidance:

- prefer copying counters directly from logs rather than paraphrasing them
- keep the full bounded observation window string when possible so `session_id`, `run_id`, `start_day`, and `end_day` stay available for later comparisons
- treat `diagnostic_counters` as factual capture
- treat `diagnostic_counters` as the sampled end-of-window state unless you explicitly note a wider aggregation method
- keep `evidence_summary` short and descriptive, not argumentative
- use `confidence` and `confounders` only for uncertainty that cannot be represented as counters or metadata
- do not treat `symptom_classification` as proof of root cause
- if runtime emits `patch_state=unknown`, keep that value unless you can replace it with an exact known local deviation set

The current diagnostics vocabulary is:

- `softwareEvidenceDiagnostics observation_window(...)`
- `environment(settings=..., patch_state=...)`
- `diagnostic_counters(...)`
- `diagnostic_context(...)`
- `softwareEvidenceDiagnostics detail(...)`

`diagnostic_context` is not itself a required top-level evidence field, but it can be copied into `notes` or `log_excerpt` when it adds useful non-primary context such as `topFactors`.

## Capture Modes

- default diagnostics: enable `EnableDemandDiagnostics=true`, keep `CaptureStableEvidence=false`, and let suspicious-state runs emit evidence only when the state looks interesting
- baseline capture: enable `CaptureStableEvidence=true` to emit daily bounded observation windows even while the city looks stable
- escalation capture: enable `VerboseLogging=true` only when you also need the noisier correction and patch traces beyond the normalized evidence lines

## Standard Comparison Checkpoints

### Core Checkpoints

#### 1. Trade patch toggle on the same save

- baseline entry: evidence entry from a save with `EnableTradePatch=false`
- comparison entry: evidence entry from the same save lineage after switching to `EnableTradePatch=true`
- required invariants: same game version, same mod ref or equivalent release, same save/scenario lineage, same phantom-vacancy setting, same diagnostics setting, comparable observation window
- variable under test: `EnableTradePatch`
- primary fields / counters: `settings`, `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareOffices(...)`
- invalid comparison cases: save changed in unrelated ways, multiple settings changed together, session-boundary effects mixed in without being noted

#### 2. Short-run vs long-run observation window

- baseline entry: short bounded observation window on a stable save
- comparison entry: longer bounded observation window on the same save and settings
- required invariants: same game version, same mod ref, same settings, same save/scenario lineage, no intentional patch changes between windows
- variable under test: observation window duration
- primary fields / counters: persistence or disappearance of `software(...)` and `softwareOffices(...)` counters, plus any repeated `symptom_classification`
- invalid comparison cases: windows that include unrelated interventions, reloads, or major simulation changes not present in both windows

### Situational Checkpoints

#### 3. Session boundary effect on the same save

- baseline entry: evidence entry before reload or restart
- comparison entry: evidence entry from the same save after reload or restart with no intended behavior change
- required invariants: same game version, same mod ref, same settings, same save/scenario lineage, comparable observation window
- variable under test: session boundary transition
- primary fields / counters: `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareOffices(...)`, relevant `softwareEvidenceDiagnostics detail(...)` lines if property state changes matter
- invalid comparison cases: any patch toggle or code change between runs, or materially different city state before capture

#### 4. Outside-connection availability state

- baseline entry: evidence entry under one outside-connection availability state
- comparison entry: evidence entry under a materially different outside-connection state
- required invariants: same save lineage, same game/mod/settings as far as possible, same general hypothesis under test
- variable under test: outside-connection availability state
- primary fields / counters: `diagnostic_counters.software(resourceProduction, resourceDemand, companies, propertyless)`, `diagnostic_counters.softwareOffices(... lackResourcesZero ...)`
- invalid comparison cases: outside-connection change confounded with patch toggles or unrelated city growth

#### 5. Starvation or recovery transition within a run

- baseline entry: bounded window before counters indicate starvation or recovery
- comparison entry: bounded window after the change is visible in diagnostics
- required invariants: same session or tightly bounded same-save sequence, same settings, same game/mod state
- variable under test: observed starvation/recovery transition
- primary fields / counters: `diagnostic_counters.software(...)`, `diagnostic_counters.softwareOffices(...)`, `diagnostic_context(topFactors=...)` when relevant
- invalid comparison cases: windows too loose to attribute the change to one transition, or evidence mostly anecdotal rather than diagnostics-backed

## Minimal Comparison Result Shape

Comparison outputs should use a small reusable shape so results can be referenced across issues and release planning:

- `checkpoint`: which standard checkpoint was used
- `baseline_ref`: the baseline evidence entry or run reference
- `comparison_ref`: the comparison evidence entry or run reference
- `invariant_status`: whether the required invariants held, and if not, what broke comparability
- `observed_deltas`: the relevant changes in `symptom_classification`, `diagnostic_counters.software(...)`, `diagnostic_counters.softwareOffices(...)`, office demand / vacancy counters when relevant, and any relevant `softwareEvidenceDiagnostics detail(...)` lines
- `notes`: any narrow context that matters for later reuse

Use [`.github/ISSUE_TEMPLATE/software_comparison.yml`](./ISSUE_TEMPLATE/software_comparison.yml) when you want a reusable issue-backed record for that shape.

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

2. Capture a normalized evidence entry.
   Use the canonical schema, the reusable evidence issue form, and the `softwareEvidenceDiagnostics` log vocabulary to record the observation.

3. Run the standard comparison checkpoints.
   Apply the relevant checkpoint definitions, keeping required invariants and variables under test explicit.

4. Record a minimal comparison result for each checkpoint used.
   Each comparison result should record the checkpoint, baseline reference, comparison reference, invariant status, observed deltas, and notes. Prefer the reusable comparison issue form when the result needs to be referenced later.

5. Classify each comparison result.
   Apply the per-comparison outcome labels: `supportive`, `contradictory`, `no material change`, or `invalid comparison`.

6. Derive an overall conclusion.
   Apply the precedence rules to classify the overall result as `not reproduced`, `inconclusive`, `plausible but weakly supported`, `strongly supported`, `mitigated but not solved`, or `contradicted by evidence`.

7. Record the repo-facing implication.
   Use the wording map so issue summaries, README language, and release-facing interpretation stay aligned with the underlying evidence category.

## Decision Record References

The current workflow definition was derived from these issue decisions:

- `#15` canonical evidence field set
- `#16` standard comparison checkpoints
- `#17` decision rules for conclusions
- `#18` stage workflow narrative
- `#19` reusable evidence entry form
