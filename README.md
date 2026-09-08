# HolmgangDuelMod

Valheim opt-in dueling mod by **catosaurluna**.

The canonical implementation plan is [DOCS/DUEL_DEVPLAN.md](DOCS/DUEL_DEVPLAN.md).

## Development prerequisites

- .NET SDK 8.x
- A local Valheim installation for runtime/plugin compilation
- BepInExPack Valheim `5.4.2333`
- Jötunn `2.29.2`

The BepInEx and Jötunn packages are available in the ignored `.deps/` directory for local development. Valheim game assemblies are intentionally not stored in this repository.

To enable runtime references, set `ValheimInstall` in an untracked `Environment.props` file or pass it to MSBuild:

```powershell
dotnet build HolmgangDuelMod.sln -p:ValheimInstall="C:\Path\To\Valheim"
```

Without that path, the solution builds the scaffold and core project but does not compile against Valheim APIs.

## Deploy and start a local dedicated server

Copy `scripts/server.local.bat.example` to `scripts/server.local.bat`, edit the paths and server arguments, then run:

```text
scripts\DeployAndStartServer.bat
```

The script builds Release, deploys HolmgangDuelMod and Jötunn, optionally bootstraps BepInEx, and starts the configured server. Keep passwords only in the ignored `server.local.bat` file.

HolmgangDuelMod is deployed together with `HolmgangDuelMod.Core.dll`, which is a required runtime dependency.
