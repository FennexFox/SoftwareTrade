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

When multiple telemetry captures feed the same performance question:

- keep each capture as its own `Performance telemetry report` intake issue
- summarize rolling interpretation, comparisons, and next steps in one generic `Investigation` umbrella issue for that performance line
- link the intake issues from the umbrella instead of treating a telemetry intake issue as the long-lived conclusion tracker

## Before You Capture Telemetry

- turn on `EnablePerformanceTelemetry`
- keep `PerformanceTelemetrySamplingIntervalSec` and `PerformanceTelemetryStallThresholdMs` the same across a comparison pair
- keep the enabled fix set the same when you want direct before/after deltas
- if a fix toggle itself is the variable under test, say that explicitly in `What changed`; direct deltas are still allowed when save/scenario, sampling interval, and stall threshold match and telemetry metadata shows exactly one known fix-toggle difference
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
If the optional comparison bundle is unreadable or malformed, the automation
still keeps the baseline summary and reports the comparison input as ignored.

## Comparison Rules

Direct before/after deltas are only treated as comparable when telemetry
confirms:

- same save name or same scenario id
- same enabled fix set, or exactly one known fix-toggle difference
- same sampling interval
- same stall threshold

Game-version and mod-version mismatches are called out explicitly as warnings.

If the enabled fix set differs only because one known fix toggle changed, the
automation still computes direct deltas and labels the comparison as a single
fix-toggle delta.

If multiple fix toggles differ, or the fix-toggle state cannot be verified from
telemetry metadata, the automation still summarizes each run but labels the
pair as `not directly comparable`.

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
