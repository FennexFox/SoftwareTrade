# Contributing

## Scope

This repository is a Cities: Skylines II mod for investigating and mitigating
no-office-demand failures. Keep changes narrow, evidence-driven, and explicit
about what is confirmed versus still under investigation.

## Branches

- `master`: default branch and source of truth for repository metadata and
  released documentation
- `develop`: integration branch for ongoing work
- `track/phantom-vacancy`: focused branch line for confirmed or suspected
  stale-market-state work, especially around signature property occupancy and
  vacancy accounting
- `track/software-instability`: focused branch line for the office-resource /
  `software` investigation path, including diagnostics and experimental
  mitigation work

For repo metadata changes such as issue templates, PR templates, workflows, and
instructions, prefer a short-lived branch from `master` and open a pull request
back to `master`.

For feature or fix work, branch from the target integration branch you actually
intend to merge into. Avoid opening a PR from a branch based on `develop` into
`master` unless you have verified there are no unrelated commits in the diff.

Use the `track/phantom-vacancy` line when the change is about market-listing
correctness, vacancy state cleanup, or reproductions tied to occupied
properties being counted as available.

Use the `track/software-instability` line when the change is about office
resource flow, outside connection or cargo storage patching, office efficiency
collapse, or diagnostics intended to validate the `software` hypothesis.

## Commits and pull requests

- Use Conventional Commits: `<type>(<scope>): <subject>`
- Keep the subject imperative, concise, and without a trailing period
- Use the PR template at `.github/pull_request_template.md`
- Explain changed defaults, reload or restart requirements, and save impact
- Link the relevant issue or investigation when one exists

Repository-specific instructions:

- [`.github/instructions/commit-message.instructions.md`](./.github/instructions/commit-message.instructions.md)
- [`.github/instructions/pull-request.instructions.md`](./.github/instructions/pull-request.instructions.md)

## Testing expectations

Before opening a PR, include the strongest verification you can realistically
provide:

- build validation if your local CSL2 toolchain is available
- manual reproduction or regression steps for runtime behavior changes
- logs, screenshots, or save context when behavior depends on in-game state

If you could not test something, state that explicitly in the PR.

## Change boundaries

- Do not claim the `software` track is solved without strong evidence
- Do not broaden vacancy fixes beyond confirmed cases without documenting risk
- Call out settings changes and whether they require reload or restart
- Keep diagnostics noise and verbose logging changes intentional

## Releases

Maintainer releases are driven locally by
[`scripts/release.ps1`](./scripts/release.ps1). If a change affects release
packaging, versioning, or release notes, use the `Release checklist` issue
template before tagging a release.
