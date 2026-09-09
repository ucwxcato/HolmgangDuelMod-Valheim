# HolmgangDuelMod â€” Development Plan

> **Status:** Implementation in progress; pure-domain tests, runtime build wiring, server-only test authorization, Greydwarf proxy, and temporary visuals are implemented. Live gameplay verification remains outstanding.
>
> **Author:** catosaurluna
>
> **Purpose:** Let two nearby players mutually opt into a temporary, bounded duel and receive an unambiguous winner without changing PvP for anyone else.
>
> **Authority:** This document owns the HolmgangDuelMod MVP behavior, state machine, configuration, safety rules, and verification gates. It is the canonical plan for this repository.
>
> **Target:** Valheim on the current supported Steam/PC build at implementation time, loaded through BepInEx. Local development packages fetched are BepInExPack Valheim `5.4.2333` and Jötunn `2.29.2`; the exact Valheim game build and runtime compatibility remain unverified.

## 0. Outcome

Two players stand near each other, use `/duel <name>` in sequence, see a 10-second countdown and temporary duel boundary, fight with duel-only damage enabled, and receive a winner when one reaches the configured defeat threshold or leaves the boundary. The duel is cleaned up on every exit path.

Complete when:

```text
two valid nearby players -> mutual /duel commands -> countdown + marker/boundary -> duel-only combat -> one valid end condition -> winner message + full cleanup
```

## 1. Locked decisions

- **Mutual command acceptance:** `/duel <player>` sends a request when no matching request exists; the target runs `/duel <requester>` to accept. This matches the requested interaction while preventing unsolicited combat.
- **One active duel per player:** A player cannot send, accept, or join another duel while pending or active, unless they first cancel/finish the current interaction.
- **Proximity is checked twice:** both players must be within `RequestMaxDistance` when requesting/accepting, and remain within the active duel radius after the countdown.
- **Countdown defaults to 10 seconds:** countdown duration is configurable, but must be positive and must not be bypassable by a client command.
- **Leaving the radius is an immediate loss:** the first participant proven outside the boundary loses; if both are outside in the same authoritative tick, the duel is a draw/failsafe rather than awarding an arbitrary winner.
- **Defeat threshold defaults to 1 HP:** reaching `DefeatHealth` or less ends the duel; the implementation must prevent ordinary death cleanup from producing a second result.
- **Duel-only PvP:** do not rely on a global PvP toggle as the sole rule. Damage is allowed only when the attacker and target are the two active duel participants. Any pre-duel PvP state is restored after cleanup.
- **Countdown safety:** no duel PvP setting or combat override may be applied during `Countdown`. Duel-only combat enforcement begins only when the authoritative countdown reaches zero and the session transitions to `Active`.
- **Temporary presentation:** the flag and optional ward-like bubble are runtime objects/effects only. They must not create permanent build pieces or world-save entries.
- **Server test harness:** provide a disabled-by-default server-only simulated opponent so one person can test duel lifecycle, countdown, arena exit, and cleanup without a second real player. When explicitly enabled on the test server, the harness does not require a separate admin-list permission; it is never enabled by a client-local config value.
- **Creature-backed test opponent:** the admin test harness should use a native `Greydwarf` prefab as the runtime combat proxy for the logical `TestOpponent`. Its normal AI, movement, attacks, health, and death should drive the same duel result paths, while the creature remains temporary and is removed during every cleanup path.
- **Server-only test authorization:** `EnableAdminTestMode` is a server-side setting. A client-local config value and client-side admin claim must never authorize spawning, damage simulation, or duel results; test commands require a server-authoritative RPC/config gate.
- **No rewards in MVP:** the mod declares a winner but does not transfer items, currency, experience, trophies, or rankings.
- **Server/mod requirement:** all participating clients must run a compatible HolmgangDuelMod build; the host/dedicated-server behavior and exact sync strategy are a Phase 0 compatibility gate, not an assumption hidden in implementation.

## 2. Goals and non-goals

### Goals

