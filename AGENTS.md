# Darkwood Multiplayer — Agent Session Log

## Session 2026-05-27 — "wrong damage" audit

### Problem
Damage numbers were wrong (too high) on the client when shooting entities.

### Root cause analysis — Damage message flow audit

**Traced the full damage path for hitscan weapons:**

1. Player shoots on **Client**
2. Physics.Raycast hits entity → runs vanilla Weapon.Fire() (draws FX, despawns bullet)
3. Vanilla `Weapon.Fire` calls `entity.getHit(damage, ...)` in the raycast loop
4. `ClientDamageRedirectPatch` (Prefix on `Character.getHit`) detects it's a local player attack, sends `PlayerAttackMessage` to host, returns `false` (blocks the original)
5. Host receives `PlayerAttackMessage`, resolves the target via CharacterTracker, calls `Character.getHit` on the host entity

### Bug 1 — Triple damage from hitscan weapons (HitscanFriendlyFirePatch + HitscanImpactSyncPatch)

`HitscanFriendlyFirePatch` (Prefix on `Physics.Raycast`) ran **before** the vanilla Weapon.Fire, did its **own** raycast, and if it hit an entity, sent a `FriendlyFireMessage` directly. Then the vanilla raycast also ran, hit the same entity, triggered `ClientDamageRedirectPatch`, which sent a `PlayerAttackMessage`.

**Result**: Two messages sent per shot. If the shield-bash hit its own target, three messages: FriendlyFire + PlayerAttack + FriendlyFire.

**Fix in `HitscanFriendlyFirePatch.cs`:**
- Removed the Prefix entirely.
- The file now contains only a no-op Prefix (`return true`) that keeps the patch registration alive for compatibility but does nothing.

**Verification** — `HitscanImpactSyncPatch.cs` (Prefix on `Core.AddPooledPrefab`) still handles FX forwarding correctly — it runs on both the FriendlyFire raycast hits (enemies) AND the vanilla raycast.

### Bug 2 — Double damage from melee swings (ClientFriendlyFirePatch + ClientMeleeSensorPatch)

`ClientMeleeSensorPatch` (Prefix on `MeleeSensor.OnTriggerEnter`) sent a `FriendlyFireMessage` when the swing collider touched an entity. Separately, vanilla's `MeleeSensor.OnTriggerEnter` called `entity.getHit()`, which triggered `ClientDamageRedirectPatch`, which sent a `PlayerAttackMessage`.

**Result**: Two messages per melee swing (FriendlyFire + PlayerAttack).

**Fix in ClientCombatPatches.cs:**
- Changed `ClientMeleeSensorPatch` Prefix to call `__instance.attackerTransform.GetComponent<Player>()` instead of using `Player.Instance` directly.
- Added a `__instance.attackerTransform == null` guard.
- **Removed `return false`** — now returns `true` to let vanilla `OnTriggerEnter` run, so only `ClientDamageRedirectPatch` sends the single `PlayerAttackMessage`.

Both fixes verified with the shield-swing test case (a melee hit through the shield-bash path that was previously sending 3 messages).

### v7 — Damage fixes (this session)

- **`HitscanFriendlyFirePatch.cs`** — No-op'd the Prefix to eliminate triple-hitscan-damage from double-raycast + friendly-fire overlap.
- **`ClientCombatPatches.cs`** — `ClientMeleeSensorPatch` now returns `true` (lets vanilla run) and uses `attackerTransform` for player detection, eliminating double-melee-damage from FriendlyFire + PlayerAttack overlap.

### v8 — Projectile weapon damage redirect & fallback fix

**Bug 1: Projectile weapons not redirected to host**

Root cause: `Bullet.onCollide` calls `charBase.getHit(damage, null, ...)` for player-fired
projectile weapons because vanilla `Player.spawnBullet` never sets `objectThatSpawnedMe`
on the bullet prefab (unlike enemy code which does). `ClientDamageRedirectPatch` checks
`attackerTransform == Player.Instance.transform` which fails because `attackerTransform`
is null → damage is applied locally on the client only, never sent to host.

Fixes:
- Added `TraverseHack.IsInsidePlayerBulletCollision` flag + `ClientProjectileDamagePatch`:
  Prefix on `Bullet.onCollide` sets the flag when `objectThatSpawnedMe == null` (player
  bullet), Postfix clears it.
