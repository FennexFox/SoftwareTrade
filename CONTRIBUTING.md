# Contributing

Use this document for contributor-facing workflow and expectations.

If you are operating releases, evidence promotion, or other maintainer-only processes, start with [MAINTAINING.md](./MAINTAINING.md) instead.

Most work should start from a tracked issue so the board can carry the intent, scope, and status of the change.

## Project Scope

This repository is a Cities: Skylines II mod for:

- shipping confirmed fixes for reproduced no-office-demand-related failures
- investigating adjacent `software`-track hypotheses without overstating them

Keep changes narrow, evidence-driven, and explicit about what is confirmed versus still under investigation.

## Contributor Workflow

Most contributions should follow this order:

1. Decide what kind of change you are making.
   Typical categories are runtime behavior, evidence/reporting workflow, or repo automation.

2. Choose the right base branch.
   Branch from the long-running line that matches the work, not from whatever branch happens to be latest locally.

3. Create a short-lived topic branch.
   Keep one main purpose per PR.

4. Make the smallest coherent change that solves the problem.
   Avoid mixing runtime fixes, investigation wording, and repo automation unless one clearly supports the other.

5. Verify as strongly as you can.
   Build, test, reproduce, or document manual validation depending on the change type.

6. Open a PR with clear scope.
   Explain the behavior change, risk, settings impact, and any reload or restart expectations.

## Before You Open A PR

Make sure the PR answer is clear on these points:

- what changed
- why it changed
- what evidence or reproduction supports it
- whether settings behavior changed
- whether a reload or full restart matters
- what you verified, and what you could not verify

If the work touches an existing issue or investigation line, link it.

## Pick The Right Branch Line

Use the long-running branch that matches the work:

| Branch | Use it for |
| --- | --- |
| `master` | released code, released docs, and small release-facing repo fixes that belong directly on the released branch |
| `develop` | cross-track integration work intended to converge toward the next release |
| `track/phantom-vacancy` | vacancy-state cleanup, market-listing correctness, signature-building demand suppression, and related reproductions |
| `track/optimization` | performance telemetry, stall analysis, and optimization work tied to measured behavior |
| `track/software-instability` | office-resource flow, outside-connection or cargo storage patching, diagnostics, raw log intake, evidence promotion, and `software`-track investigation work |

Practical rule:

- branch from `track/phantom-vacancy` for phantom-vacancy work
- branch from `track/optimization` for performance, stall, and optimization work
- branch from `track/software-instability` for `software`-track and evidence workflow work
- branch from `develop` only for genuine integration work
- branch from `master` only for small release-facing fixes that really belong there

## Keep PRs Reviewable

Each PR should have one primary purpose.

As a rule of thumb, center the PR on one of these categories:

- `runtime`: C# gameplay logic, diagnostics, settings behavior, save/load behavior, or patch application behavior
- `evidence-schema`: issue forms, schemas, reporting docs, evidence workflow docs, or maintainer instructions
- `repo-ops`: GitHub Actions, automation scripts, and tests for repo tooling

Small supporting edits outside the main category are fine, but the PR title and body must describe the dominant change honestly.

If the branch has grown too mixed, split it before opening PRs.

## Branching And Merge Model

Use short-lived topic branches for implementation work.

Merge expectations:

- topic branch -> `track/*`: use squash merge
- `track/*` -> `develop`: use merge commit
- `develop` -> `master`: use merge commit
- for PR merge commits into long-running branches, use the PR title as
  the merge commit subject
- leave PR merge commit bodies empty unless the merge itself adds
  release or integration context not already captured in the PR
- do not use `Merge pull request #...` as the merge commit subject
- for pure branch-sync merges without a PR, the default merge message is
  fine

Because the long-running branches may diverge:

- prefer merge-based syncing between long-running branches
- when syncing a `track/*` branch with `develop`, merge `develop` into the track branch
- do not rewrite shared long-running branch history unless there is a strong explicit reason
- before opening `develop -> master`, check whether `master` has release-only commits that should first come back into `develop`

## Commit And PR Expectations

- use Conventional Commits for ordinary commits, squash merges, and PR
  titles: `type(scope): subject`
- keep the subject imperative, concise, and without a trailing period
- use the PR template at [`.github/pull_request_template.md`](./.github/pull_request_template.md)
- explain changed defaults, reload or restart requirements, and save impact when relevant
- make sure the PR title describes the main effect of the final diff, not just the first change you made
- for PR merge commits into long-running branches, reuse the PR title as
  the merge commit subject and treat the PR body as the detailed summary

Repository-specific writing instructions:

- [`.github/instructions/commit-message.instructions.md`](./.github/instructions/commit-message.instructions.md)
- [`.github/instructions/pull-request.instructions.md`](./.github/instructions/pull-request.instructions.md)

## Testing Expectations

Before opening a PR, provide the strongest realistic verification you can:

- build validation if your local CSL2 toolchain is available
- manual reproduction or regression steps for runtime behavior changes
- logs, screenshots, or save context when behavior depends on in-game state
- script or workflow test results when the PR changes repo automation

If you could not test something important, say so explicitly in the PR.

## Change Boundaries

Do not over-claim the current evidence.

- do not claim the `software` track is solved without strong evidence
- do not describe `software` consumer distress as proof of lower office demand without direct demand evidence
- do not broaden vacancy fixes beyond confirmed cases without documenting risk
- call out settings changes and whether they require reload or restart
- keep diagnostics noise and verbose logging changes intentional
- keep the distinction between deterministic capture and interpreted summary explicit in evidence tooling

## Release-Facing Changes

If a change affects release packaging, versioning, or release notes, use the `Release checklist` issue template and follow [MAINTAINING.md](./MAINTAINING.md) for the release workflow.
