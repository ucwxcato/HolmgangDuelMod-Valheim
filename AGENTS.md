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

## Repository hygiene

- Keep generated binaries, local Valheim assemblies, BepInEx installations, logs, and player/world data out of source control.
- Use the repositoryâ€™s `DOCS/` folder for design and verification documents.
- Prefer reversible, narrowly scoped changes; preserve unrelated user work.

