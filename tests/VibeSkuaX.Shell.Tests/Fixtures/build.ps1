[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MxmlcPath,
    [string]$GameScriptsPath
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $GameScriptsPath) { $GameScriptsPath = Join-Path $repository 'references\decompiled game\Game4000r5\scripts' }
$category = Join-Path $GameScriptsPath 'InvCat.as'
if (-not (Test-Path -LiteralPath $category)) { throw 'The Game4000r5 InvCat.as reference is required to rebuild this fixture.' }
# Compile the original category code without importing the rest of the decompilation's global classes.
$staging = Join-Path $repository 'artifacts\foundation-fixture-source'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -LiteralPath $category -Destination (Join-Path $staging 'InvCat.as') -Force
$env:FLEX_HOME = Split-Path -Parent (Split-Path -Parent (Resolve-Path -LiteralPath $MxmlcPath).Path)
$env:PLAYERGLOBAL_HOME = Join-Path $env:FLEX_HOME 'frameworks\libs\player'
$source = Join-Path $repository 'Skua.AS3\skua\src'
$output = Join-Path $PSScriptRoot 'InventoryHarness.swf'
& $MxmlcPath -source-path $source "-source-path+=$staging" -target-player 28.0 -optimize -output $output (Join-Path $PSScriptRoot 'InventoryHarness.as')
if ($LASTEXITCODE -ne 0) { throw 'Inventory fixture compilation failed.' }
function Get-SourceHash([string]$path) {
    $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n").Replace("`r", "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-', '') }
    finally { $sha.Dispose() }
}
[ordered]@{
    GameSnapshot = 'Game4000r5'
    SourceHashFormat = 'UTF8 text with LF line endings'
    InvCatSha256 = Get-SourceHash $category
    InventorySha256 = Get-SourceHash (Join-Path $source 'skua\api\Inventory.as')
    HarnessSha256 = Get-SourceHash (Join-Path $PSScriptRoot 'InventoryHarness.as')
    SwfSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'InventoryHarness.manifest.json') -Encoding UTF8
