# HolmgangDuelMod runtime status

Updated 2026-11-19.

## Build and test environment

- Test client profile: `C:\Users\magni\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Holmgang-TEST` (Jötunn 2.30.2, Server Devcommands).
- Jötunn 2.30.2 is pinned for the build in `.deps\Jotunn-2.30.2` (gitignored) and is the version the test profile already carries.
- Runtime compile references: the Valheim dedicated server at `C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server`. The managed folder there is `valheim_server_Data\Managed`, so pass `-p:ValheimManaged=` explicitly. Without it the runtime define is not set and the build silently compiles only the scaffold.
- `dotnet test` passes 31 pure tests. The Release build with the runtime define succeeds.

## Changed on 2026-11-19

- The native Greydwarf test proxy is gone. `GreydwarfTestCombatant`, its `ZNetScene` identity scans, and the `Character.Damage` countdown hook (`DuelCombatHooks`) were deleted.
- The test opponent is now `VirtualTestCombatant` in Core: a non-networked, in-memory opponent with no Valheim object, no ZDO, and no world-save entry. The same coordinator evaluates it as a real player.
- Countdown protection is enforced by the combatant itself. `/dueltest damage` is refused while the session is in countdown and applied only when it is Active.
- The coordinator destroys the test opponent on every end path, so cleanup no longer depends on the presentation layer.
- Fixed: a test duel that ended by health or radius left its bookkeeping behind, so the next `/dueltest start` was refused. Test bookkeeping is now pruned.
- `LegacyTestCreatureCleanup` runs once at server start and removes Greydwarfs tagged by older builds from the reused test world. Delete it when that world is clean.
- `start_holmgangduelmod_test.bat` now uses the `Holmgang-TEST` profile. It also no longer `mkdir`s the `Unknown-HolmgangDuelMod*.dll` targets, which made the deploy step fail.

## Live singleplayer results (2026-11-19, Holmgang-TEST profile)

Confirmed in-game with the host as the server, character at full HP:

1. Countdown runs 5 to 0 in chat, and the bubble appears when it starts.
2. `/dueltest damage 0` during countdown is refused as protected; status stays Countdown.
3. The duel survives countdown end and status reads Active.
4. `/dueltest damage 50` keeps the duel going.
5. `/dueltest damage 0` ends the duel; flag and bubble disappear; status reads no active duel.
6. `/dueltest start` works again after the 5-second cooldown without a cancel.
7. Walking out of the radius ends the duel.

Earlier end-of-countdown failure: the character was actually at 1 HP, so the health-threshold rule fired. The `Local health probe` log line exists to confirm which Valheim health accessor is reliable.

Confirmed after the command-path fix: `/dueltest leave` and `/dueltest cancel` clear the flag and bubble, and the flag is white with a brown pole.

Known gaps: a duel can start while a participant is at or below the defeat threshold, so it ends at once. Accepted for now; players are not expected to start at 1 HP. Flag cloth and pole use the unlit `Sprites/Default` shader to avoid the missing-shader purple.

## Authored on 2026-11-19, not yet verified

- Chat result announcement ("X won the duel against Y: reason."). Verified in singleplayer only through code paths; the multiplayer broadcast is untested.
- Pair-only combat: player damage is allowed only between the two Active participants. Other player-vs-player damage to or from a duelist is blocked. Pure rules are tested, but the Harmony hook on `Player.Damage` and the PvP enable/restore have not run in a live session.
- PvP state: duelists get PvP on at Active, and their original setting is restored on every end path. Each original is written to `BepInEx\config\catosaurluna.holmgangduelmod.pvp-journal.txt` before PvP is enabled. If the server stops mid-duel, the setting is restored when that player returns. There is a short window after login before that restore runs, so a crash mid-duel is not fully safe yet.
- Real duel commands: `/duel <name>`, `/duel accept <name>`, `/duel cancel` and `/duel status` now run on the dedicated server through the same RPC path as `/dueltest`. Before this change they ran on each client's own duel manager, so two players could never duel. `/duel <name>` accepts a pending request from that player (reciprocal acceptance). The challenged player gets a chat line from the server. Keyword and name matching are case-insensitive, with unit tests. Two-client test not yet run.
- Player identity: all server paths use one ID, the platform user ID matched through the character ZDO ID. Before this change, local players were keyed by character ID and remote players by platform ID, so duelists on a dedicated server could not be matched. The fallback to character ID is logged as a warning.

## Not yet verified in a live Valheim session

- Pair-only combat and PvP restoration, with two real players (singleplayer cannot exercise this).
- Real two-player duels, including the winner message on clients.
- Phase 0 interception spike from the plan: it is still open, so the hook strategy has not been proven safe on a dedicated server.

Creature hits no longer exercise the test, because the opponent is no longer a creature. Test damage is an explicit command.

## History: 2026-09-15 live session (native proxy, superseded)

- Two Greydwarves could appear in the test area. Native `ZNetScene.SpawnObject` registration was unreliable on the Valheim 1.0 dedicated server, including preserved-world leftovers and duplicate object registration.
- Damage landed before the countdown ended. The gate was implemented but was not verified while proxy identity was failing.
- The client countdown stopped around 8 when the coordinator lost the proxy participant.
- The server log did not show the expected command, spawn, or registration lines during that run.
