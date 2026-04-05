# GitHub Copilot Instructions (Repository-wide)

These default instructions apply to general Copilot outputs in this
repository. Task-specific templates are split by topic under
`.github/instructions/`.

## Git Text Workflow
- Treat the files under `.github/instructions/` as the canonical source
  for generated commit messages and PR text.
- Before generating a commit message, open and follow
  `.github/instructions/commit-message.instructions.md`.
- Before generating a PR title or body, open and follow
  `.github/instructions/pull-request.instructions.md`, then fill
  `.github/pull_request_template.md`.
- Do not draft commit messages or PR text from memory, the active file,
  or the latest commit when one of those instruction files applies.
- If the relevant instruction file is missing or conflicts with the
  request, say so explicitly instead of improvising.

## General
- Always write in English.
- Be concise, factual, and reviewer-friendly.
- Do not invent details not present in the diff or context.
- Prefer actionable language. Avoid vague phrases like "various changes"
  unless unavoidable.
- Treat `.codex/vendor/**` as external vendored code managed in a
  separate repository.
- Do not propose edits or review comments for `.codex/vendor/**` unless
  the request explicitly targets vendor sync, upstream fixes, or a
  vendoring audit.

## Language & Style Guardrails
- Prefer short bullets over long paragraphs.
- Use consistent terminology with the codebase.
- If unsure about a detail, state uncertainty explicitly instead of
  guessing.

## Topic-specific Instruction Files
- Commit messages: `.github/instructions/commit-message.instructions.md`
- PR title and description:
  `.github/instructions/pull-request.instructions.md`