- `ClientDamageRedirectPatch` now checks `isProjectileDamage` via the flag. When true:
  sends `PlayerAttackMessage` to host AND always returns `false` (blocks local getHit).

**Bug 2: Host fallback name matching used attacker position**

Root cause: When `PlayerAttackMessage.TargetNameHash` is 0 (entity not host-synced),
`HandlePlayerAttack` used `attackPos` (attacker position) for `FindClosestByName`.
With multiple same-named entities (e.g. 3 dogs), the wrong entity took damage.

Fix: `HandlePlayerAttack` now uses `msg.TargetPosX/Y/Z` (target position from sender)
instead of `attackPos` for the name-based fallback search.

## Root cause: dogs not dying on client

Two separate bugs:

### Bug 1 — Dropped death snapshot

Entity state was sent from host to client via `DeliveryMethod.Unreliable` (UDP). If the snapshot containing `Alive=false` was dropped, the client never learned the entity died.

**Fixes:**
- `EntityStateBroadcastService.cs:115` — Changed send delivery from `Unreliable` to `ReliableOrdered`.

### Bug 2 — Death animation not playing on client

`c.die()` was called correctly when a snapshot with `Alive=false` arrived, but the death animation never played. The vanilla death animation is driven by AI components (`Enemy_Basic`, `Enemy_Dog`) in their `Update()` — they detect `alive==false` and call `tk2dSpriteAnimator.Play(deathClip)`. Those AI components are **disabled on the client** (`ClientAIDisablePatches`), so `c.die()` set the flags (`alive=false`, `dying=true`) but no one started the animation.

**Fixes in `ClientEntityInterpolationService.cs`:**
1. Restructured `UpdateInterpolation` to drive animation from the snapshot's `Clip` field for **all** entities (alive and dead), not just alive ones. The death clip sent by the host is now played on the client via the snapshot.
2. Added `c.die()` calls in all three pending-match code paths when `HostAlive==false && client.alive==true`, so entities matched after the initial snapshot also die properly.
3. Added logging when death is detected on the client.

## Previous changes (historical record)

### v1 — Vanilla game patches

- **`HostAIPatches`** — Disabled AI pathfinding on host so player can control the proxy character. Traverses the Enemy_AIPlayer component and sets `aggression = 0`, `active = false`, `moving = false`, etc. Also blocks `NPC.enabled` for NPC-type entities (quest givers).
- **`HostCombatPatches`** — Prefix on `Character.getHit` blocks damage processing when triggered by a remote player's attack (identified via `CoopPlayerRegistry.IsProxy`); returns `false` to skip original. Postfix on `Player.startSecondAttack` prevents double-hit sound when remote player attacks.
- **`ClientAIDisablePatches`** — On client, disables AI + `Enemy_Basic` on all entities except the local player; sets components to inactive so the host is authoritative.
- **`ClientCombatPatches`** — Prefix on `Character.getHit` returns `false` for ALL entities on client (no local damage processing; damage is handled server-authoritative). Postfix blocks `Player.startSecondAttack` on remote players.
- **`CoopGameplayPatches`** — Tags `NetworkRole` on awake; suppresses `ChasePlayer` on proxies; suppresses `ForceIdle` on proxy; blocks `MarkedAsKilled` on proxy.
- **`HostDetectionGapPatches`** — On host, blocks `Player.enemyInSightOfPlayer` and `AI.noticePlayer` for proxy entities so the proxy isn't detected by enemies.

### v2 — Network sync foundations

- **`LanNetworkManager`** — UDP transport via `LiteNetLib`. Host binds on a port, client connects. Fixed `msgType` encoding to use `NetDataWriter.Put(ushort)` / `NetDataReader.GetUShort()`.
- **`EntityStateBroadcastService`** — Every 100ms, snapshots all `CharacterTracker` entries (position, rotation, animation clip, alive flag, health) and sends as `EntityStateMessage`.
- **`ClientEntityInterpolationService`** — Receives snapshots and interpolates client entities toward host positions. Handles entity matching via position+name, idle fallback, and the `state.alive` flag.
- **`CharacterTracker`** — Assigns stable host-side IDs to characters on spawn/unfreeze so the host can reference them across snapshots. Removes on destroy.
- **`WorldPhysicsSyncService`** — Syncs `Rigidbody` states (position, rotation, velocity, angular velocity) of pushable objects (barrels, doors) at 10 Hz.
- **`PlayerPositionManager`** — Broadcasts local player transform each frame; receives remote player positions for interpolation.

