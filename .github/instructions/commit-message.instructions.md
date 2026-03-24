# Commit Message Instructions

This file is the canonical source for generated commit messages in this
repository. Read it before drafting commit text.

When asked to generate a commit message, output only the final commit
message.

## Format
- For ordinary commits and squash-merge commit text, use Conventional
  Commits:
  - `<type>(<scope>): <subject>`
  - Body and footer are optional.
  - If a body is present, separate subject, body, and footer with blank
    lines.

## PR Merge Commits
- For PR merge commits into long-running branches, use the PR title
  exactly as the merge commit subject.
- Leave the body empty unless the merge itself adds release or
  integration context not already captured in the PR description.
- Do not use `Merge pull request #123 from ...` as the subject.

## Types
Use one of:
- `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`,
  `ci`, `chore`, `revert`

## Type Selection (strict)
- Choose `feat` only for a clearly new user-facing capability.
- Choose `fix` for behavior correction, compatibility alignment, runtime
  stability, or keeping existing features working.
- Choose `refactor` only when behavior is unchanged and the change is
  primarily structural.
- Choose `test` only when the meaningful change is limited to tests or
  test infrastructure.
- If runtime logic changed and tests/docs/config changed alongside it, do
  not choose `test` or `docs`; title the logic change and mention the
  supporting work in the body instead.
- If uncertain between `feat` and `fix`, choose `fix`.
- Repo-specific default: changes that make existing
  settings/patches/signatures apply correctly are usually `fix`, not
  `feat`.

## Scopes
- `scope` is required.
- Prefer existing areas/modules. If unsure, choose one of:
  - `core`, `ui`, `api`, `infra`, `build`, `docs`, `test`, `config`,
    `deps`, `systems`, `patches`
- Keep scope lowercase and short (1-2 words).

## Subject Rules
- Imperative mood: "add", "fix", "remove", "align", "clarify",
  "prevent", "rename"
- 50 characters or fewer
- No trailing period
- Describe the primary behavior or workflow intent, not the file list.
- Do not let tests, deleted files, or doc cleanup override the subject
  when they only support a logic change.
- Avoid generic subjects like "update files", "remove related scripts",
  or "add tests for behavior" when a more specific runtime intent is
  visible in the diff.
- Prefer the narrowest real module or behavior scope.

## Body Rules (only when needed)
Add a body when:
- more than one file changed, or
- a new file/system/component was added, or
- behavior changed in a way reviewers should verify, or
- migration/testing steps matter

Body should:
- explain what changed and why
- wrap lines at about 72 chars
- use 2-4 bullets
- each bullet starts with `- ` and an imperative verb

## Breaking Changes
- If breaking, add `!` after type or scope, e.g. `feat(api)!: ...`
- Add footer:
  - `BREAKING CHANGE: <what breaks and what to do>`

## References
- If an issue/PR number is known from context, add:
  - `Refs: #123`

## Examples
- `fix(systems): refresh bus lane penalties on setting change`
- `fix(systems): require body marker for promoted issue match`
- `fix(infra): remove label-based raw-log promotion path`
- `docs(instructions): clarify commit message guidelines`

Example with body:

```text
fix(systems): refresh bus lane penalties on setting change

- add BusLanePenaltyRefreshSystem and register it in Mod
- mark car lanes with PathfindUpdated after settings apply
- run an initial refresh so current saves pick up slider values
```
