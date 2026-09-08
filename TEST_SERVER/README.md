# HolmgangDuelMod clean isolated test server

`start_holmgangduelmod_test.bat` uses the existing Steam Valheim dedicated-server installation as a runtime source, then creates a separate `TEST_SERVER\server` copy.

The isolated copy excludes the shared server’s BepInEx plugins, configs, cache, and logs. Each launch clears its plugin directory and deploys only:

- `HolmgangDuelMod.dll`
- `Jotunn.dll` and its package support files

The launcher refuses to start if another plugin DLL is detected.

## First run

1. Confirm the shared server is installed at `C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server`.
2. Run `start_holmgangduelmod_test.bat`.
3. Wait for the clean copy to be created and the server to finish starting.
4. Connect from a clean client profile to `127.0.0.1:2463`.
5. Add your Steam ID64 to `server\BepInEx\config\adminlist.txt` before using admin-only test commands.

The test world is `holmgangduelmod_test`, the local password is `696970`, and the server is not publicly listed.

Delete `TEST_SERVER\server` only when you intentionally want to recreate the clean copy from the shared runtime source. The launcher does not delete it automatically.
