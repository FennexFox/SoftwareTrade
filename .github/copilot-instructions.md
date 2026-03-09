# GitHub Copilot Instructions (Repository-wide)

These default instructions apply to general Copilot outputs in this
repository. Task-specific templates are split by topic under
`.github/instructions/`.

## General
- Always write in English.
- Be concise, factual, and reviewer-friendly.
- Do not invent details not present in the diff or context.
- Prefer actionable language. Avoid vague phrases like "various changes"
  unless unavoidable.

## Language & Style Guardrails
- Prefer short bullets over long paragraphs.
- Use consistent terminology with the codebase.
- If unsure about a detail, state uncertainty explicitly instead of
  guessing.

## Topic-specific Instruction Files
- Commit messages: `.github/instructions/commit-message.instructions.md`
- PR title and description:
  `.github/instructions/pull-request.instructions.md`
- Maintainer direction-check discussions:
  `.github/instructions/maintainer-collaboration.instructions.md`
