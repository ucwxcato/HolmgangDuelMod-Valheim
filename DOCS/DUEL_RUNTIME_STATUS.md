# HolmgangDuelMod runtime status

Updated 2026-09-15 after the Valheim 1.0.12 test session.

## Confirmed working

- Client and isolated server negotiate Valheim network version 40.
- Client and server load Jötunn 2.30.0.
- HolmgangDuelMod loads on the dedicated server.
- The `Character.Damage` Harmony patch installs on the dedicated server.
- The client receives duel presentation start and end RPCs.
- Release build succeeds and all 25 game-independent tests pass.

## Still broken or unverified

- The `/dueltest` server log does not record the expected command, Greydwarf
  spawn, or proxy-registration messages during the reported run. The server
  therefore does not yet provide sufficient evidence that the native proxy is
  bound to one authoritative object.
- Two Greydwarves can still appear in the test area. The current native
  `ZNetScene.SpawnObject` approach remains unreliable on the Valheim 1.0
  dedicated server, including preserved-world leftovers and duplicate object
  registration.
- Pre-countdown damage was observed by the tester. The arena-wide damage gate
  is implemented and loads, but live behavior is not verified while the proxy
  identity/registration path is failing.
- The client countdown can receive the start RPC and then terminate early
  when the coordinator loses the proxy participant; this was observed as the
  countdown stopping around 8.
- Real two-player pair-only combat filtering and PvP state restoration are
  still not implemented for ordinary player duels.

## Recommended next implementation

Replace the native Greydwarf test proxy with a server-owned deterministic test
combatant (or a non-networked health dummy plus explicit server damage events).
The test harness should not depend on scanning `ZNetScene.m_instances` after
`SpawnObject`; that scan is the source of duplicate identity and lifecycle
failures. Keep the real-player duel path separate until authoritative combat
hooks are implemented and verified.

