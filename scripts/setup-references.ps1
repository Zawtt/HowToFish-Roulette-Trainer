param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $GameExecutable
)

$ErrorActionPreference = 'Stop'
$executable = Get-Item -LiteralPath $GameExecutable
if ($executable.Extension -ne '.exe') {
    throw 'GameExecutable must point to the game executable.'
}

$gameRoot = $executable.Directory.FullName
$dataName = [IO.Path]::GetFileNameWithoutExtension($executable.Name) + '_Data'
$managedCandidates = @(
    (Join-Path $gameRoot "$dataName\Managed"),
    (Join-Path $gameRoot 'How to Fish_Data\Managed')
)
$managedCandidates += Get-ChildItem -LiteralPath $gameRoot -Directory -Filter '*_Data' |
    ForEach-Object { Join-Path $_.FullName 'Managed' }

$managed = $managedCandidates |
    Select-Object -Unique |
    Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Assembly-CSharp.dll') } |
    Select-Object -First 1

if (-not $managed) {
    throw 'A Unity Managed directory was not found next to the selected executable.'
}

$destination = Join-Path $PSScriptRoot '..\lib'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$references = @(
    'UnityEngine.CoreModule.dll',
    'UnityEngine.PhysicsModule.dll',
    'UnityEngine.ScreenCaptureModule.dll'
)

foreach ($reference in $references) {
    $source = Join-Path $managed $reference
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required reference was not found: $reference"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $destination $reference) -Force
}

Write-Host "Local references prepared from: $managed"
