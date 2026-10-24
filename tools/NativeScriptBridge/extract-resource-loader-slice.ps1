$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\resource-loader-probe'
$outputSource = Join-Path $outputDirectory 'generated_source_slice.c'
$outputManifest = Join-Path $outputDirectory 'generated_source_slice.manifest.json'
$sourceDefinitions = @(
    @{ Path = 'external/irisEng/ai_common.c'; Names = @('AI_ul_CallbackLoadInstance', 'AI_ul_CallbackLoadModel') },
    @{ Path = 'external/irisEng/loading.c'; Names = @('LOA_ReadLong', 'LOA_ReadCharArray', 'LOA_MakeFileRef') }
)

function Get-BytesSha256 {
    param([byte[]] $Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($Bytes)
        return [BitConverter]::ToString($hashBytes).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-FunctionBody {
    param([string] $SourceText, [byte[]] $SourceBytes, [System.Text.Encoding] $SourceEncoding, [string] $FunctionName, [string] $SourcePath)

    $signaturePattern = '(?m)^[\t ]*[A-Za-z_][A-Za-z0-9_\t \*]*\b' + [regex]::Escape($FunctionName) + '\s*\('
    $signatureMatches = [regex]::Matches($SourceText, $signaturePattern)
    $definitions = [System.Collections.Generic.List[object]]::new()
    foreach ($signatureMatch in $signatureMatches) {
        $openParenthesis = $SourceText.IndexOf('(', $signatureMatch.Index)
        $parenthesisDepth = 0
        $closeParenthesis = -1
        for ($index = $openParenthesis; $index -lt $SourceText.Length; $index++) {
            if ($SourceText[$index] -eq '(') { $parenthesisDepth++ }
            if ($SourceText[$index] -eq ')') {
                $parenthesisDepth--
                if ($parenthesisDepth -eq 0) {
                    $closeParenthesis = $index
                    break
                }
            }
        }
        if ($closeParenthesis -lt 0) { continue }

        $openBrace = -1
        for ($index = $closeParenthesis + 1; $index -lt $SourceText.Length; $index++) {
            if ([char]::IsWhiteSpace($SourceText[$index])) { continue }
            if ($SourceText[$index] -eq '/' -and $index + 1 -lt $SourceText.Length -and $SourceText[$index + 1] -eq '/') {
                $newline = $SourceText.IndexOf("`n", $index + 2)
                if ($newline -lt 0) { break }
                $index = $newline
                continue
            }
            if ($SourceText[$index] -eq '/' -and $index + 1 -lt $SourceText.Length -and $SourceText[$index + 1] -eq '*') {
                $commentEnd = $SourceText.IndexOf('*/', $index + 2, [System.StringComparison]::Ordinal)
                if ($commentEnd -lt 0) { break }
                $index = $commentEnd + 1
                continue
            }
            if ($SourceText[$index] -eq '{') { $openBrace = $index }
            break
        }
        if ($openBrace -lt 0) { continue }

        $braceDepth = 0
        $lexicalState = 'code'
        for ($index = $openBrace; $index -lt $SourceText.Length; $index++) {
            $current = $SourceText[$index]
            $next = if ($index + 1 -lt $SourceText.Length) { $SourceText[$index + 1] } else { [char]0 }

            if ($lexicalState -eq 'line-comment') {
                if ($current -eq "`n") { $lexicalState = 'code' }
                continue
            }
            if ($lexicalState -eq 'block-comment') {
                if ($current -eq '*' -and $next -eq '/') { $lexicalState = 'code'; $index++ }
                continue
            }
            if ($lexicalState -eq 'string' -or $lexicalState -eq 'character') {
                if ($current -eq '\') { $index++; continue }
                if ($lexicalState -eq 'string' -and $current -eq '"') { $lexicalState = 'code'; continue }
                if ($lexicalState -eq 'character' -and $current -eq "'") { $lexicalState = 'code'; continue }
                continue
            }

            if ($current -eq '/' -and $next -eq '/') { $lexicalState = 'line-comment'; $index++; continue }
            if ($current -eq '/' -and $next -eq '*') { $lexicalState = 'block-comment'; $index++; continue }
            if ($current -eq '"') { $lexicalState = 'string'; continue }
            if ($current -eq "'") { $lexicalState = 'character'; continue }
            if ($current -eq '{') { $braceDepth++ }
            if ($current -eq '}') {
                $braceDepth--
                if ($braceDepth -eq 0) {
                    $endExclusive = $index + 1
                    $bodyText = $SourceText.Substring($signatureMatch.Index, $endExclusive - $signatureMatch.Index)
                    $startLine = ([regex]::Matches($SourceText.Substring(0, $signatureMatch.Index), "`n")).Count + 1
                    $endLine = $startLine + ([regex]::Matches($bodyText, "`n")).Count
                    $bodyByteOffset = $SourceEncoding.GetByteCount($SourceText.Substring(0, $signatureMatch.Index))
                    $bodyByteCount = $SourceEncoding.GetByteCount($bodyText)
                    $bodyBytes = [byte[]]::new($bodyByteCount)
                    [Array]::Copy($SourceBytes, $bodyByteOffset, $bodyBytes, 0, $bodyByteCount)
                    $definitions.Add([pscustomobject]@{
                        Name = $FunctionName
                        Source = $SourcePath
                        StartLine = $startLine
                        EndLine = $endLine
                        ByteOffset = $bodyByteOffset
                        ByteCount = $bodyByteCount
                        Text = $bodyText
                        Bytes = $bodyBytes
                        BodySha256 = Get-BytesSha256 -Bytes $bodyBytes
                    })
                    break
                }
            }
        }
    }

    if ($definitions.Count -ne 1) {
        throw "Expected exactly one function definition '$FunctionName' in $SourcePath, found $($definitions.Count)."
    }
    return $definitions[0]
}

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
    $sourceBytes = [System.IO.File]::ReadAllBytes($sourceFile)
    $sourceEncoding = [System.Text.UTF8Encoding]::new($false, $true)
    $sourceEncodingName = 'UTF-8'
    try {
        $sourceText = $sourceEncoding.GetString($sourceBytes)
    }
    catch {
        $sourceEncoding = [System.Text.Encoding]::GetEncoding(1252)
        $sourceText = $sourceEncoding.GetString($sourceBytes)
        $sourceEncodingName = 'Windows-1252'
    }
    $sourceHash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
    $lineEnding = 'LF'
    if ($sourceText.Contains("`r`n")) {
        $lineEnding = 'CRLF'
        if ($sourceText.Replace("`r`n", '').Contains("`n")) { $lineEnding = 'Mixed' }
    }
    $hasBom = $sourceBytes.Length -ge 3 -and $sourceBytes[0] -eq 0xEF -and $sourceBytes[1] -eq 0xBB -and $sourceBytes[2] -eq 0xBF
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
    Purpose = 'Exact-source native queue probe; no callback execution or BF/LOA resolution.'
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
