@echo off
setlocal EnableExtensions

REM HolmgangDuelMod isolated local dedicated-server launcher.
REM The shared Steam server is used only as a clean runtime source. Existing
REM BepInEx plugins/config/cache/logs are excluded from the isolated copy.

set "REPO_DIR=%~dp0.."
for %%I in ("%REPO_DIR%") do set "REPO_DIR=%%~fI"
set "BASE_SERVER_INSTALL=C:\PROGRA~2\Steam\steamapps\common\Valheim dedicated server"
set "ISOLATED_SERVER_DIR=%~dp0server"
set "PLUGIN_DIR=%ISOLATED_SERVER_DIR%\BepInEx\plugins"
set "SAVE_DIR=%ISOLATED_SERVER_DIR%\saves"
set "PORT=2463"
set "WORLD=holmgangduelmod_test"
set "PASSWORD=696970"

if not exist "%BASE_SERVER_INSTALL%\valheim_server.exe" goto :no_base_server
if not exist "%BASE_SERVER_INSTALL%\BepInEx\core\BepInEx.dll" goto :no_base_bepinex
if not exist "%BASE_SERVER_INSTALL%\winhttp.dll" goto :no_base_doorstop
if not exist "%REPO_DIR%\.deps\Jotunn-2.29.2\plugins\Jotunn.dll" goto :no_jotunn

if not exist "%ISOLATED_SERVER_DIR%\valheim_server.exe" (
    echo Creating clean isolated server copy. This may take a few minutes...
    robocopy "%BASE_SERVER_INSTALL%" "%ISOLATED_SERVER_DIR%" /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 ^
        /XD "%BASE_SERVER_INSTALL%\BepInEx\plugins" ^
            "%BASE_SERVER_INSTALL%\BepInEx\config" ^
            "%BASE_SERVER_INSTALL%\BepInEx\cache" ^
            "%BASE_SERVER_INSTALL%\logs" ^
        /XF "BepInEx\LogOutput.log"
    if errorlevel 8 goto :copy_server_failed
)

if not exist "%ISOLATED_SERVER_DIR%\BepInEx\plugins" mkdir "%ISOLATED_SERVER_DIR%\BepInEx\plugins"
if not exist "%ISOLATED_SERVER_DIR%\BepInEx\config" mkdir "%ISOLATED_SERVER_DIR%\BepInEx\config"
if not exist "%SAVE_DIR%" mkdir "%SAVE_DIR%"
copy /Y "%~dp0adminlist.txt" "%SAVE_DIR%\adminlist.txt" >nul
if errorlevel 1 goto :admin_copy_failed

REM Start from a clean plugin directory on every launch. The isolated copy is
REM disposable; no third-party plugin may survive into this test run.
del /Q /S "%PLUGIN_DIR%\*.dll" "%PLUGIN_DIR%\*.pdb" "%PLUGIN_DIR%\*.mdb" "%PLUGIN_DIR%\*.xml" >nul 2>&1
for /d %%D in ("%PLUGIN_DIR%\*") do rd /S /Q "%%~fD"

echo Building HolmgangDuelMod Release...
dotnet build "%REPO_DIR%\HolmgangDuelMod.sln" --configuration Release ^
    -p:ValheimInstall="%ISOLATED_SERVER_DIR%" ^
    -p:ValheimManaged="%ISOLATED_SERVER_DIR%\valheim_server_Data\Managed"
if errorlevel 1 goto :build_failed

