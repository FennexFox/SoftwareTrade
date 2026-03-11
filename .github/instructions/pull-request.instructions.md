# Pull Request Instructions

When asked to generate a PR title/description, follow this structure.
Prefer filling the repository template at
`.github/pull_request_template.md`.

## PR Title
- Use Conventional Commit style:
  - `<type>(<scope>): <summary>`
- Choose `type`/`scope` using the same rules as commit messages.
- Summary 72 characters or fewer, imperative mood, no trailing period.
- Title the primary behavior or workflow change, not the biggest file or
  supporting test/doc churn.
- If runtime logic changed and tests/docs/config changed alongside it,
  title the logic change.

## PR Description Template
Use these sections in template order. Keep bullets concise, concrete,
and non-redundant.

## What changed
- 2-6 bullets covering the main functional or behavioral changes
- Start with the primary user-facing or reviewer-relevant change

## Why
- State the problem being solved or the reason for the change
- Link issues if known (e.g., `Refs: #123`)

## How
- Capture key implementation choices, constraints, and tradeoffs
- Mention important defaults, thresholds, reload/restart requirements,
  and save/migration impact when relevant

## Testing
- State what was tested: unit, integration, manual, or none
- Include reviewer verification commands or steps when applicable

## Risk / Rollback
- Call out meaningful risk areas only
- Include rollback or mitigation when shipped behavior could regress

## Reviewer Checklist
- Keep the template checklist when filling the full repository template.
- Mark items only when the diff or provided context supports them.

## PR Classification (optional)
If asked to classify the PR, use one label:
- `Feature`, `Bugfix`, `Refactor`, `Docs`, `Chore/Maintenance`,
  `Build/CI`, `Test`

Provide a one to two sentence justification.
