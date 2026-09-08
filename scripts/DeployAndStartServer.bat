@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "REPOSITORY_ROOT=%SCRIPT_DIR%.."
set "LOCAL_CONFIG=%SCRIPT_DIR%server.local.bat"

if not exist "%LOCAL_CONFIG%" (
    echo Missing local server configuration:
    echo   %LOCAL_CONFIG%
    echo.
    echo Copy server.local.bat.example to server.local.bat and edit it first.
    exit /b 2
)

call "%LOCAL_CONFIG%"

if not defined VALHEIM_SERVER_DIR (
    echo VALHEIM_SERVER_DIR is not configured.
    exit /b 2
)
if not defined VALHEIM_SERVER_EXE set "VALHEIM_SERVER_EXE=%VALHEIM_SERVER_DIR%\valheim_server.exe"
if not defined INSTALL_BEPINEX_BOOTSTRAP set "INSTALL_BEPINEX_BOOTSTRAP=1"

set "INSTALL_SWITCH=false"
if /I "%INSTALL_BEPINEX_BOOTSTRAP%"=="1" set "INSTALL_SWITCH=true"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%DeployAndStartServer.ps1" ^
  -RepositoryRoot "%REPOSITORY_ROOT%" ^
  -ServerDirectory "%VALHEIM_SERVER_DIR%" ^
  -ServerExecutable "%VALHEIM_SERVER_EXE%" ^
  -ServerArguments "%VALHEIM_SERVER_ARGS%" ^
  -ValheimInstallDirectory "%VALHEIM_INSTALL_DIR%" ^
  -InstallBepInExBootstrap $INSTALL_SWITCH

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo Deployment failed with exit code %EXIT_CODE%.
)
exit /b %EXIT_CODE%
