param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [string]$ServerDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ServerExecutable,
    [string]$ServerArguments = '',
    [string]$ValheimInstallDirectory = '',
    [bool]$InstallBepInExBootstrap = $true
)

$ErrorActionPreference = 'Stop'

function Assert-Path([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description was not found: $Path"
    }
}

Assert-Path $RepositoryRoot 'Repository root'
Assert-Path $ServerDirectory 'Dedicated server directory'
Assert-Path $ServerExecutable 'Dedicated server executable'

$solution = Join-Path $RepositoryRoot 'HolmgangDuelMod.sln'
$depsRoot = Join-Path $RepositoryRoot '.deps'
$bepRoot = Join-Path $depsRoot 'BepInExPack_Valheim-5.4.2333\BepInExPack_Valheim'
$jotunnRoot = Join-Path $depsRoot 'Jotunn-2.29.2'
$jotunnPlugin = Join-Path $jotunnRoot 'plugins'
$serverBepInEx = Join-Path $ServerDirectory 'BepInEx'
$serverPlugins = Join-Path $serverBepInEx 'plugins'
$modPluginDirectory = Join-Path $serverPlugins 'HolmgangDuelMod'
$jotunnPluginDirectory = Join-Path $serverPlugins 'Jotunn'

Assert-Path $solution 'HolmgangDuelMod solution'
Assert-Path $jotunnPlugin 'Jötunn package plugin directory'

Write-Host 'Building HolmgangDuelMod Release...' -ForegroundColor Cyan
$buildArgs = @('build', $solution, '--configuration', 'Release', '--no-restore')
if ($ValheimInstallDirectory) {
    $buildArgs += "-p:ValheimInstall=$ValheimInstallDirectory"
}
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}

$buildRoot = Join-Path $RepositoryRoot 'src\HolmgangDuelMod\bin\Release'
$modCandidates = @(Get-ChildItem -LiteralPath $buildRoot -Filter 'HolmgangDuelMod.dll' -File -Recurse | Sort-Object LastWriteTime -Descending)
if ($modCandidates.Count -eq 0) {
    throw "No HolmgangDuelMod.dll was produced under $buildRoot."
}
$modBinary = $modCandidates[0]
$coreBinary = Join-Path $modBinary.Directory.FullName 'HolmgangDuelMod.Core.dll'
Assert-Path $coreBinary 'HolmgangDuelMod.Core dependency'

New-Item -ItemType Directory -Force -Path $serverPlugins, $modPluginDirectory, $jotunnPluginDirectory | Out-Null
Copy-Item -LiteralPath $modBinary.FullName -Destination (Join-Path $modPluginDirectory 'HolmgangDuelMod.dll') -Force
Copy-Item -LiteralPath $coreBinary -Destination (Join-Path $modPluginDirectory 'HolmgangDuelMod.Core.dll') -Force

Write-Host "Deployed $($modBinary.FullName)" -ForegroundColor Green
Get-ChildItem -LiteralPath $jotunnPlugin -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $jotunnPluginDirectory $_.Name) -Force
}
Write-Host 'Deployed Jötunn 2.29.2.' -ForegroundColor Green

if ($InstallBepInExBootstrap) {
    Assert-Path $bepRoot 'BepInEx package root'
    Write-Host 'Copying BepInEx bootstrap files...' -ForegroundColor Cyan
    Get-ChildItem -LiteralPath $bepRoot -Force | ForEach-Object {
        if ($_.Name -notin @('BepInEx', 'valheim_server_Data', 'valheim_Data')) {
            Copy-Item -LiteralPath $_.FullName -Destination $ServerDirectory -Recurse -Force
        }
    }
    $packageBepInEx = Join-Path $bepRoot 'BepInEx'
    if (Test-Path -LiteralPath $packageBepInEx) {
        New-Item -ItemType Directory -Force -Path $serverBepInEx | Out-Null
        Copy-Item -LiteralPath (Join-Path $packageBepInEx '*') -Destination $serverBepInEx -Recurse -Force
    }
}

Write-Host 'Starting Valheim dedicated server...' -ForegroundColor Cyan
$serverWorkingDirectory = Split-Path -Parent $ServerExecutable
Start-Process -FilePath $ServerExecutable -ArgumentList $ServerArguments -WorkingDirectory $serverWorkingDirectory
Write-Host 'Valheim server process started.' -ForegroundColor Green
