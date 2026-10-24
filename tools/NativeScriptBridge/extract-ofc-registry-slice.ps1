param(
    [string] $ManifestPath = 'artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/manifest.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestFullPath = (Resolve-Path (Join-Path $repoRoot $ManifestPath)).Path
$manifestDirectory = [System.IO.Path]::GetDirectoryName($manifestFullPath)
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\ofc-registry-probe'

Import-Module (Join-Path $PSScriptRoot 'ExactSourceExtraction.psm1') -Force

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$fixture = $manifest.Fixture
$resourceMap = @{}
foreach ($resource in $fixture.Resources) { $resourceMap[$resource.File] = $resource }
$functionLink = @($fixture.Links | Where-Object { $_.Role -eq 'model-entry:.ofc' })
if ($functionLink.Count -ne 1 -or $functionLink[0].Status -ne 'Resolved') {
    throw 'Expected one resolved model OFC link in the selected manifest.'
}
$functionResource = $resourceMap[$functionLink[0].File]
if (-not $functionResource -or $functionResource.Status -ne 'Resolved' -or $functionResource.Kind -ne 'r') {
    throw 'The selected model OFC link does not identify a resolved resource of kind r.'
}
$resourcePath = [System.IO.Path]::GetFullPath((Join-Path $manifestDirectory $functionResource.File))
$manifestPrefix = $manifestDirectory.TrimEnd('\') + '\'
if (-not $resourcePath.StartsWith($manifestPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The selected OFC resource escapes its fixture directory.'
}
$resourceBytes = [System.IO.File]::ReadAllBytes($resourcePath)
if ($resourceBytes.Length -ne [int]$functionResource.Size -or
    (Get-FileHash -LiteralPath $resourcePath -Algorithm SHA256).Hash -ne $functionResource.Sha256) {
    throw 'The selected OFC resource does not match the manifest size and SHA-256.'
}

$key = [Convert]::ToUInt32([string]$functionLink[0].Key, 16)
$keyHex = $key.ToString('X8')
$registryPath = Join-Path $repoRoot 'external\irisEng\ai\ai_compiledscripts.c'
$registryDocument = Read-ExactSourceFile -Path $registryPath
$registryBytes = $registryDocument.Bytes
$registryText = $registryDocument.Text
$registryEncoding = $registryDocument.Encoding
$registryEncodingName = $registryDocument.EncodingName
$registryHash = $registryDocument.Sha256
$rowPattern = '(?m)^[\t ]*\{[\t ]*0x' + $keyHex + '[\t ]*,[\t ]*(?<symbol>[A-Za-z_][A-Za-z0-9_]*)[\t ]*\}[\t ]*,[\t ]*\r?$'
$registryRows = [regex]::Matches($registryText, $rowPattern)
if ($registryRows.Count -ne 1) {
    throw "The selected key $keyHex does not have exactly one source registry row; refusing an uncompiled fallback."
}
$registryRow = $registryRows[0]
$symbol = $registryRow.Groups['symbol'].Value
$rowLine = ([regex]::Matches($registryText.Substring(0, $registryRow.Index), "`n")).Count + 1
$rowBytes = [System.Text.Encoding]::UTF8.GetBytes($registryRow.Value)
$rowHash = Get-BytesSha256 -Bytes $rowBytes

$callbackDefinitions = [System.Collections.Generic.List[object]]::new()
foreach ($sourceFile in Get-ChildItem (Join-Path $repoRoot 'external\irisEng\ai') -Filter '*.c' -Recurse) {
    $document = Read-ExactSourceFile -Path $sourceFile.FullName
    $sourceBytes = $document.Bytes
    $sourceText = $document.Text
    $sourceEncoding = $document.Encoding
    $definitionPattern = '(?m)^[\t ]*[A-Za-z_][A-Za-z0-9_\t \*]*\b' + [regex]::Escape($symbol) + '\s*\('
    if (-not [regex]::IsMatch($sourceText, $definitionPattern)) { continue }
    $relativeSource = $sourceFile.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $callbackDefinitions.Add((Get-FunctionBody -SourceText $sourceText -SourceBytes $sourceBytes -SourceEncoding $sourceEncoding -FunctionName $symbol -SourcePath $relativeSource))
}
if ($callbackDefinitions.Count -ne 1) {
    throw "Registry symbol '$symbol' must have exactly one source definition in the Iris AI tree; found $($callbackDefinitions.Count)."
}
$callbackDefinition = $callbackDefinitions[0]
$callbackSourcePath = Join-Path $repoRoot ($callbackDefinition.Source.Replace('/', '\'))
$callbackSourceHash = (Get-FileHash -LiteralPath $callbackSourcePath -Algorithm SHA256).Hash

$sourceDefinitions = @(
    @{ Path = 'external/irisEng/ai_common.c'; Names = @('AI_ul_CallbackLoadFunction') },
    @{ Path = 'external/irisEng/loading.c'; Names = @(
        'LOA_ReadLong', 'LOA_ReadShort', 'LOA_ReadChar', 'LOA_ReadCharArray',
        'LOA_ReadString', 'LOA_ul_GetCurrentKey', 'LOA_MakeFileRef'
    ) }
)
$loaderFunctions = [System.Collections.Generic.List[object]]::new()
$sourceRecords = [System.Collections.Generic.List[object]]::new()
foreach ($sourceDefinition in $sourceDefinitions) {
    $sourcePath = Join-Path $repoRoot $sourceDefinition.Path
    $document = Read-ExactSourceFile -Path $sourcePath
    $sourceBytes = $document.Bytes
    $sourceText = $document.Text
    $sourceEncoding = $document.Encoding
    $encodingName = $document.EncodingName
    $sourceRecords.Add([pscustomobject]@{
        Path = $sourceDefinition.Path
        Sha256 = Get-BytesSha256 -Bytes $sourceBytes
        Encoding = $encodingName
    })
    foreach ($functionName in $sourceDefinition.Names) {
        $loaderFunctions.Add((Get-FunctionBody -SourceText $sourceText -SourceBytes $sourceBytes -SourceEncoding $sourceEncoding -FunctionName $functionName -SourcePath $sourceDefinition.Path))
    }
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$loaderPath = Join-Path $outputDirectory 'generated_ofc_loader.c'
$loaderStream = [System.IO.MemoryStream]::new()
$ascii = [System.Text.Encoding]::ASCII
foreach ($function in $loaderFunctions) {
    $comment = "/* $($function.Source):$($function.StartLine)-$($function.EndLine); body SHA-256 $($function.BodySha256) */`r`n"
    $commentBytes = $ascii.GetBytes($comment)
    $loaderStream.Write($commentBytes, 0, $commentBytes.Length)
    $loaderStream.Write($function.Bytes, 0, $function.Bytes.Length)
    $separator = $ascii.GetBytes("`r`n`r`n")
    $loaderStream.Write($separator, 0, $separator.Length)
}
$loaderOutputBytes = $loaderStream.ToArray()
$loaderStream.Dispose()
[System.IO.File]::WriteAllBytes($loaderPath, $loaderOutputBytes)

$registryOutputPath = Join-Path $outputDirectory 'generated_ofc_registry_row.c'
$registryC = @"
/* One active registry row selected from ai_compiledscripts.c by manifest OFC key.
 * The declared extent preserves the source ABI; SizeofArray exposes one active row. */
#include "../../../external/irisEng/ai/ai_compiledscripts.h"
extern int $symbol();
tStruct_funclist AI2C_gat_fctdefs[2530] = { { (int)0x$keyHex, $symbol } };
unsigned int AI2C_i_SizeofArray(void) { return 1; }
int (*OFCRegistryProbeTarget(void))() { return $symbol; }
const char* OFCRegistryProbeSymbolName(void) { return "$symbol"; }
"@
[System.IO.File]::WriteAllText($registryOutputPath, $registryC, [System.Text.Encoding]::ASCII)
$registryOutputBytes = [System.IO.File]::ReadAllBytes($registryOutputPath)

$manifestOutput = [pscustomobject]@{
    SchemaVersion = 1
    Purpose = 'Source-sliced OFC metadata loader and one-row callback registry identity probe; no callback execution.'
    Fixture = [pscustomobject]@{
        Container = $manifest.Container
        OwnerKey = $fixture.OwnerKey
        WorldKey = $functionLink[0].PreferredWorld
        FunctionKey = $keyHex
        ResourceFile = $functionResource.File
        ResourceSize = $resourceBytes.Length
        ResourceSha256 = $functionResource.Sha256
        ResourceOffset = $functionResource.Offset
    }
    Registry = [pscustomobject]@{
        Source = 'external/irisEng/ai/ai_compiledscripts.c'
        SourceSha256 = $registryHash
        SourceEncoding = $registryEncodingName
        SourceLine = $rowLine
        ExactRow = $registryRow.Value
        RowSha256 = $rowHash
        Symbol = $symbol
        FullRegistryRowCount = 2530
        ProbeRegistryRowCount = 1
        ScopeNote = 'The probe verifies this source row and runs Iris lookup over the one-row extracted registry slice; it does not link or claim the full registry.'
    }
    CallbackBody = [pscustomobject]@{
        Source = $callbackDefinition.Source
        SourceSha256 = $callbackSourceHash
        StartLine = $callbackDefinition.StartLine
        EndLine = $callbackDefinition.EndLine
        BodySha256 = $callbackDefinition.BodySha256
        Execution = 'Not executed; pointer identity only.'
    }
    LoaderSources = @($sourceRecords)
    ExtractedFunctions = @($loaderFunctions | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            Source = $_.Source
            StartLine = $_.StartLine
            EndLine = $_.EndLine
            ByteOffset = $_.ByteOffset
            ByteCount = $_.ByteCount
            BodySha256 = $_.BodySha256
        }
    })
    Generated = [pscustomobject]@{
        LoaderPath = $loaderPath.Substring($repoRoot.Length + 1)
        LoaderSha256 = Get-BytesSha256 -Bytes $loaderOutputBytes
        RegistryRowPath = $registryOutputPath.Substring($repoRoot.Length + 1)
        RegistryRowSha256 = Get-BytesSha256 -Bytes $registryOutputBytes
    }
}
$manifestOutputPath = Join-Path $outputDirectory 'ofc_registry_probe_manifest.json'
$manifestOutput | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestOutputPath -Encoding UTF8
Write-Output "Generated OFC loader slice: $loaderPath"
Write-Output "Generated one-row registry slice: $registryOutputPath"
Write-Output "Source-span manifest: $manifestOutputPath"