### v3 — Combat & damage sync

- **`ClientHitscanDamageRedirectPatch`** — On client, when the player fires a weapon and the hitscan hits an entity, the original `getHit` is blocked; instead a `PlayerAttackMessage` is sent to the host with (target hostId, damage, position, direction).
- **`WeaponFireSyncPatch`** — Prefix on `Weapon.Fire` traps the "last hit entity" before the original runs; passes its host ID into the attack message so the host can resolve `CharacterTracker.FindByStableId`.
- **`HostDeathSendPatch`** — When an entity dies on host (`Character.die`), broadcasts a reliable `EntityDeathMessage` for the client to acknowledge. (This may have been superseded by the `Alive` flag in snapshots.)
- **`ClientDeathPatches`** — On client death, notifies host; on respawn, re-enables rendering and removes the spectator overlay. Blocks `Player.realDeath` on client when host is alive.

### v4 — Environment sync

- Various patches for doors, containers, barricades, shadows, fires, explosions, gas pumps, compressors, traps, bear traps, etc. All follow the pattern: on client, block the original interaction and send an action message to the host; on host, apply the action and broadcast state change.

### v5 — Animation & sound

- **`PlayerAnimationSnapshot`** — Captures and broadcasts player animation state (clip, frame, position, rotation) at 20 Hz for remote player rendering.
- **`AudioSuppressionPatch`** — Disables `AudioListener` on proxy and remote player characters to prevent double-audio.
- **`EntitySoundSyncPatches`** — Relays `SoundArea.Play` calls from host to client for positional entity sounds.

### v6 — AI proxy & world

- **`PlayerProxyBuilder`** — Builds a "proxy" character for the remote player on host by cloning the local player's body and attaching `CoopPlayerMarker` + `RemotePlayerProxy` scripts.
- **`NightSpawnRedirectPatches`** — Redirects night event spawns to both players' locations.
- **`CoopPlayerRegistry`** — Tracks all player-related entities (local, remote, proxy) and provides lookup methods.

### v9 — Proxy smell detection & client-only entity cleanup

**Bug 1: Proxy invisible to AI from behind (smell detection)**

Root cause: The proxy gets a standalone `CharBase` (not `Character`). Vanilla `checkForCharactersInViewRange` filters by `Character` component, so the proxy is invisible to standard AI detection. `HostCanSeeEnemyPatch` CASE 2 requires FOV check (line-of-sight), so approaching from behind fails. Sniffer uses `Player.Instance` (host player, potentially far away).

