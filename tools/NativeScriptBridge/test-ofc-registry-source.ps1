$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Import-Module (Join-Path $PSScriptRoot 'ExactSourceExtraction.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'OfcRegistrySource.psm1') -Force
$directory = Join-Path $repoRoot 'artifacts\native-script-bridge\registry-source-regression'
New-Item -ItemType Directory -Path $directory -Force | Out-Null

function Read-SyntheticSource {
    param([string] $Name, [string] $Text, [System.Text.Encoding] $Encoding = [Text.UTF8Encoding]::new($false))
    $path = Join-Path $directory $Name
    [IO.File]::WriteAllBytes($path, $Encoding.GetBytes($Text))
    Read-ExactSourceFile -Path $path
}

function Assert-ContractRejected {
    param($Definition, $Declaration, [string] $Reason)
    $rejected = $false
    try { [void](Get-OfcRegistryContract $Definition $Declaration 'fixture.c' 'fixture.h') }
    catch { $rejected = $true }
    if (-not $rejected) { throw $Reason }
}

$definitionText = "//tStruct_funclist AI2C_gat_fctdefs[1518] = {`n" +
    "/* tStruct_funclist AI2C_gat_fctdefs[999] = { */`n" +
    "tStruct_funclist AI2C_gat_fctdefs[17] = {`n    { 0x00000001, Callback },`n};"
$declarationText = "//extern tStruct_funclist AI2C_gat_fctdefs[136];`nextern tStruct_funclist AI2C_gat_fctdefs[17];"
$definition = Read-SyntheticSource 'definition.c' $definitionText
$declaration = Read-SyntheticSource 'declaration.h' $declarationText
$contract = Get-OfcRegistryContract $definition $declaration 'fixture.c' 'fixture.h'
if ($contract.Extent -ne 17 -or $contract.Definition.StartLine -ne 3 -or $contract.Declaration.StartLine -ne 2) {
    throw 'Changed source extent or commented historical declarations were misinterpreted.'
}
Assert-ContractRejected $definition (Read-SyntheticSource 'mismatch.h' ($declarationText.Replace('[17]', '[18]'))) 'Mismatched header extent was accepted.'
Assert-ContractRejected (Read-SyntheticSource 'duplicate.c' ($definitionText + "`n" + $definitionText)) $declaration 'Multiple active definitions were accepted.'
Assert-ContractRejected $definition (Read-SyntheticSource 'missing.h' '// extern tStruct_funclist AI2C_gat_fctdefs[17];') 'Comment-only declaration was accepted.'

$accent = [char]0xE9
$row = "    { 0x00000001, /* caf${accent} */ Callback },"
$prefix = "/* pr${accent}fix */`r`n"
$rowDocument = Read-SyntheticSource 'row-1252.c' ($prefix + $row + "`r`n") ([Text.Encoding]::GetEncoding(1252))
$selected = Get-OfcRegistryRow $rowDocument 1 'row-1252.c'
$expected = [Text.Encoding]::GetEncoding(1252).GetBytes($row + "`r")
if ($selected.Symbol -ne 'Callback' -or $selected.Span.ByteOffset -ne ([Text.Encoding]::GetEncoding(1252).GetByteCount($prefix)) -or
    $selected.Span.ByteCount -ne $expected.Length -or $selected.Span.Sha256 -ne (Get-BytesSha256 $expected)) {
    throw 'Registry row provenance used re-encoded UTF-8 instead of the original Windows-1252 span.'
}
Write-Output 'PASS: registry extent follows unique active source/header contracts; row spans retain original encoding.'
