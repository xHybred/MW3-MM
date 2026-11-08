param(
    [switch]$Manager
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root 'build'
$out = Join-Path $root 'out'

$cmake = & (Join-Path $root 'tools\find-cmake.ps1')
if (-not $cmake) { throw 'CMake not found. Install Visual Studio Build Tools with the C++ workload.' }

New-Item -ItemType Directory -Force -Path $out | Out-Null

& $cmake -S (Join-Path $root 'src\runtime') -B (Join-Path $build 'runtime') -A x64
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed' }

& $cmake --build (Join-Path $build 'runtime') --config Release
if ($LASTEXITCODE -ne 0) { throw 'Runtime build failed' }

Copy-Item (Join-Path $build 'runtime\Release\MW3Mod.dll') (Join-Path $out 'MW3Mod.dll') -Force
Write-Host "built $out\MW3Mod.dll"

# The manager installs from its own copy of the DLL, so a build without -Manager
# used to leave that copy at an older version and the app would silently put the
# old runtime back. Keep it in step whenever it already exists.
$managerDll = Join-Path $out 'manager\MW3Mod.dll'
if (-not $Manager -and (Test-Path $managerDll)) {
    Copy-Item (Join-Path $out 'MW3Mod.dll') $managerDll -Force
    Write-Host "updated $managerDll"
}

if ($Manager) {
    & dotnet publish (Join-Path $root 'src\manager') -c Release -o (Join-Path $out 'manager')
    if ($LASTEXITCODE -ne 0) { throw 'Manager build failed' }
    Copy-Item (Join-Path $out 'MW3Mod.dll') (Join-Path $out 'manager\MW3Mod.dll') -Force
    Write-Host "built $out\manager"
}
