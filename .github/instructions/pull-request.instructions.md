# Pull Request Instructions

When asked to generate a PR title/description, follow this structure.
Prefer filling the repository template at
`.github/pull_request_template.md`.

## PR Title
- Use Conventional Commit style:
  - `<type>(<scope>): <summary>`
- Summary 72 characters or fewer, imperative mood, no trailing period
- Choose `type`/`scope` using the same rules as commit messages

## PR Description Template
Use these sections in order:

## What changed
- Bullet list of main functional or behavioral changes (2-6 bullets)

## Why
- Motivation / problem statement
- Link issues if known (e.g., `Refs: #123`)

## How
- Key implementation notes, constraints, and tradeoffs
- Mention important defaults/ranges/thresholds that changed

## Testing
- What you tested (unit/integration/manual)
- How reviewers can verify (commands/steps if applicable)

## Risk / Rollback
- Potential risk areas
- Rollback plan or mitigation (if relevant)

## PR Classification (optional)
If asked to classify the PR, use one label:
- `Feature`, `Bugfix`, `Refactor`, `Docs`, `Chore/Maintenance`,
  `Build/CI`, `Test`

Provide a one to two sentence justification.
