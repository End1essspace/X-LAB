$ErrorActionPreference = 'Stop'

$repo = 'D:\projects\GitHub\X-LAB'
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not (Test-Path (Join-Path $repo '.git'))) {
    throw "Git repository not found: $repo"
}

$legacyDirectory = Join-Path $repo 'legacy'
New-Item -ItemType Directory -Path $legacyDirectory -Force | Out-Null

$oldBat = Join-Path $repo 'screen_hotkey_listener.bat'
if (Test-Path $oldBat) {
    Move-Item $oldBat (Join-Path $legacyDirectory 'screen_hotkey_listener.bat') -Force
}
elseif (-not (Test-Path (Join-Path $legacyDirectory 'screen_hotkey_listener.bat'))) {
    Copy-Item (Join-Path $packageRoot 'legacy\screen_hotkey_listener.bat') $legacyDirectory -Force
}

Copy-Item (Join-Path $packageRoot 'src') $repo -Recurse -Force
Copy-Item (Join-Path $packageRoot 'README.md') (Join-Path $repo 'README.md') -Force

Write-Host 'Step 01 files applied.'
Write-Host 'Next: build and run the C# project before committing.'
