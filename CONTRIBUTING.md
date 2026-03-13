# Contributing

Use this document for contributor-facing workflow and expectations.
Maintainers operating releases or investigation workflows should start with
[MAINTAINING.md](./MAINTAINING.md).

## Scope

This repository is a Cities: Skylines II mod for investigating and mitigating
no-office-demand failures plus adjacent `software`-track hypotheses that were
originally suspected to explain them.

Keep changes narrow, evidence-driven, and explicit about what is confirmed
versus still under investigation.

## Branches

- `master`:
  default branch and source of truth for released code, released documentation,
  and repository state tied to actual releases
- `develop`:
  integration branch for ongoing work that is intended to converge toward the
  next release
- `track/phantom-vacancy`:
  focused long-running branch for confirmed or suspected stale-market-state
  work, especially around signature property occupancy and vacancy accounting
- `track/software-instability`:
  focused long-running branch for the office-resource / `software`
  investigation path, including diagnostics, evidence collection, and
  experimental mitigation work

## Branching model

Use short-lived topic branches for actual implementation work.
Branch from the long-running branch you intend to merge into.

Examples:

- branch from `track/phantom-vacancy` for vacancy-state or signature-related work
- branch from `track/software-instability` for office-resource, diagnostics,
  evidence workflow, or `software`-track work
- branch from `develop` only when the work is genuinely cross-track integration
- branch from `master` for small release-facing repo metadata or documentation
  fixes that belong directly on the released branch

## Merge strategy

- Topic branch -> `track/*`: use **Squash and merge** to keep track history clean.
- `track/*` -> `develop`: use **Merge pull request** (merge commit) to preserve track-level integration history.
- `develop` -> `master`: use **Merge pull request** (merge commit) to preserve release-level integration history.

## Keeping PRs reviewable

A working branch may contain multiple kinds of changes, but each PR should have
one primary purpose.

As a rule of thumb, try to keep PRs centered on one of these categories:

- **runtime**: C# gameplay logic, diagnostics, settings behavior, save/load or
  patch application behavior
- **evidence-schema**: issue forms, schemas, reporting docs, evidence workflow
  docs, maintainer instructions
- **repo-ops**: GitHub Actions, automation scripts, test code for repo tooling,
  promotion or triage workflows

It is acceptable for one PR to include small supporting edits outside its main
category, but the PR title and body must describe the dominant change honestly.

If a branch has grown too large or mixed, split it before opening PRs by using
short-lived PR branches and moving only the relevant commits into each one.

## Choosing the right branch line

Use the `track/phantom-vacancy` line when the change is about:

- market-listing correctness
- vacancy state cleanup
- stale or occupied properties being counted as available
- reproductions tied to signature buildings or phantom-vacancy behavior

Use the `track/software-instability` line when the change is about:

- office resource flow
- office-demand/global-sales undercount
- virtual import seller/path inconsistencies
- software producer/consumer distress
- diagnostics investigating the `software` track
- raw log intake, evidence promotion, or software-track reporting workflow

## Syncing long-running branches

Because `track/*`, `develop`, and `master` are long-running branches, they may
diverge.

General guidance:

- prefer **merge-based syncing** between long-running branches
- when syncing a `track/*` branch with `develop`, merge `develop` into the track branch with `git merge --ff develop`
- do not rewrite shared long-running branch history unless there is a strong,
  explicit reason
- before opening `develop -> master`, make sure you understand whether
  `master` contains release-only commits that should first be merged back into
  `develop`

Avoid opening a PR into `master` from a branch that unintentionally includes
unrelated commits.

## Commits and pull requests

- Use Conventional Commits: `type(scope): subject`
- Keep the subject imperative, concise, and without a trailing period
- Use the PR template at `.github/pull_request_template.md`
- Explain changed defaults, reload or restart requirements, and save impact
- Link the relevant issue or investigation when one exists
- Make sure the PR title describes the main effect of the final diff, not just
  the noisiest file or the first change you made

Repository-specific instructions:

- [`.github/instructions/commit-message.instructions.md`](./.github/instructions/commit-message.instructions.md)
- [`.github/instructions/pull-request.instructions.md`](./.github/instructions/pull-request.instructions.md)

## Testing expectations

Before opening a PR, include the strongest verification you can realistically
provide:

- build validation if your local CSL2 toolchain is available
- manual reproduction or regression steps for runtime behavior changes
- logs, screenshots, or save context when behavior depends on in-game state
- script or workflow test results when the PR changes repo automation

If you could not test something, state that explicitly in the PR.

## Change boundaries

- Do not claim the `software` track is solved without strong evidence
- Do not describe `software` consumer distress as proof of lower office demand
  without direct demand evidence
- Do not broaden vacancy fixes beyond confirmed cases without documenting risk
- Call out settings changes and whether they require reload or restart
- Keep diagnostics noise and verbose logging changes intentional
- Do not let evidence automation become unstated product behavior; keep the
  distinction between deterministic capture and interpreted summary explicit

## Releases

If a change affects release packaging, versioning, or release notes, use the
`Release checklist` issue template and follow [MAINTAINING.md](./MAINTAINING.md)
for release operations.
