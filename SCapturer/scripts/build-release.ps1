[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',

    [Parameter()]
    [switch]$SkipTests,

    [Parameter()]
    [switch]$SkipReliability,

    [Parameter()]
    [switch]$SkipMsi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repositoryRoot 'SCapturer.sln'
$appProject = Join-Path $repositoryRoot 'src\SCapturer.App\SCapturer.App.csproj'
$testProject = Join-Path $repositoryRoot 'tests\SCapturer.Tests\SCapturer.Tests.csproj'
$reliabilityProject = Join-Path $repositoryRoot 'tools\SCapturer.Reliability\SCapturer.Reliability.csproj'
$publishProfile = 'win-x64'

$workRoot = Join-Path $repositoryRoot "dist\work\$Version"
$publishDirectory = Join-Path $workRoot 'publish\win-x64'
$portableStage = Join-Path $workRoot 'portable'
$wixObjectDirectory = Join-Path $workRoot 'wix'
$releaseDirectory = Join-Path $repositoryRoot "dist\release\$Version"

$portableArchive = Join-Path $releaseDirectory "SCapturer-v$Version-win-x64-portable.zip"
$msiPath = Join-Path $releaseDirectory "SCapturer-v$Version-win-x64.msi"
$hashFile = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$releaseNotesPath = Join-Path $releaseDirectory 'RELEASE_NOTES.md'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host "`n> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkCyan
    & $FilePath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath"
    }
}

function Get-WixTool {
    param(
        [Parameter(Mandatory)]
        [string]$ExecutableName
    )

    $fromPath = Get-Command $ExecutableName -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $candidateRoots = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:WIX)) {
        $candidateRoots.Add($env:WIX)
        $candidateRoots.Add((Join-Path $env:WIX 'bin'))
    }

    $candidateRoots.Add('C:\Program Files (x86)\WiX Toolset v3.14\bin')
    $candidateRoots.Add('C:\Program Files\WiX Toolset v3.14\bin')
    $candidateRoots.Add('C:\Program Files (x86)\WiX Toolset v3.11\bin')
    $candidateRoots.Add('C:\Program Files\WiX Toolset v3.11\bin')

    foreach ($root in $candidateRoots | Select-Object -Unique) {
        $candidate = Join-Path $root $ExecutableName
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "$ExecutableName was not found. Install WiX Toolset 3.14 or add its bin directory to PATH."
}