- Chat commands for request, acceptance, cancellation, and status/help feedback.
- Name lookup that handles duplicate/partial names safely and reports ambiguity.
- Configurable request distance, countdown, duel radius, health threshold, marker, bubble, messages, and cooldowns.
- Clear countdown, start, boundary, end, winner, loser, draw, and cancellation feedback.
- A single in-memory duel state machine with authoritative validation and deterministic cleanup.
- Compatibility-minded hooks for player damage/PvP, movement/radius checks, disconnects, death, world changes, and plugin shutdown.
- Automated state-machine tests plus a two-player in-game smoke-test checklist.

### Explicitly out of scope

- Arenas, protected zones, matchmaking, spectators, teams, tournaments, rankings, persistence, replays, or a web UI.
- Duel kits, item locking, inventory snapshots, item betting, loot, or rewards.
- More than two participants, multi-round matches, rematches, or automatic queueing.
- A permanent custom flag item or permanent ward/building piece.
- Replacing the serverâ€™s general PvP rules outside an active duel.

## 3. User experience / operational flow

1. Player A types `/duel PlayerB`.
2. HolmgangDuelMod resolves a unique online player, checks self-targeting, duel state, world/instance compatibility, and distance. It creates a pending request with an expiry and tells both players how to accept.
3. Player B types `/duel PlayerA`. HolmgangDuelMod revalidates the request, identity, distance, and availability. Invalid or expired requests fail without side effects.
4. HolmgangDuelMod creates the active session at a fixed center (default: midpoint between players after acceptance), spawns the temporary flag and optional boundary presentation, and starts the countdown. Combat remains blocked by HolmgangDuelMod during countdown.
5. Each countdown second is announced through configured chat/center-screen feedback. A disconnect, death, invalid player, or radius violation during countdown cancels safely unless configuration explicitly says a pre-start violation forfeits; MVP uses cancellation.
6. At zero, HolmgangDuelMod marks the session `Active`, records both participantsâ€™ original relevant PvP state, and enables only the pairâ€™s duel damage rule.
7. During `Active`, the host/authority checks health and distance at a bounded interval and the damage hook filters eligible attacker/target pairs. The first terminal condition wins by the rules below.
8. HolmgangDuelMod transitions once to `Ending`, determines winner/loser/draw, announces the result, disables duel-only combat, restores state, removes marker/bubble, clears indexes, and applies the configured post-duel cooldown.

Suggested commands:

```text
/duel <player>       Send a request or accept that player's pending request
/duel accept <player>Explicit acceptance alias; same validation as reciprocal command
/duel cancel          Cancel your pending request or countdown
/duel status          Show your current request/duel state
/duel help            Show command syntax and restrictions
```

Admin-only test commands, available only when testing is explicitly enabled:

```text
/dueltest start       Start a local test duel against the simulated TestOpponent
/dueltest damage <hp> Set the simulated opponent's health and evaluate defeat rules
/dueltest leave       Simulate the opponent leaving the duel radius
/dueltest cancel      Cancel the simulated duel through the normal cleanup path
/dueltest reset       Force test-state cleanup after a failed development run
```

The logical simulated opponent has a stable fake participant ID and display name. In the preferred runtime harness it is represented by a temporary native Greydwarf combat proxy. The proxy is not a fake player, must not be saved permanently, and must be removed if the duel, world, plugin, or authority ends unexpectedly. A headless/pure simulated participant remains available for unit tests.

## 4. Architecture and ownership

The following paths are proposed; they do not exist yet.

```text
HolmgangDuelMod.sln
src/HolmgangDuelMod/
  Plugin.cs                 BepInEx entry point, config, dependency checks, lifecycle
  Commands/DuelCommand.cs   Chat parsing, player lookup, user-facing validation
  Domain/DuelSession.cs     Immutable IDs, participants, center, radius, timestamps
  Domain/DuelState.cs       Pending/Countdown/Active/Ending/Completed/Cancelled
  Services/DuelManager.cs   Single owner of indexes, transitions, idempotent cleanup
  Services/DuelRules.cs     Distance, health, damage-pair, timeout, and cooldown rules
  Integrations/ValheimHooks.cs Harmony/game event adapters; no business policy
  Presentation/DuelVisuals.cs Temporary flag, optional bubble, countdown/result messages
  Configuration/DuelConfig.cs Typed config binding and validation
tests/HolmgangDuelMod.Tests/       Pure state-machine and rule tests
DOCS/DUEL_DEVPLAN.md       Canonical behavior and verification contract
```

Ownership boundaries:

