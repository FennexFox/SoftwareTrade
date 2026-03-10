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
- `mod_ref`: branch name and commit SHA, or another exact source reference
- `settings`: state of `EnableTradePatch`, `EnablePhantomVacancyFix`, `EnableDemandDiagnostics`, `CaptureStableEvidence`, and `VerboseLogging`
- `patch_state`: any local deviations from a normal release build, including extra logging, local patches, or disabled systems; use `unknown` when the runtime cannot determine them reliably

Optional:

- `platform_notes`: anything relevant about platform or install layout
- `other_mods`: only if they may affect office demand, trade, property, or company behavior

### 2. Scenario

Scenario fields describe what was being observed.

Required:

- `scenario_label`: save identity, test city name, or a stable scenario label
- `scenario_type`: existing save, fresh city, reproduced test case, or another short classification
- `reproduction_conditions`: what the tester did or what state the city was already in
- `observation_window`: the bounded time span observed, preferably copied from `softwareEvidenceDiagnostics observation_window(...)` when diagnostics are available

Optional:

- `expected_behavior`: what should have happened instead
- `comparison_baseline`: another save, version, or patch state used for comparison

### 3. Observation

Observation fields describe the actual evidence collected.

Required:

- `symptom_classification`: the main observed symptom, using a stable label
- `diagnostic_counters`: the relevant counter groups captured during the observation window; include all groups needed for the hypothesis under test, such as `software(...)`, `electronics(...)`, and `softwareOffices(...)` when present
- `evidence_summary`: the short factual summary of what was observed
- `confidence`: low, medium, or high
- `confounders`: known uncertainties, competing explanations, or `none known`; use this for uncertainty that is not already represented directly by counters or metadata

Optional:

- `log_excerpt`: only short excerpts or references to attached logs, including relevant `softwareEvidenceDiagnostics detail(...)` lines when office-level state matters
- `artifacts`: links or filenames for logs, saves, screenshots, or videos; may include relevant `softwareEvidenceDiagnostics detail(...)` lines such as `detail_type=softwareOfficeStates`
- `notes`: anything useful that does not fit the structured fields

## Raw Versus Normalized Fields

The following fields are expected to come from raw diagnostics with little or no interpretation:

- `diagnostic_counters`
- `log_excerpt`
- `settings`
- parts of `patch_state` when local code or logging differs from the normal build, or the literal `unknown` when the runtime cannot name those deviations

When the active question is upstream input pressure versus downstream office-resource gating, prefer preserving the matching raw counter groups and any relevant `softwareEvidenceDiagnostics detail(...)` lines together rather than paraphrasing them into prose.

The following fields usually require explicit investigator input:

- `scenario_label`
- `scenario_type`
- `reproduction_conditions`
- `observation_window`
- `confounders`
- `patch_state` when the runtime emitted `unknown` but the maintainer knows the exact local deviations

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

These labels are working categories, not proof of root cause.
Keep them symptom-based rather than cause-based. Do not introduce presumed root-cause labels such as `electronics_shortage`.

## Related Documents

- [`.github/software-investigation-workflow.md`](./software-investigation-workflow.md): log capture, comparison checkpoints, decision rules, and stage workflow
- [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./ISSUE_TEMPLATE/software_evidence.yml): reusable evidence entry form