if not exist "%REPO_DIR%\src\HolmgangDuelMod\bin\Release\netstandard2.1\HolmgangDuelMod.dll" goto :no_mod
if not exist "%REPO_DIR%\src\HolmgangDuelMod.Core\bin\Release\netstandard2.1\HolmgangDuelMod.Core.dll" goto :no_core
mkdir "%PLUGIN_DIR%\HolmgangDuelMod" >nul 2>&1
copy /Y "%REPO_DIR%\src\HolmgangDuelMod\bin\Release\netstandard2.1\HolmgangDuelMod.dll" "%PLUGIN_DIR%\HolmgangDuelMod\HolmgangDuelMod.dll" >nul
copy /Y "%REPO_DIR%\src\HolmgangDuelMod.Core\bin\Release\netstandard2.1\HolmgangDuelMod.Core.dll" "%PLUGIN_DIR%\HolmgangDuelMod\HolmgangDuelMod.Core.dll" >nul
mkdir "%PLUGIN_DIR%\Jotunn" >nul 2>&1
copy /Y "%REPO_DIR%\.deps\Jotunn-2.29.2\plugins\*" "%PLUGIN_DIR%\Jotunn\" >nul
if errorlevel 1 goto :deploy_failed

REM Enable the test harness on the server only. Client-local config does
REM not authorize this feature and is intentionally never copied from clients.
copy /Y "%~dp0HolmgangDuelMod.server.cfg" "%ISOLATED_SERVER_DIR%\BepInEx\config\catosaurluna.holmgangduelmod.cfg" >nul
if errorlevel 1 goto :test_config_copy_failed

REM Fail closed if anything other than the intended two mod assemblies exists.
powershell.exe -NoProfile -Command "$bad = Get-ChildItem -LiteralPath '%PLUGIN_DIR%' -Filter '*.dll' -File -Recurse | Where-Object { $_.Name -notin @('HolmgangDuelMod.dll','HolmgangDuelMod.Core.dll','Jotunn.dll') }; if ($bad) { $bad | ForEach-Object { Write-Error ('Unexpected plugin: ' + $_.FullName) }; exit 1 }"
if errorlevel 1 goto :outside_mod

set "SteamAppId=892970"
echo.
echo ============================================================
echo  HolmgangDuelMod clean isolated test server
echo ============================================================
echo  Server:  %ISOLATED_SERVER_DIR%
echo  World:   %WORLD%
echo  Connect: 127.0.0.1:%PORT%
echo  Plugins: HolmgangDuelMod + Jotunn only
echo  Saves:   %SAVE_DIR%
echo  Admins:  %SAVE_DIR%\adminlist.txt
echo.
echo  SteamID64 76561198062587799 is configured as an admin.
echo  Press CTRL+C to stop the server.
echo ============================================================
echo.

cd /d "%ISOLATED_SERVER_DIR%"
"%ISOLATED_SERVER_DIR%\valheim_server.exe" ^
    -name "HolmgangDuelMod Test" ^
    -port %PORT% ^
    -world "%WORLD%" ^
    -password "%PASSWORD%" ^
    -savedir "%SAVE_DIR%" ^
    -public 0
goto :eof

:no_base_server
echo ERROR: Shared Valheim dedicated server was not found at %BASE_SERVER_INSTALL%
pause
exit /b 1
:no_base_bepinex
echo ERROR: Shared server does not have BepInEx installed.
pause
exit /b 1
:no_base_doorstop
echo ERROR: Shared server does not have winhttp.dll.
pause
exit /b 1
:no_jotunn
echo ERROR: Local Jotunn 2.29.2 dependency is missing from .deps.
pause
exit /b 1
:copy_server_failed
echo ERROR: Could not create the clean isolated server copy.
pause
exit /b 1
:build_failed
echo ERROR: HolmgangDuelMod Release build failed.
pause
exit /b 1
:no_mod
echo ERROR: HolmgangDuelMod.dll was not produced.
pause
exit /b 1
:no_core
echo ERROR: HolmgangDuelMod.Core.dll was not produced.
pause
exit /b 1
:deploy_failed
echo ERROR: Could not deploy the intended plugins.
pause
exit /b 1
:admin_copy_failed
echo ERROR: Could not install the isolated test server admin list.
pause
exit /b 1
:test_config_copy_failed
echo ERROR: Could not enable the server-only HolmgangDuelMod test config.
pause
exit /b 1
:outside_mod
echo ERROR: An unexpected plugin DLL was found. Server launch refused.
pause
exit /b 1