- `DuelManager` owns truth and may be the only component that changes session state.
- `ValheimHooks` translates game events into domain calls; it must not independently declare winners.
- `DuelRules` owns eligibility and terminal-condition precedence.
- `DuelVisuals` owns presentation and cleanup of temporary objects; visuals never authorize damage.
- BepInEx owns plugin loading/config persistence. JÃ¶tunn is a helper/dependency boundary, not a second duel manager.

Phase 0 must spike the exact Valheim APIs for chat interception, stable player identity, player health, damage attribution, disconnect/death/world events, prefab/effect spawning, and server/host synchronization before names are treated as final.

## 5. Data, lifecycle, and failure handling

### Session state

```text
None -> Pending -> Countdown -> Active -> Ending -> Completed
                 \-> Cancelled
```

`DuelSession` should contain: unique session ID, two stable participant IDs, display-name snapshots, world/instance identity, center, radius, creation/start/deadline timestamps, state, original PvP values if changed, end reason, winner/loser/draw, and cleanup marker. Do not persist it to the world in MVP.

Required invariants:

- Participant IDs are unique and indexed to at most one pending/active session.
- Only legal state transitions are accepted; repeated end signals are no-ops after `Ending`.
- The winner decision is made once using a deterministic precedence: explicit invalid/disconnect/leave event, health threshold, then tick order/authority tie-breaker; simultaneous terminal conditions become a draw where no fair winner can be proven.
- Cleanup runs from `try/finally`-equivalent lifecycle handling and is safe after partial visual spawn failure.
- On plugin reload, world unload, server shutdown, or lost authority, all local sessions are cancelled and temporary objects removed; no stale duel is resumed.
- Pending requests expire after `RequestTimeout` and are rate-limited per sender/target.

## 6. Configuration, permissions, and integrations

Proposed BepInEx config contract (names may be adjusted to actual binding conventions):

```ini
[Duel]
RequestMaxDistance = 20
DuelRadius = 15
CountdownSeconds = 10
DefeatHealth = 1
RequestTimeoutSeconds = 30
PostDuelCooldownSeconds = 5
BoundaryCheckIntervalSeconds = 0.10
EnableFlag = true
EnableBubble = true
BubblePrefabOrEffect = ward-like runtime effect selected during Phase 0
AllowDuelWhileGlobalPvpEnabled = true

[Testing]
EnableAdminTestMode = false
```

- Default permission is ordinary player access; the test harness is separately gated by server-only configuration and disabled by default.
- Commands must be case-insensitive for command keywords and use a safe, unambiguous player resolver for names.
- If JÃ¶tunn or a required helper is missing/incompatible, the plugin must fail at load with a clear error rather than partially enabling duels.
- Configuration must be validated at load: positive distances/times, `DefeatHealth > 0`, sane upper bounds, and no negative cooldowns. Invalid values use safe defaults and log the correction.
- Test mode must require server-authoritative `EnableAdminTestMode = true`. A client-local config value is ignored; `dueltest` commands must be rejected until the server gate authorizes them.
- The simulated opponent must call the same `DuelManager`, rules, countdown, radius, result, and cleanup paths as a real duel; only the participant/event adapter is simulated.
- Configuration synchronization for dedicated servers is TBD until Phase 0 identifies whether host-only or client-visible settings are authoritative. Player-facing visuals must use the authorityâ€™s session values.

## 7. Safety, security, and product constraints

- Treat every chat command, client callback, damage event, and visual object as untrusted input. Revalidate session membership and current state at the boundary.
- Never trust display names, client-reported distance, client-reported health, or client-reported winner claims.
- Damage filtering must check both stable participant IDs, active session ID, world identity, and current state. It must fail closed when attribution is missing.
- A global `Player.m_pvp` change alone is prohibited because it can expose a duelist to unrelated players. If a vanilla hook makes pair-only filtering impossible, Phase 0 must stop and resolve the compatibility strategy before MVP implementation.
- Record only the minimum identifiers needed in logs; do not log chat contents or unrelated player data. Log state transitions and cleanup failures at useful levels.
- Rate-limit requests and repeated invalid commands. Do not allow request spam to extend expiry or lock a target indefinitely.
- If a flag/bubble spawn fails, the duel may continue only if the boundary can still be enforced and players are clearly informed; otherwise cancel before `Active`.
- If the authority cannot validate a boundary or combat event, cancel safely rather than awarding a potentially false win.

