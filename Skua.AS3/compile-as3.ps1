[CmdletBinding()]
param(
    [string]$MxmlcPath
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Join-Path $scriptRoot 'skua'
$sourcePath = Join-Path $projectRoot 'src'
$mainSourcePath = Join-Path $sourcePath 'skua\Main.as'
$outputDirectory = Join-Path $projectRoot 'bin'
$outputPath = Join-Path $outputDirectory 'skua.swf'

if (-not (Test-Path -LiteralPath $mainSourcePath)) {
    throw "AS3 entry point was not found: $mainSourcePath"
}

if ([string]::IsNullOrWhiteSpace($MxmlcPath)) {
    $mxmlcCommand = Get-Command mxmlc -ErrorAction SilentlyContinue
    if ($mxmlcCommand) {
        $MxmlcPath = $mxmlcCommand.Source
    }
    elseif ($env:FLEX_HOME) {
        $MxmlcPath = Join-Path $env:FLEX_HOME 'bin\mxmlc.bat'
    }
}

if ([string]::IsNullOrWhiteSpace($MxmlcPath) -or -not (Test-Path -LiteralPath $MxmlcPath)) {
    throw 'Apache Flex mxmlc was not found. Install Apache Flex SDK 4.16.1 or pass -MxmlcPath.'
}

if (-not $env:FLEX_HOME) {
    $mxmlcDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $MxmlcPath).Path
    $env:FLEX_HOME = Split-Path -Parent $mxmlcDirectory
}
if (-not $env:PLAYERGLOBAL_HOME) {
    $env:PLAYERGLOBAL_HOME = Join-Path $env:FLEX_HOME 'frameworks\libs\player'
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $MxmlcPath `
    -source-path $sourcePath `
    -default-size 958 550 `
    -output $outputPath `
    $mainSourcePath `
    -target-player 28.0 `
    -optimize

if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputPath)) {
    throw "AS3 compilation failed: $outputPath"
}

$output = Get-Item -LiteralPath $outputPath
function Get-SourceHash([string]$path) {
    $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n").Replace("`r", "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-', '') }
    finally { $sha.Dispose() }
}
$sourceHashes = [ordered]@{}
Get-ChildItem -LiteralPath $sourcePath -Filter '*.as' -Recurse | Sort-Object FullName | ForEach-Object {
    $relativePath = $_.FullName.Substring($sourcePath.Length + 1).Replace('\', '/')
    $sourceHashes[$relativePath] = Get-SourceHash $_.FullName
}
[ordered]@{
    TargetPlayer = '28.0'
    SourceHashFormat = 'UTF8 text with LF line endings'
    SwfSha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
    Sources = $sourceHashes
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $scriptRoot 'skua.swf.manifest.json') -Encoding UTF8
Write-Host "AS3 game content built: $($output.FullName) ($($output.Length) bytes)" -ForegroundColor Green
exit 0
