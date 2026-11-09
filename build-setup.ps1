$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'out'
$stage = Join-Path $out 'installer\app'
$runtime = Join-Path $out 'MW3Mod.dll'

# The runtime build refreshes the manager's bundled copy, so the manager must not be running.
Stop-Process -Name MW3ModManager -Force -ErrorAction SilentlyContinue

& (Join-Path $root 'build.ps1')
if (-not (Test-Path $runtime)) { throw 'Runtime build did not produce out\MW3Mod.dll' }

$version = $null
$ascii = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($runtime))
if ($ascii -match 'MW3Mod (\d+\.\d+\.\d+)') { $version = $Matches[1] }
if (-not $version) { throw 'Could not read the runtime version from out\MW3Mod.dll' }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

& dotnet publish (Join-Path $root 'src\manager') -c Release -r win-x64 --self-contained true -o $stage -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Manager publish failed' }

Copy-Item $runtime (Join-Path $stage 'MW3Mod.dll') -Force
Get-ChildItem $stage -Filter '*.pdb' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php.' }

& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\MW3Mod.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed' }

Write-Host "built $out\MW3ModSetup.exe"
