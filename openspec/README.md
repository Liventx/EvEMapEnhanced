# OpenSpec for EvEMapEnhanced

This directory holds the project's [OpenSpec](https://github.com/Fission-AI/OpenSpec)-style
specifications: a plain-Markdown, spec-driven-development layer that describes what the
application actually does, kept next to the code it describes and versioned in the same repo.

The `openspec` CLI/agent tooling isn't installed in this repo -- this structure was authored by
hand to follow the same convention, so it stays compatible if the tooling is added later. See
`.cursor/rules/openspec-workflow.mdc` for the rule that keeps it up to date automatically.

## Layout

```
openspec/
├── specs/              # Source of truth: how the system behaves today, by domain
│   └── <domain>/spec.md
├── changes/            # In-flight change proposals (delta specs), archived once merged
│   └── archive/
└── config.yaml         # Project context for anyone (human or agent) authoring specs
```

## Domains

| Domain | Covers |
|---|---|
| `sde-data` | Downloading/importing CCP's Static Data Export into the local SQLite cache |
| `map-rendering` | Standard/Schematic map display modes, Dotlan-derived layout, plate styling |
| `routing` | Gate, jump-drive, jump-bridge and hybrid pathfinding over the universe graph |
| `jump-planning` | Capital jump range/fuel/fatigue mechanics and pilot skill profiles |
| `structures` | User-entered citadels, jump bridges, cyno beacons/jammers |
| `activity-stats` | Kill activity and NPC-kill data sourced from zKillboard/ESI |
| `user-data` | Persisted pilot profiles, saved routes, and system notes |

## Working with specs (manual workflow, no CLI)

**Before starting non-trivial work**, read the `spec.md` for every domain your change touches.

**For a small, already-agreed change**, edit the relevant `specs/<domain>/spec.md` directly as
part of the same commit that implements it -- add/update/remove the `### Requirement:` and
`#### Scenario:` blocks that changed.

**For a larger or more ambiguous change**, create `openspec/changes/<slug>/`:
- `proposal.md` -- why, what, scope
- `specs/<domain>/spec.md` -- delta only, using `## ADDED/MODIFIED/REMOVED Requirements`
- `tasks.md` -- implementation checklist

Once implemented, merge the delta into `openspec/specs/<domain>/spec.md` and move the folder to
`openspec/changes/archive/`.

See `openspec/config.yaml` for project context and authoring rules injected into every spec.
