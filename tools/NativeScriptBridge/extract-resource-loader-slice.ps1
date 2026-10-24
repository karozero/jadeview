$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\resource-loader-probe'
$outputSource = Join-Path $outputDirectory 'generated_source_slice.c'
$outputManifest = Join-Path $outputDirectory 'generated_source_slice.manifest.json'
$sourceDefinitions = @(
    @{ Path = 'external/irisEng/ai_common.c'; Names = @('AI_ul_CallbackLoadInstance', 'AI_ul_CallbackLoadModel', 'AI_ul_CallbackLoadVars') },
    @{ Path = 'external/irisEng/loading.c'; Names = @('LOA_ReadLong', 'LOA_ReadShort', 'LOA_ReadFloat', 'LOA_ReadString', 'LOA_ReadCharArray', 'LOA_ReadVector', 'LOA_MakeFileRef') }
)

Import-Module (Join-Path $PSScriptRoot 'ExactSourceExtraction.psm1') -Force

function Assert-ExtractorLexingChecks {
    $sample = "void Sample(void) { const char *text = `"}`"; /* } */ // }`r`nchar quote = '}'; }"
    $encoding = [System.Text.Encoding]::GetEncoding(1252)
    $sampleBytes = $encoding.GetBytes($sample)
    $sampleFunction = Get-FunctionBody -SourceText $sample -SourceBytes $sampleBytes -SourceEncoding $encoding -FunctionName 'Sample' -SourcePath 'synthetic.c'
    if ((Get-BytesSha256 -Bytes $sampleFunction.Bytes) -ne (Get-BytesSha256 -Bytes $sampleBytes)) {
        throw 'Extractor lexical regression: braces in comments or literals changed the exact function byte span.'
    }

    $duplicateRejected = $false
    try {
        $duplicateSource = $sample + "`r`n" + $sample
        [void](Get-FunctionBody -SourceText $duplicateSource -SourceBytes ($encoding.GetBytes($duplicateSource)) -SourceEncoding $encoding -FunctionName 'Sample' -SourcePath 'synthetic-duplicate.c')
    }
    catch { $duplicateRejected = $true }
    if (-not $duplicateRejected) { throw 'Extractor lexical regression: duplicate definitions were not rejected.' }

    $missingRejected = $false
    try {
        [void](Get-FunctionBody -SourceText 'void Other(void) { }' -SourceBytes ($encoding.GetBytes('void Other(void) { }')) -SourceEncoding $encoding -FunctionName 'Sample' -SourcePath 'synthetic-missing.c')
    }
    catch { $missingRejected = $true }
    if (-not $missingRejected) { throw 'Extractor lexical regression: missing definition was not rejected.' }
}

Assert-ExtractorLexingChecks

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$extractedFunctions = [System.Collections.Generic.List[object]]::new()
$sourceRecords = [System.Collections.Generic.List[object]]::new()
foreach ($sourceDefinition in $sourceDefinitions) {
    $sourceFile = Join-Path $repoRoot $sourceDefinition.Path
    $document = Read-ExactSourceFile -Path $sourceFile
    $sourceBytes = $document.Bytes
    $sourceText = $document.Text
    $sourceEncoding = $document.Encoding
    $sourceEncodingName = $document.EncodingName
    $sourceHash = $document.Sha256
    $lineEnding = $document.LineEnding
    $hasBom = $document.HasBom
    $sourceRecords.Add([pscustomobject]@{
        Path = $sourceDefinition.Path
        Sha256 = $sourceHash
        Encoding = $sourceEncodingName
        HasBom = $hasBom
        LineEnding = $lineEnding
    })
    foreach ($functionName in $sourceDefinition.Names) {
        $extractedFunctions.Add((Get-FunctionBody -SourceText $sourceText -SourceBytes $sourceBytes -SourceEncoding $sourceEncoding -FunctionName $functionName -SourcePath $sourceDefinition.Path))
    }
}

$generatedStream = [System.IO.MemoryStream]::new()
function Write-GeneratedAscii {
    param([System.IO.Stream] $Stream, [string] $Text)

    $lineBytes = [System.Text.Encoding]::ASCII.GetBytes($Text)
    $Stream.Write($lineBytes, 0, $lineBytes.Length)
}

Write-GeneratedAscii -Stream $generatedStream -Text "/* Generated from exact Iris byte spans; see generated_source_slice.manifest.json. */`r`n"
Write-GeneratedAscii -Stream $generatedStream -Text "`r`n"
foreach ($function in $extractedFunctions) {
    Write-GeneratedAscii -Stream $generatedStream -Text "/* $($function.Source):$($function.StartLine)-$($function.EndLine); body SHA-256 $($function.BodySha256) */`r`n"
    $function | Add-Member -NotePropertyName GeneratedByteOffset -NotePropertyValue ([int]$generatedStream.Position)
    $function | Add-Member -NotePropertyName GeneratedByteCount -NotePropertyValue $function.Bytes.Length
    $generatedStream.Write($function.Bytes, 0, $function.Bytes.Length)
    $generatedBodyBytes = [byte[]]::new($function.GeneratedByteCount)
    [Array]::Copy($generatedStream.ToArray(), $function.GeneratedByteOffset, $generatedBodyBytes, 0, $function.GeneratedByteCount)
    if ((Get-BytesSha256 -Bytes $generatedBodyBytes) -ne $function.BodySha256) {
        throw "Generated function bytes changed during extraction: $($function.Name)."
    }
    Write-GeneratedAscii -Stream $generatedStream -Text "`r`n`r`n"
}
$generatedBytes = $generatedStream.ToArray()
$generatedStream.Dispose()
[System.IO.File]::WriteAllBytes($outputSource, $generatedBytes)

$manifest = [pscustomobject]@{
    SchemaVersion = 1
    Purpose = 'Exact-source native AIC/AIM/OVA reference queue probe; no OFC execution or BF-world resolution.'
    GeneratedSource = $outputSource.Substring($repoRoot.Length + 1)
    GeneratedSourceSha256 = Get-BytesSha256 -Bytes $generatedBytes
    GeneratedSourceEncoding = 'Original source bytes for extracted bodies; ASCII wrapper text'
    Sources = @($sourceRecords)
    ExtractedFunctions = @($extractedFunctions | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            Source = $_.Source
            StartLine = $_.StartLine
            EndLine = $_.EndLine
            ByteOffset = $_.ByteOffset
            ByteCount = $_.ByteCount
            GeneratedByteOffset = $_.GeneratedByteOffset
            GeneratedByteCount = $_.GeneratedByteCount
            BodySha256 = $_.BodySha256
        }
    })
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputManifest -Encoding UTF8
Write-Output "Generated exact-source slice: $outputSource"
Write-Output "Source-span manifest: $outputManifest"
