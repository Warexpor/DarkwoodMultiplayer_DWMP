# Co-op story / quest sync (design)

## Goal

Two human players in one world, each with:

- Own inventory + hotbar
- Own cutscenes when they interact locally (cooking table, NPC dialogue, etc.)
- Shared world state (quests, flags, doors, time of day)

## Phases

### Phase A — Per-player interactions (Local mode, in progress)

- `Player.Instance` routes to the controlled character
- `InputScript` click handlers bind to the active `Player`
- Each clone keeps its own `Inventory` / `Hotbar`
- Mobs attack the nearest living player; either player can be “seen”

### Phase B — Shared flags (LAN + Local)

Host-authoritative sync over reliable channel:

- `Flags` / `Events` / `NightScenarios` deltas
- Door open states, generator on/off, quest stage ints
- Client applies packets; never writes host save directly

### Phase C — Per-player cinematics

When an interactable starts a cutscene:

1. Only the interacting `playerId` (0 = main, 1 = second) enters `performingAction` / UI lock
2. Other player stays movable unless area-lock script requires freeze
3. RPC: `InteractableLock { id, ownerId, busy: true }`

Cooking table example: Player 2 interacts → Player 2 gets cooking UI loop; Player 1 can still walk in the hideout unless script sets room lock.

### Phase D — Quest journal

- Shared quest completion flags on host
- Optional per-player journal notes for tutorials
- UI shows “Partner completed X” for non-interacting player

## Save rules

| Who | Saves what |
|-----|------------|
| Host | Canonical world + both flag blobs |
| Client | Sidecar `profile_N_coop.json` with last known flag hash |
| Local split-screen | Single machine: one save profile, two `Player.SaveState` slots in sidecar |

Do not call `SaveManager` from the inactive player’s `Player` instance.