Fix: `HostAIPatches.cs` `HostCanSeeEnemyPatch` CASE 2 now:
- Extends effective detection range to include `Sniffer.radius` (smell range)
- Bypasses FOV restriction when the proxy is within Sniffer smell radius
- Skips the raycast for smell detection (smell doesn't need line-of-sight)

**Bug 2: Client-only entities from divergent saves (ghost rabbit)**

Root cause: Host and client each load their own save file via `SaveManager.loadObj` → `Core.AddPrefab`. If the installations have different saves (e.g. separate Darkwood copies), the client has entities the host doesn't know about. When the client damages such an entity, `PlayerAttackMessage` reaches the host but the host can't find the target → damage never applied.

Fix: `ClientEntityInterpolationService` now:
- Tracks `_everHostSyncedIds` — all entity IDs ever assigned by the host
- After receiving first snapshot + 5s grace period (`UnmatchedCleanupDelay`), destroys any character in the tracker that has NEVER been host-synced (neither `_hostSyncedIds` nor `_everHostSyncedIds`)
- Excludes the local player (`Player.Instance`) and remote player proxies
- Previously-synced but stale entities are preserved (have `_everHostSyncedIds` entry)

### v10 — AI audit: zero client-side AI enforcement

**Audit findings — unpatched AI components with Update/FixedUpdate:**

| Component | What its Update() does | Risk |
|-----------|----------------------|------|
| `AILerp` | Pathfinding movement via A* — moves entity with `canMove` flag | **Critical** — moves entity client-side, overriding host position |
| `RVOAI` | RVO-based pathfinding — moves entity with `canMove` flag | **Critical** — same as AILerp |
| `Flier` | During dive: damages `Player.Instance.getHit(5f)` directly | **Critical** — deals player damage on client without host authority |
| `Shooter` | Fires projectiles at target (player), calls `Player.Instance.getHit()` | **Critical** — shoots and damages player on client |
| `InSightOfPlayer` | Detects if player sees entity, fires EventTriggers | **Critical** — fires trigger scripts on client without host authority |
| `RandomMovement` | Randomly repositions entity every `refreshRate` seconds | **High** — overrides host-driven position with random client movement |
| `ShadowCreature` | Updates `distanceToPlayer` for proximity attack | **Medium** — cosmetic (distance tracking). But `animationTriggerListener` can destroy GameObject. |
| `FollowTarget` | Follows a target transform | **High** — moves entity client-side |

**Fix approach**: Add Harmony Prefixes to each component's `Update()` in `ClientAIDisablePatches.cs`. Each Prefix checks `ShouldSkipAI` (client + not local player) and returns `false` to block the method.

**Already patched**: `Character.Update`, `Character.FixedUpdate`, `Character.LateUpdate`, `Sniffer.Update`, `AIPath.Update` (from earlier work).

**Patches applied:**
- `AILerp.Update` — blocked (pathfinding movement)
- `Flier.Update` — blocked (dive attack, dealt `Player.Instance.getHit(5f)` directly)
- `Shooter.Update` — blocked (projectile fire, dealt `Player.Instance.getHit(damage)` directly)
- `InSightOfPlayer.Update` — blocked (fires EventTriggers on sight detection)
- `RandomMovement.Update` — blocked (random repositioning overrides host position)

**Patches applied:**
- `AILerp.Update` — blocked (pathfinding movement)
- `Flier.Update` — blocked (dive attack, dealt `Player.Instance.getHit(5f)` directly)
- `Shooter.Update` — blocked (projectile fire, dealt `Player.Instance.getHit(damage)` directly)
- `InSightOfPlayer.Update` — blocked (fires EventTriggers on sight detection)
- `RandomMovement.Update` — blocked (random repositioning overrides host position)
- `RVOController.Update` — blocked (RVO movement, sets `tr.position` directly)
- `RichAI.Update` — blocked (A* navigation movement, sets `tr.position` directly)

**Skipped (empty or cosmetic-only Update):**
- `RVOAI` — empty `Update()`, no-op
- `FollowTarget` — empty `Update()`, no-op

**No Character found = safe default**: The aggressive `ShouldSkipAI(Component)` overload blocks ANY component on a non-player client GameObject entirely by checking `comp.gameObject == Player.Instance.gameObject`. No need for `GetComponent<Character>()` at all.

**Build & deploy**: `DarkwoodMultiplayer.dll` built and deployed to both host + client directories.

### v11 — Shared shadow sync

**Problem**: Shadows were client-local. Each machine spawned its own `ShadowCreature` entities independently via `CharacterSpawner.waitToSpawnShadow()` and `Player.tryToSpawnShadow()`, so the host never saw shadows attacking the client and vice versa.

**Existing infrastructure** (already present but incomplete):
- `ShadowSpawnMessage` + `ShadowEventMessage` types
- `ShadowCaptureOnSpawnPatch` (Postfix on `Core.AddPrefab`) — captured only `"characters/fakechars/shadow"`, not `"shadow_immortal"`
- `HostShadowSyncPatch` (Postfix on `Player.tryToSpawnShadow`) — sent `ShadowEventMessage`
- `ClientDisableShadowSpawnPatch` — blocked `CharacterSpawner.waitToSpawnShadow` on client
- `HandleShadowEvent` — called `Player.tryToSpawnShadow()` locally (spawning shadows at wrong positions)
- `HandleShadowSpawn` — spawned shadow at host position but no continuous sync

**Fixes:**

**`NetMessages.cs`:**
- Extended `ShadowSpawnMessage` with: `ShadowId` (short), `ShadowType` (byte, 0=regular/1=immortal), `DistanceToPlayer` (float), `Flags` (byte, bit 0=alive, bit 1=dead)
- Added `ShadowStateUpdateMessage` (same fields minus ShadowType) for periodic host→client state sync
- Added `NetMessageType.ShadowStateUpdate = 74`

**`LanNetworkManager.cs`:**
- Added shadow tracker: `_shadowTracked` dictionary, `_nextShadowId` counter, `BroadcastShadowStates()` at 0.3s interval
- Added `GetNextShadowId()`, `RegisterShadow()`, `UnregisterShadow()` methods
- Added `HandleShadowStateUpdate()` — receives periodic state, updates shadow position + distanceToPlayer + plays Death1 on death
- Fixed `HandleShadowEvent()` — now only sets `CharacterSpawner` flags (`shadowsRemove=false`, `spawnedShadows=true`, etc.) without calling `tryToSpawnShadow()`
- Updated `HandleShadowSpawn()` — uses ShadowType to pick the right prefab, attaches `ShadowSyncInfo` component with ID, applies initial state, plays Float animation, stores in `_clientShadowLookups`
- Added `BroadcastShadowStates()` — iterates all tracked shadows, sends `ShadowStateUpdateMessage` with current position/distance/flags, cleans dead entries

**`ShadowSyncPatches.cs`:**
- `ShadowCaptureOnSpawnPatch` now captures BOTH `"characters/fakechars/shadow"` AND `"characters/fakechars/shadow_immortal"`
- Uses `ShadowSyncInfo` component (new `MonoBehaviour` with `ShadowId` + `ShadowType`) attached to each shadow GameObject
- Assigns unique ID via `net.GetNextShadowId()` and registers in host tracker
- `HostShadowDiePatch` (Prefix on `ShadowCreature.die`) — unregisters from tracker when shadow dies

**`ClientAIDisablePatches.cs`:**
- Added 5 new prefixes blocking `ShadowCreature` lifecycle on client: `Start`, `OnEnable`, `appear`, `die`, `Update`
- All use the aggressive `ShouldSkipAI(Component)` overload

**How it works:**
1. Host spawns shadow (via `Player.tryToSpawnShadow` or `CharacterSpawner.waitToSpawnShadow`)
2. `ShadowCaptureOnSpawnPatch` intercepts the `Core.AddPrefab` call, assigns ID, sends `ShadowSpawnMessage`
3. Host tracks the shadow in `_shadowTracked`, broadcasts its state every 0.3s
4. Client receives `ShadowSpawnMessage` → creates shadow at exact host position with Float animation
5. Client receives `ShadowStateUpdateMessage` every 0.3s → updates shadow position + distanceToPlayer
6. When shadow dies on host → `HostShadowDiePatch` unregisters it → next broadcast won't include it → client receives `dead=true` → plays Death1 animation → removed from lookup

---

## Session 2026-05-28 — Torch/light sync, dream choice popup, blood splatter sync

### Torch/light sync fix
- `SecondPlayerAnimController.UpdateEmitterPosition()`:
  - Falls back to "Idle" clip position when `CurrentClip` is null
  - Keeps current position instead of resetting to `(0,0,0)` for unknown clips / OOB frames
  - Made `internal` so `LanNetworkManager` can call it
- `HandlePlayerLightState` calls `SetEmittedItem()` then immediately calls `animCtrl.UpdateEmitterPosition()` so emitters snap to correct offset frame-1

### Dream choice popup
- New `UI/DreamChoiceGUI.cs`: OnGUI popup with Spectate (default) / Join buttons + 15s timeout
- `DreamSyncManager.OnRemoteDreamStarted()` now shows popup for regular dreams instead of immediate freeze
- Dual-presence dreams and epilogue still auto-join (no popup)
- `_pendingChoiceActive` tracked; `DreamChoiceGUI.Hide()` called on `OnRemoteDreamEnded()` and `OnDisconnected()`

### Dream spectate — position sync
- **Problem**: Host choosing Spectate would have camera follow the client's proxy (regular world) instead of staying in the dream scene → host saw regular world with dream camera effects
- **Fix**: `EnterDreamSpectator()` now uses `_dreamSpectatorTarget` (a hidden DontDestroyOnLoad GameObject) as the follow target when `_remoteDreamActive && !_isDualPresence`, instead of the remote proxy
- **Dreamer position sync**: `DreamPositionUpdateMessage` (type 79) sent by the dreamer at 10 Hz. `HandleDreamPositionUpdate()` updates the spectator target position on the receiving end.
- `StartDreamPositionSync()` / `StopDreamPositionSync()` coroutine in `DreamSyncManager` manages the send loop

### Blood splatter sync (comprehensive)
- `HitscanBloodPatch` (Prefix on `Core.AddPrefab("FX/Bloodsplats/*")`):
  - **Removed** `player.currentItem.isFirearm` and `item != null` guards
  - Now forwards ALL `FX/Bloodsplats/*` prefabs — from entity hits, player damage, friendly fire, enemy-vs-enemy
  - `ApplyingFromNetwork` guard prevents re-forwarding loops
- `HanleBulletImpact`: `Core.AddPrefab` for `FX/Bloodsplats/Shotsplat*` now wrapped in `ApplyingFromNetwork = true`

### Relevant files
- `Patches/BulletFXSyncPatch.cs`: `HitscanBloodPatch` — broadened to forward all blood
- `Players/SecondPlayerAnimController.cs`: `UpdateEmitterPosition()` — clip/position fallbacks
- `Networking/LanNetworkManager.cs`: `HandlePlayerLightState` — immediate `UpdateEmitterPosition()`; `HandleBulletImpact` — `ApplyingFromNetwork` guard; `HandleDreamPositionUpdate` — updates spectator target; `_dreamSpectatorTarget` — hidden follow target; `SendDreamPositionUpdate` — dreamer→spectator position sync
- `UI/DreamChoiceGUI.cs` (new): popup
- `Sync/DreamSyncManager.cs`: popup integration; `StartDreamPositionSync`/`StopDreamPositionSync` coroutine; `EnterDreamSpectator()` — uses `_dreamSpectatorTarget` instead of proxy when spectating remote dream
- `Networking/NetMessages.cs`: `DreamPositionUpdateMessage` + `DreamPositionUpdate = 79`

---

## Session 2026-05-28 (continued) — Dream sync simplified: non-dreamer freezes

### Root cause: dream scene divergence
When both machines load the dream scene independently, they produce different entity sets. The host's scene may miss entities the client's scene has (e.g. `Door_talkable_outside_bunker_underground_02`). Loading the dream scene on the host also corrupts the `WorldGrid` singleton, breaking the regular world server.

### Fix: non-dreamer just freezes
`DreamSyncManager.OnRemoteDreamStarted()` for regular (non-dual-presence) dreams now calls `ApplyDreamFreeze()` only — no scene loading, no popup, no spectate, no position sync. Dual-presence dreams (epilogue) still load the scene and follow the old path.

### Changes applied
- `Sync/DreamSyncManager.cs`: Regular remote dreams → `ApplyDreamFreeze()` only. Dual-presence path unchanged.
- `Networking/LanNetworkManager.cs`: Removed `_dreamSpectatorTarget`, `GetOrCreateDreamSpectatorTarget()`, `HandleDreamPositionUpdate()`, `SendDreamPositionUpdate()`, and the `DreamPositionUpdate` switch case — all dead code.
- `UI/DreamChoiceGUI.cs`: Dead code (no longer referenced), kept as orphan.

### Remaining concerns
- Dreamer still plays the dream normally (vanilla flow on both sides).
- Non-dreamer freezes (`FreezeTracker.AddFreeze()` → `Core.pause(keepMusicAndEnviromental: true)`).
- On dream end, non-dreamer unfreezes (`RemoveDreamFreeze()`).
- Entity cleanup guard (`if (DreamSyncManager.IsLocalDreamActive) return;`) in `ClientEntityInterpolationService` still in place.
- Blocking patch guards (`IsLocalDreamActive` → return true) in `ClientMeleeSensorPatch`, `ClientDamageRedirectPatch`, `DialogSyncInitiatePatch` still in place.
- `DreamPositionUpdateMessage` type 79 definition kept in `NetMessages.cs` for message type numbering compatibility.
