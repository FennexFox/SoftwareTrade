# Performance Telemetry Reporting

Use this guide when you want to submit coarse performance telemetry for
maintainer triage.

This intake is for performance comparison. It is not the same as the
`Raw log report` flow for `softwareEvidenceDiagnostics`.

## What Telemetry Is For

Performance telemetry is intended to answer two questions:

- what steady-state overhead the mod adds outside stalls
- how stall frequency, duration, and severity change during stressed periods

The automation summarizes those two views separately and checks whether a
baseline/comparison pair is directly comparable.

The automation does not diagnose root cause. If you need semantic cause
analysis, collect a matching diagnostics raw log as well.

## When To Use This Intake

Use `Performance telemetry report` when:

- you are comparing before/after optimization behavior
- you want a deterministic summary of steady-state and stall behavior
- you want a managed GitHub comment that checks comparison validity

Use `Raw log report` instead when:

- you need semantic investigation of buyer, seller, or office-state behavior
- the question depends on `softwareEvidenceDiagnostics detail(...)` lines
- you want evidence promotion into the `software` investigation flow

## Before You Capture Telemetry

- turn on `EnablePerformanceTelemetry`
- keep `PerformanceTelemetrySamplingIntervalSec` and `PerformanceTelemetryStallThresholdMs` the same across a comparison pair
- keep the enabled fix set the same unless the fix toggle itself is the variable under test
- prefer the same save lineage, same game version, and same mod version for direct comparison
- if you need semantic interpretation later, also capture a matching diagnostics raw log on the same save and settings

## What To Upload

Preferred:

- one `.zip` file per run containing:
  - `perf_summary.csv`
  - `perf_stalls.csv`

Also accepted:

- the two CSV files attached directly
- both CSV texts pasted inline in separate fenced code blocks

The baseline bundle is required. The comparison bundle is optional.

## Comparison Rules

Direct before/after deltas are only treated as comparable when telemetry
confirms:

- same save name or same scenario id
- same enabled fix set
- same sampling interval
- same stall threshold

Game-version and mod-version mismatches are called out explicitly as warnings.

If those invariants do not hold, the automation still summarizes each run, but
it labels the pair as `not directly comparable` and skips direct delta claims.

## What The Automation Does

The managed comment computes:

- steady-state rollups from summary windows where `is_stall_window=false`
- stall rollups from `perf_stalls.csv`
- direct comparison deltas when invariants hold
- observational anomaly flags such as queue pressure or elevated stall
  frequency

The automation is deterministic only. It does not use GitHub Models and does
not assign causal explanations such as "the mod caused the stall".

## What The Automation Does Not Do

- no root-cause diagnosis
- no evidence promotion flow
- no per-frame trace reconstruction
- no semantic interpretation of company or office state

If performance telemetry shows an interesting regression, use it to decide what
to measure next, then collect the matching diagnostics logs if the next step
requires semantic cause analysis.
