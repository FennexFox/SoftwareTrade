# Commit Message Instructions

When asked to generate a commit message, output only the final commit
message.

## Format
- Use Conventional Commits:
  - `<type>(<scope>): <subject>`
  - Body and footer are optional, but see "Body Rules" below.
  - If a body is present, use:
    - blank line
    - body
    - blank line
    - footer

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
- If uncertain between `feat` and `fix`, choose `fix`.
- For patch/signature alignment, default to `fix`.
- For adding refresh/rebuild systems that make existing settings/patches
  apply correctly, default to `fix`.

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
- Describe the primary intent, not the file list

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
- `fix(config): set ped walk factor default to 1.0`
- `docs(readme): clarify installation steps`
- `refactor(core): simplify route weight calculation`
- `fix(systems): refresh bus lane penalties on setting change`

Example with body:

```text
fix(systems): refresh bus lane penalties on setting change

- add BusLanePenaltyRefreshSystem and register it in Mod
- mark car lanes with PathfindUpdated after settings apply
- run an initial refresh so current saves pick up slider values
```
