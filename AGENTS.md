# Repository AGENTS

## Review Scope

- Treat `.codex/vendor/**` as externally managed vendored code.
- The authoritative upstream for the current vendor subtree is the
  `packetflow_foundry` remote, not this repository.
- Ignore `.codex/vendor/**` during routine code review, planning, and
  implementation unless the task explicitly asks for a vendor sync,
  upstream fix, or vendoring audit.
- Keep consumer-repository overrides in `.codex/project/`,
  `.codex/agents/`, or `.agents/skills/`, not under `.codex/vendor/`.

## Subagent Policy

Default to using subagents when doing so is likely to improve efficiency
through parallelism, tighter context windows, or clear task ownership.

Prefer subagents for:
- independent investigation or codebase exploration
- log triage, diff review, and documentation verification
- disjoint implementation tasks with clear file or module ownership
- sidecar work that can run in parallel without blocking the immediate
  next local step

Avoid subagents for:
- trivial or very small tasks
- urgent critical-path work needed immediately for the next step
- tasks with heavy context-sharing overhead
- overlapping write scopes that are likely to cause merge or
  coordination churn

Treat this as the default operating policy unless higher-priority
session instructions or more specific skill instructions say otherwise.
