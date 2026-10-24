$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$extractor = Join-Path $PSScriptRoot 'extract-resource-loader-slice.ps1'

function Assert-EqualHash {
    param([byte[]] $Bytes, [string] $Expected, [string] $Description)
    if ((Get-BytesSha256 -Bytes $Bytes) -ne $Expected) {
        throw "Hash mismatch: $Description"
    }
}

Import-Module (Join-Path $PSScriptRoot 'ExactSourceExtraction.psm1') -Force

# Non-ASCII content before and inside the function distinguishes byte and character offsets.
$accent = [char]0xE9
$prefix = "/* pr${accent}fix */`r`n"
$body = "void EncodingSample(void)`r`n{`r`n    const char *text = `"caf${accent}`";`r`n}`n"
$bodyWithoutTrailingNewline = $body.TrimEnd("`n")
foreach ($encoding in @([System.Text.UTF8Encoding]::new($false, $true), [System.Text.Encoding]::GetEncoding(1252))) {
    $source = $prefix + $body + 'void Other(void) {}'
    $sourceBytes = $encoding.GetBytes($source)
    $function = Get-FunctionBody -SourceText $source -SourceBytes $sourceBytes -SourceEncoding $encoding `
        -FunctionName 'EncodingSample' -SourcePath 'synthetic-encoding.c'
    $expectedBody = $encoding.GetBytes($bodyWithoutTrailingNewline)
    if ($function.ByteOffset -ne $encoding.GetByteCount($prefix) -or $function.ByteCount -ne $expectedBody.Length) {
        throw "Incorrect byte offset/count for $($encoding.WebName)."
    }
    Assert-EqualHash -Bytes $function.Bytes -Expected (Get-BytesSha256 -Bytes $expectedBody) `
        -Description "synthetic body $($encoding.WebName)"
}

# Invoke the complete entry point in a separate process, including its lexical self-checks.
& powershell -NoProfile -ExecutionPolicy Bypass -File $extractor
if ($LASTEXITCODE -ne 0) { throw "Source extractor failed: $LASTEXITCODE" }
$directory = Join-Path $repoRoot 'artifacts\native-script-bridge\resource-loader-probe'
$manifest = Get-Content -LiteralPath (Join-Path $directory 'generated_source_slice.manifest.json') -Raw | ConvertFrom-Json
$generated = [IO.File]::ReadAllBytes((Join-Path $repoRoot $manifest.GeneratedSource))
Assert-EqualHash -Bytes $generated -Expected $manifest.GeneratedSourceSha256 -Description 'generated compilation unit'
foreach ($source in $manifest.Sources) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $repoRoot $source.Path))
    Assert-EqualHash -Bytes $bytes -Expected $source.Sha256 -Description $source.Path
}
foreach ($function in $manifest.ExtractedFunctions) {
    $original = [IO.File]::ReadAllBytes((Join-Path $repoRoot $function.Source))
    foreach ($span in @(
        @{ Bytes = $original; Offset = $function.ByteOffset; Count = $function.ByteCount; Kind = 'source' },
        @{ Bytes = $generated; Offset = $function.GeneratedByteOffset; Count = $function.GeneratedByteCount; Kind = 'generated' }
    )) {
        if ($span.Offset -lt 0 -or $span.Count -lt 0 -or $span.Offset -gt $span.Bytes.Length - $span.Count) {
            throw "Manifest span out of bounds: $($function.Name) $($span.Kind)"
        }
        $slice = [byte[]]::new($span.Count)
        [Array]::Copy($span.Bytes, $span.Offset, $slice, 0, $span.Count)
        Assert-EqualHash -Bytes $slice -Expected $function.BodySha256 -Description "$($function.Name) $($span.Kind)"
    }
    if ($function.ByteCount -ne $function.GeneratedByteCount) {
        throw "Body length changed: $($function.Name)"
    }
}
Write-Output "PASS: exact-source extraction preserves UTF-8/Windows-1252 byte spans and $($manifest.ExtractedFunctions.Count) manifested source/generated body hashes."
