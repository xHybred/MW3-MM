param()

$candidates = @(
    "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
)

foreach ($candidate in $candidates) {
    if (Test-Path $candidate) { Write-Output $candidate; exit 0 }
}

$command = Get-Command cmake.exe -ErrorAction SilentlyContinue
if ($command) { Write-Output $command.Source; exit 0 }

exit 1