function New-CleanDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Assert-SingleFilePublish {
    param(
        [Parameter(Mandatory)]
        [string]$Directory
    )

    $symbolFiles = @(
        Get-ChildItem -LiteralPath $Directory -File -Filter '*.pdb'
    )

    if ($symbolFiles.Count -gt 0) {
        $symbolNames = $symbolFiles.Name -join ', '
        Write-Host "Removing optional publish symbols: $symbolNames" -ForegroundColor DarkGray
        $symbolFiles | Remove-Item -Force
    }

    $files = @(Get-ChildItem -LiteralPath $Directory -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'SCapturer.exe') {
        $actual = if ($files.Count -eq 0) {
            '<empty>'
        }
        else {
            ($files.Name -join ', ')
        }

        throw "Expected one deployable file named SCapturer.exe after symbol cleanup, found: $actual"
    }

    if ($files[0].Length -le 0) {
        throw 'Published SCapturer.exe is empty.'
    }
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$assemblyVersion = "$Version.0"
$buildProperties = @(
    "-p:Version=$Version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    "-p:InformationalVersion=$Version"
)

Write-Host "SCapturer release build v$Version" -ForegroundColor Cyan
Write-Host "Repository : $repositoryRoot"
Write-Host "Release    : $releaseDirectory"

New-CleanDirectory $workRoot
New-CleanDirectory $releaseDirectory
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $wixObjectDirectory -Force | Out-Null

Invoke-Checked $dotnet (@('clean', $solutionPath, '-c', 'Release') + $buildProperties)
Invoke-Checked $dotnet @('restore', $solutionPath)
Invoke-Checked $dotnet (@('build', $solutionPath, '-c', 'Release', '--no-restore') + $buildProperties)

if (-not $SkipTests) {
    Invoke-Checked $dotnet @(
        'run',
        '--project', $testProject,
        '-c', 'Release',
        '--no-build'
    )
}

if (-not $SkipReliability) {
    Invoke-Checked $dotnet @(
        'run',
        '--project', $reliabilityProject,
        '-c', 'Release',
        '--no-build',
        '--',
        '--captures', '100',
        '--console-cycles', '30',
        '--region-cancel-cycles', '5',
        '--process-cycles', '10'
    )
}

Invoke-Checked $dotnet (@(
    'publish',
    $appProject,
    "-p:PublishProfile=$publishProfile",
    '-o', $publishDirectory
) + $buildProperties)

Assert-SingleFilePublish $publishDirectory
$publishedExe = Join-Path $publishDirectory 'SCapturer.exe'
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($publishedExe)
if ($versionInfo.FileVersion -ne $assemblyVersion) {
    throw "Published file version is '$($versionInfo.FileVersion)', expected '$assemblyVersion'."
}

New-CleanDirectory $portableStage
Copy-Item -LiteralPath $publishedExe -Destination $portableStage

$portableReadmeTemplate = Join-Path $repositoryRoot 'packaging\portable\README.txt'
$portableReadme = (Get-Content -LiteralPath $portableReadmeTemplate -Raw)
$portableReadme = $portableReadme.Replace('{{VERSION}}', $Version)
Set-Content -LiteralPath (Join-Path $portableStage 'README.txt') -Value $portableReadme -Encoding utf8

if (Test-Path -LiteralPath $portableArchive) {
    Remove-Item -LiteralPath $portableArchive -Force
}

Compress-Archive -Path (Join-Path $portableStage '*') -DestinationPath $portableArchive -CompressionLevel Optimal

$releaseArtifacts = [System.Collections.Generic.List[string]]::new()
$releaseArtifacts.Add($portableArchive)

if (-not $SkipMsi) {
    $candle = Get-WixTool 'candle.exe'
    $light = Get-WixTool 'light.exe'
    $wixSource = Join-Path $repositoryRoot 'packaging\windows\SCapturer.wxs'
    $wixObject = Join-Path $wixObjectDirectory 'SCapturer.wixobj'

    Invoke-Checked $candle @(
        '-nologo',
        '-arch', 'x64',
        "-dProductVersion=$Version",
        "-dSourceDir=$publishDirectory",
        '-out', $wixObject,
        $wixSource
    )

    Invoke-Checked $light @(
        '-nologo',
        '-spdb',
        '-sice:ICE91',
        '-out', $msiPath,
        $wixObject
    )

    $msiExists = Test-Path -LiteralPath $msiPath -PathType Leaf
    if (-not $msiExists) {
        throw 'WiX did not produce an MSI package.'
    }

    if ((Get-Item -LiteralPath $msiPath).Length -le 0) {
        throw 'WiX produced an empty MSI package.'
    }

    $releaseArtifacts.Add($msiPath)
}

$releaseNotesTemplate = Join-Path $repositoryRoot 'packaging\RELEASE_NOTES.template.md'
$releaseNotes = (Get-Content -LiteralPath $releaseNotesTemplate -Raw)
$releaseNotes = $releaseNotes.Replace('{{VERSION}}', $Version)
$releaseNotes = $releaseNotes.Replace('{{DATE}}', (Get-Date -Format 'yyyy-MM-dd'))
Set-Content -LiteralPath $releaseNotesPath -Value $releaseNotes -Encoding utf8

$hashLines = foreach ($artifact in $releaseArtifacts) {
    $hash = Get-FileHash -LiteralPath $artifact -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant()) *$([System.IO.Path]::GetFileName($artifact))"
}
Set-Content -LiteralPath $hashFile -Value $hashLines -Encoding ascii

Remove-Item -LiteralPath $workRoot -Recurse -Force

Write-Host "`nRelease artifacts:" -ForegroundColor Green
Get-ChildItem -LiteralPath $releaseDirectory -File |
    Sort-Object Name |
    ForEach-Object {
        Write-Host ("  {0,-52} {1,10:N0} bytes" -f $_.Name, $_.Length)
    }

Write-Host "`nSCapturer v$Version packaging completed." -ForegroundColor Green
