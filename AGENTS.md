# HolmgangDuelMod agent guide

## Project purpose

HolmgangDuelMod is a Valheim BepInEx plugin by `catosaurluna` that provides opt-in, two-player duels. The canonical design and acceptance criteria are in [DOCS/DUEL_DEVPLAN.md](DOCS/DUEL_DEVPLAN.md).

## Source of truth

- Treat `DOCS/DUEL_DEVPLAN.md` as the product and architecture authority until implementation evidence changes it.
- Mark work as planned or authored until it has been built and verified in a real Valheim runtime. Do not mark checklist items complete from source inspection alone.
- If implementation and the plan diverge, update the plan in the same change or explicitly document why the plan remains authoritative.

## Technical baseline

- Target a BepInEx Valheim plugin in C#.
- Use Harmony for narrow game hooks and JÃ¶tunn for supported Valheim modding helpers where useful.
- Keep game-assembly references out of distributable packages; use local developer references/publicized assemblies.
- Treat the host/server as the authority for duel lifecycle and outcomes where the Valheim runtime permits it. Client-only visual effects must never decide a winner.

## Non-negotiable invariants

- A duel has exactly two participants and one authoritative lifecycle state.
- Requests, acceptance, cancellation, timeout, death/near-death, disconnect, world transition, and radius exit must be idempotent and must clean up all duel state.
- Never leave a playerâ€™s pre-duel PvP setting changed after a duel ends.
- Never make a player vulnerable to unrelated players merely because they entered a duel.
- Validate player identity by stable platform/player identity, not display name alone; display names are presentation and lookup input only.
- Do not spawn permanent world objects or write world-save data for a temporary duel marker unless the plan is deliberately revised.
- Fail closed when the plugin cannot prove that both participants or the duel authority are valid.

## Workflow expectations

- Read the canonical plan before changing architecture or public behavior.
- Prefer small, testable services: command/request handling, duel state machine, combat/radius rules, presentation, and cleanup.
- Add or update tests for every state transition and invalid-input path that can be tested without the game.
- Before declaring a phase complete, run the phaseâ€™s stated verification gate and record the evidence in the plan or a linked test report.
- Do not add arenas, rankings, spectators, rewards, kits, persistence, or matchmaking to the MVP without an explicit plan revision.

## Local test-server configuration

- Launch the isolated test server with `TEST_SERVER\start_holmgangduelmod_test.bat`.
- The canonical test client is the r2modman profile at `C:\Users\magni\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Holmgang-TEST` (renamed from `HolmgangDuelMod` on 2026-11-19; the old profile is no longer on this machine). After every successful test-server build, deploy both `HolmgangDuelMod.dll` and `HolmgangDuelMod.Core.dll` into `BepInEx\plugins\HolmgangDuelMod\` under their real assembly names. Do not rename the Core DLL: `HolmgangDuelMod.dll` references it by assembly name, and the `Unknown-HolmgangDuelMod*.dll` layout made the plugin fail to instantiate on 2026-11-19, so `/dueltest` was unknown. In singleplayer the profile config `BepInEx\config\catosaurluna.holmgangduelmod.cfg` needs `[Testing] EnableAdminTestMode = true`, because the host is the server. Do not alter the profile's other mods or its JÃ¶tunn package.
- The launcher uses `TEST_SERVER\server\saves` through Valheim's `-savedir` argument. The test world is stored under `server\saves\worlds_local\holmgangduelmod_test.*` and must be reused between runs; do not regenerate it for ordinary test cycles.
- Valheim's authoritative admin-list path is `server\saves\adminlist.txt`, not `server\BepInEx\config\adminlist.txt`. The current `/dueltest` harness does not require admin-list permission, but the server-only `EnableAdminTestMode = true` setting is required and client-local configuration cannot enable it.
- The server test harness uses a server-owned, non-networked `VirtualTestCombatant` (Core) as the simulated `TestOpponent`. It has no Valheim object, ZDO, or world-save entry; its health and position live in memory and the same coordinator evaluates it as a real player. Server-to-client presentation RPCs still drive the temporary duel flag/bubble. The server remains authoritative for lifecycle, outcomes, cleanup, and test-mode enablement.
- The native Greydwarf proxy, its `ZNetScene` identity scans, and the `Character.Damage` countdown hook were removed on 2026-11-19. `LegacyTestCreatureCleanup` runs once at server start and removes Greydwarfs tagged by older builds from the reused test world; delete it once that world is clean.
- Test damage is an explicit `/dueltest damage <hp>` event. It is refused with a "protected" message while the countdown runs and is applied only once the session is Active. Hitting a creature no longer tests anything.
- The virtual-opponent build is **untested in a live Valheim session**. Before claiming it works, run one `/dueltest start` from the `Holmgang-TEST` profile and record: no creature spawns; visible flag and bubble; chat countdown 5 to 0; `/dueltest damage 0` before zero is refused and health is unchanged; the same command after zero ends the duel with the caller as winner; a second `/dueltest start` works without `/dueltest cancel`. Do not claim this verified until those observations are recorded in DOCS/DUEL_DEVPLAN.md.
- If runtime failures occur, inspect `TEST_SERVER\server\BepInEx\LogOutput.log` and preserve the existing isolated world before changing save paths or deleting generated data.

## Repository hygiene

- Keep generated binaries, local Valheim assemblies, BepInEx installations, logs, and player/world data out of source control.
- Use the repositoryâ€™s `DOCS/` folder for design and verification documents.
- Prefer reversible, narrowly scoped changes; preserve unrelated user work.

