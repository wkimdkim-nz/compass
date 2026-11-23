# AGENTS.md

## Agent skills

### Issue tracker

Issues and PRDs are tracked as GitHub issues via the `gh` CLI. External PRs are not a triage surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Default label vocabulary — each triage role's label equals its name (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: the glossary is `docs/glossary.md`, decisions are `docs/adr/`. See `docs/agents/domain.md`.

### Branch names

`<type>/<slug>`, with `<type>` one of `feat`, `fix`, `docs`, `chore`, `refactor`, `ci`, `research`, `prototype`, and a lowercase, hyphenated slug. Work on an issue is `<type>/issue-<N>-<slug>` (e.g. `feat/issue-101-toast`). The pre-commit hook refuses any other name except `claude/*`.

### Coding standards

The judgement rules a reviewer checks a diff against, and that code written here follows, are in `CODING_STANDARDS.md`.
