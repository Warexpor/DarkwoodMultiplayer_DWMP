# Darkwood Multiplayer

BepInEx mod for LAN co-op story mode in Darkwood.

## Install

1. Install [BepInEx 5](https://docs.bepinex.dev/) for Darkwood (Mono build — game uses `Darkwood_Data/Managed`).
2. Build `DarkwoodMultiplayer.sln` in Rider or Visual Studio.
3. Copy `DarkwoodMultiplayer\bin\Debug\DarkwoodMultiplayer.dll` to:
   `C:\Program Files (x86)\Steam\steamapps\common\Darkwood\BepInEx\plugins\DarkwoodMultiplayer\`

Both players need the **same mod DLL version** (protocol version must match).

## Config: Local vs LAN

After first launch, edit:

`C:\Program Files (x86)\Steam\steamapps\common\Darkwood\BepInEx\config\com.darkwood.multiplayer.cfg`

| Setting | Values | Purpose |
|---------|--------|---------|
| **PlayMode** | `Local` or `LAN` | `Local` = two players on one PC. `LAN` = network co-op. |
| **SwitchControlKey** | default `Tab` | Swap control between main and second player (Local only) |
| **SpawnSecondPlayerKey** | default `F1` | Spawn local second player |

Restart the game after changing **PlayMode**.

### Local co-op (split control on one PC)

1. Set `PlayMode = Local`.
2. Load a save.
3. Second player auto-spawns (or press **F1**).
4. Press **Tab** to switch control.
5. The active character is a **full `Player`** (inventory, doors, combat, crafting, dialogue). The inactive one is frozen.

### LAN co-op

1. Set `PlayMode = LAN` and restart.
2. **Host** loads the save you want to play, then **Host LAN game**.
3. **Client** uses the **same save profile/chapter** as the host, then **Connect**.
4. Each machine controls its own `Player.Instance`; the other human appears as a **remote proxy** with synced position and animations (walk / run / idle, leg facing, flip).

Default port: **7777**. Allow UDP/TCP through the firewall.

## Animation sync (LAN)

Network snapshots include:

- Locomotion: idle / walk / run (from `Player.running`, not guessed speed only)
- Leg facing angle and reverse-walk flag
- Sprite flip

Remote proxies blend legs back to a standing pose when idle (same idea as the main character).

## World sync and saves (current + planned)

### What works now (v0.10)

On connect, the **host** sends a `WorldSession` packet:

- Save profile id (`profile_N`)
- Chapter id and in-game day
- Big location name (where the host is)

The **client** logs what profile/chapter to load. **Both players must use the same save profile and chapter before connecting** until full replication exists.

### Planned architecture (v0.11 → v1.0)

| Layer | Host-authoritative | Notes |
|-------|-------------------|--------|
| **Session** | Save slot + chapter + RNG seed | Client refuses play if mismatch (or auto-loads host slot) |
| **World** | Time of day, doors, triggers, story flags | `Flags`, `Events`, `NightScenarios` deltas over reliable channel |
| **Entities** | NPC HP, positions, spawners | Snapshot + event RPCs |
| **Inventory** | Crafting outcomes, pickups | Each peer has own inventory in save; host validates transactions; periodic `Inventory.SaveState` sync for shared crates only |
| **Client save** | Host checkpoint | Client writes a co-op sidecar file or defers saves until disconnect — **never** overwrite host `SaveManager` blob from client without host ack |

### Inventory on client today

- Your machine always saves **your** `Player.Instance` inventory when you save the game.
- The remote player has **no** `Player` component — they cannot pick up items on your screen.
- For story co-op, next step is **shared world flags + scene load sync**, then **host-validated item transfers** (give item, open chest once).

## Roadmap

| Phase | Scope |
|-------|--------|
| **0.10** (now) | LAN anim sync (run/walk/legs), local full Player co-op, world session handshake |
| **0.11** | Scene / location load sync when host travels |
| **0.12** | Host-authoritative doors, time of day, key flags |
| **1.0** | Inventory replication, combat sync, full story flags |

## Paths

- Game: `C:\Program Files (x86)\Steam\steamapps\common\Darkwood`
- Decompiled reference: `C:\Users\Androidus\Desktop\Darkwood DECOMPILED\Scripts\Assembly-CSharp`
- Mod project: `C:\DarkwoodMod\DarkwoodMultiplayer`