## 8. Verification matrix

| Scenario | Expected result | Evidence required |
|---|---|---|
| Valid nearby request and reciprocal acceptance | One countdown starts for exactly two players | Automated transition test plus two-client manual log/video or checklist |
| Far-away, self, offline, ambiguous, or busy target | Clear refusal; no session or side effect | Command/rule tests and runtime chat output |
| Countdown reaches zero | Boundary/marker remain, session becomes Active, only pair can damage each other | Two-client gameplay smoke test and logs |
| Duelist attacks unrelated player | Damage is blocked according to the duel rule; unrelated combat is not globally enabled | Three-player runtime test |
| Participant reaches `DefeatHealth` | Exactly one winner/result; cleanup occurs | Health-threshold test and runtime result/cleanup evidence |
| Participant leaves radius | Leaver loses immediately; opponent wins | Movement test at boundary, including jitter/threshold behavior |
| Both leave or terminal signals arrive together | Draw/cancel according to deterministic rule; no arbitrary winner | Rule test with same-tick inputs |
| Disconnect, death, world transfer, plugin shutdown | No stale active duel, visuals removed, safe result/cancel message | Failure-path tests and restart/shutdown smoke test |
| Request expires/cancel/cooldown | Request/session indexes are released and spam is bounded | Time-controlled unit tests |
| Admin test mode starts simulated duel | Authorized admin can exercise the normal countdown, marker, bubble, and lifecycle without another player | Admin runtime smoke test with testing explicitly enabled |
| Admin test mode simulates damage or opponent leave | Normal winner/loser and cleanup paths run exactly once | `/dueltest damage`, `/dueltest leave`, result log, and cleanup evidence |
| Ordinary player invokes `/dueltest` or testing is disabled | Command is refused and no simulated participant/session is created | Permission/config integration test |
| Missing dependency or invalid config | Plugin fails closed or applies validated safe defaults with actionable log | Startup log evidence |
| Vanilla PvP already enabled/disabled before duel | Original state is preserved after every ending path | Runtime test matrix |

## 9. Phased checklist

### Phase 0 â€” Design lock and runtime spike

- [x] Create the C# solution/project and build metadata without checking in Valheim/BepInEx binaries. Scaffold builds in Debug without runtime references and in Release against the local dedicated-server assemblies.
- [x] Pin the initial BepInEx pack (`5.4.2333`) and Jötunn package (`2.29.2`) for development; keep them in ignored `.deps/` storage.
- [ ] Pin the supported Valheim build, Harmony strategy, and client/server installation requirement after runtime testing.
- [ ] Verify exact chat, player identity, health/damage, disconnect/death/world, prefab/effect, and synchronization APIs in the target runtime.
- [x] Add the Jötunn `CommandManager`/`ConsoleCommand` transport adapter for `duel` and `dueltest`; local API/XML verification and a runtime build pass, but live in-game command smoke testing remains pending.
- [ ] Use `TEST_SERVER/start_holmgangduelmod_test.bat` to create an isolated server copy that excludes outside plugins/config/cache/logs and deploys only HolmgangDuelMod plus Jötunn.
- [ ] Prove a safe pair-only damage interception strategy with a minimal throwaway hook.
- [ ] **Verify:** clean build plus a host and two clients load the empty plugin without errors; spike results and versions are recorded here.

### Phase 1 â€” Pure MVP domain and commands

