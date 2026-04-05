# .codex AGENTS

<!-- packetflow_foundry consumer bootstrap:start -->
## PacketFlow Foundry
- Vendor: `.codex/vendor/packetflow_foundry`
- Project-local overlays: `.codex/project/profiles/`, `.agents/skills/`, `.codex/agents/`
- `.codex/agents/` is the project-scoped subagent discovery surface. Vendored foundry agent TOMLs are bridged there from `.codex/vendor/packetflow_foundry/.codex/agents/`.
- `.agents/skills/` is a thin discovery-wrapper surface. Reusable retained kernels stay under `.codex/vendor/packetflow_foundry/builders/packet-workflow/retained-skills/`.
- Do not edit `.codex/vendor/packetflow_foundry` for local needs.
- Treat `.codex/vendor/**` as externally managed vendored code and ignore it during routine consumer-repo review unless the task explicitly targets vendor sync or upstream work.
- `.codex/project/profiles/default/profile.json` is a project-local scaffold.
- Skill-specific packet-workflow overrides may live at `.codex/project/profiles/<skill-name>/profile.json`.
- Legacy `.codex/project/agents/` is migration-only and should move to `.codex/agents/`.
- Legacy `.codex/project/skills/` is migration-only and should move to `.agents/skills/`.
<!-- packetflow_foundry consumer bootstrap:end -->
