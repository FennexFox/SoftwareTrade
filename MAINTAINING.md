# Maintaining

## Purpose

This document is the entry point for maintainer and repo-operator workflows.

Use it when you are operating releases, running `software`-track investigations,
or updating repository metadata and process documents. It is not a replacement
for [CONTRIBUTING.md](./CONTRIBUTING.md); that document remains the main guide
for contributor-facing branch, commit, PR, and testing expectations.

## Release Operations

- Local release flow is authoritative.
- Use [scripts/release.ps1](./scripts/release.ps1) for maintainer releases.
- GitHub release automation is defined in [`.github/workflows/release.yml`](./.github/workflows/release.yml).

## Software Investigation

For `software`-track investigation work, use these documents together:

- evidence schema: [`.github/software-evidence-schema.md`](./.github/software-evidence-schema.md)
- investigation workflow: [`.github/software-investigation-workflow.md`](./.github/software-investigation-workflow.md)
- evidence entry form: [`.github/ISSUE_TEMPLATE/software_evidence.yml`](./.github/ISSUE_TEMPLATE/software_evidence.yml)

Runtime investigation logs use the `softwareEvidenceDiagnostics` vocabulary.

## Contributor Process

For branching strategy, commit style, pull request expectations, and testing
expectations, use [CONTRIBUTING.md](./CONTRIBUTING.md).

## Repo Metadata

Operational repository metadata lives under [`.github/`](./.github/), including:

- issue templates
- pull request templates
- instruction files
- workflow definitions