- [x] Implement pure typed config validation with safe fallback values and correction reporting; BepInEx binding/logging remains runtime work. The `DuelSession`/`DuelManager` state machine is implemented and covered by the initial pure-domain tests below.
- [x] Implement game-independent deterministic player resolution, request expiry, cooldown, and command feedback; Valheim player-directory wiring remains runtime work.
- [x] Implement game-independent parsing for `/duel`, `/duel accept`, `/duel cancel`, `/duel status`, `/duel help`, and `/dueltest` command shapes.
- [x] Define and implement a simulated participant path with a stable fake ID, display name, center position, and test health.
- [x] Add disabled-by-default `EnableAdminTestMode` configuration and an administrator authorization abstraction.
- [x] Implement `/dueltest start`, `/dueltest damage <hp>`, `/dueltest leave`, `/dueltest cancel`, and `/dueltest reset` in the game-independent command service.
- [x] Ensure test commands inject domain events through `DuelManager` instead of directly mutating session state.
- [x] Implement unit tests for legal/illegal transitions, duplicate requests, timeouts, cooldowns, duplicate end signals, command services, and runtime coordination; 24 tests pass.
- [x] Add initial pure-domain tests for countdown gating, horizontal radius checks, reciprocal acceptance, request expiry, cooldown, and idempotent session ending; `dotnet test` passes with 6 tests.
- [x] **Verify:** 20 pure-domain tests pass; command logic has no direct Unity/game-state mutation. Runtime player-directory and chat-service wiring remain in Phase 2/compatibility work.

### Phase 2 â€” Runtime duel mechanics

- [ ] Complete and runtime-verify Valheim adapters for proximity, countdown ticking, health threshold, disconnect/death/world cleanup, and boundary checks; the local player snapshot adapter and Unity update-loop wiring are implemented below but have not yet passed an in-game smoke test.
- [x] Add the initial Valheim player-directory adapter using the installed build's `Player.GetAllPlayers()`, local-player identity, position, health, death, and world-name APIs; Release compilation against the dedicated-server assemblies passes. Network authority and live smoke verification remain pending.
- [x] Implement the game-independent runtime coordinator for countdown activation, health threshold, radius exit, disconnect/death/world-transfer outcomes, and idempotent presentation cleanup; 4 coordinator tests pass.
- [ ] Run the simulated opponent through the normal countdown, marker/bubble, radius, result, and cleanup paths.
- [x] Implement the native Greydwarf combat proxy for admin test mode, including prefab lookup, temporary spawn, health/position polling, death detection through the runtime coordinator, and explicit cleanup; live in-game spawn/death verification remains pending.
- [x] Add a server-authoritative `/dueltest` RPC/config gate that validates the sender identity and server admin list before executing test commands; live network smoke verification remains pending.
- [ ] Ensure the test opponent is never a visible/networked Valheim `Player` entity and cannot be used as a duel damage source outside the test harness.
- [ ] Add pair-only combat filtering and preservation/restoration of relevant pre-duel PvP state.
- [ ] Add temporary flag and optional bubble presentation with guaranteed cleanup.
- [x] Implement compile-verified temporary center flag/totem and configurable translucent bubble presentation with an owner-controlled cleanup object; live visual smoke verification remains pending.
- [ ] **Verify:** two-player and three-player runtime matrix passes, including unrelated-player damage isolation and all end paths.

### Phase 3 â€” Hardening and release packaging

- [ ] Add structured logs, compatibility/version checks, rate limits, and clear missing-dependency behavior.
- [ ] Add README/install instructions, configuration reference, and a release package that excludes local game assemblies and logs.
- [ ] Test reload/shutdown, dedicated-server/client mismatch, malformed names, rapid commands, boundary jitter, and simultaneous terminal events.
- [ ] **Verify:** clean installation on the target Valheim build, no stale state after restart, and every Phase 3 verification item has recorded evidence.

## 10. Open tuning points

- **Runtime authority model:** host-only versus all-client hooks, determined by Phase 0 and dedicated-server testing.
- **Exact flag/bubble asset:** vanilla banner/effect or a JÃ¶tunn-registered temporary prefab, selected after testing cleanup and network visibility.
- **Boundary geometry:** radial horizontal distance only versus 3D distance; default plan assumes horizontal distance with a documented vertical tolerance.
- **Tie behavior:** draw is the safe default when both players cross a terminal condition in the same authoritative update.
- **Message transport:** chat-only versus optional center-screen countdown, based on the least intrusive compatible Valheim UI hook.

## References

- JÃ¶tunn repository and installation/dependency guidance: <https://github.com/Valheim-Modding/Jotunn>
- JÃ¶tunn quickstart and publicized assembly guidance: <https://valheim-modding.github.io/Jotunn/guides/quickstart.html>
- JÃ¶tunn example project/build setup: <https://github.com/Valheim-Modding/JotunnModExample>

These references informed the proposed tooling baseline; they do not verify that this empty repository has any dependency installed.

