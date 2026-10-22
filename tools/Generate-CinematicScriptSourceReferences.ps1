param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$FunctionNamesPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$resolvedFunctionNames = (Resolve-Path -LiteralPath $FunctionNamesPath).Path
$knownFunctions = @{}
(Get-Content -LiteralPath $resolvedFunctionNames -Raw | ConvertFrom-Json).PSObject.Properties |
    ForEach-Object { $knownFunctions[$_.Value] = $true }

function Get-MatchingBrace([string]$text, [int]$openIndex) {
    $depth = 0
    $state = 'code'
    $escaped = $false
    for ($position = $openIndex; $position -lt $text.Length; $position++) {
        $current = $text[$position]
        $next = if ($position + 1 -lt $text.Length) { $text[$position + 1] } else { [char]0 }
        if ($state -eq 'line-comment') {
            if ($current -eq "`n") { $state = 'code' }
            continue
        }
        if ($state -eq 'block-comment') {
            if ($current -eq '*' -and $next -eq '/') { $state = 'code'; $position++ }
            continue
        }
        if ($state -in @('string', 'character')) {
            if ($escaped) { $escaped = $false; continue }
            if ($current -eq '\') { $escaped = $true; continue }
            if (($state -eq 'string' -and $current -eq '"') -or ($state -eq 'character' -and $current -eq "'")) {
                $state = 'code'
            }
            continue
        }
        if ($current -eq '/' -and $next -eq '/') { $state = 'line-comment'; $position++; continue }
        if ($current -eq '/' -and $next -eq '*') { $state = 'block-comment'; $position++; continue }
        if ($current -eq '"') { $state = 'string'; continue }
        if ($current -eq "'") { $state = 'character'; continue }
        if ($current -eq '{') { $depth++ }
        if ($current -eq '}') {
            $depth--
            if ($depth -eq 0) { return $position }
        }
    }
    return -1
}

$definitionPattern = [regex]'(?m)^\s*(?:int|void)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{'
$referencePattern = [regex]'(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:\([^)]*\)\s*)*AI_ul_RealResolveOneRef(?<kind>Object|Function)\s*\(\s*(?<key>0x[0-9A-Fa-f]+)(?:[uU])?\s*\)'
$references = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -Filter 'ai_compiledscripts_*.c' -File -Recurse) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($definition in $definitionPattern.Matches($text)) {
        $functionName = $definition.Groups[1].Value
        if (-not $knownFunctions.ContainsKey($functionName)) { continue }
        $openBrace = $definition.Index + $definition.Length - 1
        $closeBrace = Get-MatchingBrace $text $openBrace
        if ($closeBrace -lt 0) { continue }
        $bodyStart = $openBrace + 1
        $body = $text.Substring($bodyStart, $closeBrace - $bodyStart)
        $functionLine = 1 + [regex]::Matches($text.Substring(0, $definition.Index), "`n").Count
        foreach ($reference in $referencePattern.Matches($body)) {
            $symbol = $reference.Groups['symbol'].Value
            $functionPrefix = "${functionName}_"
            $callbackPrefix = "fct_${functionName}_"
            if ($symbol.StartsWith($callbackPrefix, [StringComparison]::Ordinal)) {
                $symbol = $symbol.Substring($callbackPrefix.Length)
            }
            elseif ($symbol.StartsWith($functionPrefix, [StringComparison]::Ordinal)) {
                $symbol = $symbol.Substring($functionPrefix.Length)
            }
            $line = $functionLine + [regex]::Matches($body.Substring(0, $reference.Index), "`n").Count
            $references.Add([ordered]@{
                FunctionName = $functionName
                Kind = $reference.Groups['kind'].Value
                Symbol = $symbol
                Key = $reference.Groups['key'].Value
                Source = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
                Line = $line
            })
        }
    }
}

$uniqueReferences = @($references | Group-Object { "$($_.FunctionName)|$($_.Kind)|$($_.Symbol)|$($_.Key)|$($_.Source)|$($_.Line)" } |
    ForEach-Object { $_.Group[0] })
$byFunction = [ordered]@{}
foreach ($reference in $uniqueReferences) {
    if (-not $byFunction.Contains($reference.FunctionName)) {
        $byFunction[$reference.FunctionName] = [System.Collections.Generic.List[object]]::new()
    }
    $byFunction[$reference.FunctionName].Add([ordered]@{
        Kind = $reference.Kind
        Symbol = $reference.Symbol
        Key = $reference.Key
        Source = $reference.Source
        Line = $reference.Line
    })
}

if ($byFunction.Count -eq 0) {
    throw 'No source references were indexed; refusing to write an empty index.'
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$serialized = [System.Text.UTF8Encoding]::new($false).GetBytes(
    (ConvertTo-Json -InputObject $byFunction -Depth 8 -Compress))
$fileStream = [System.IO.File]::Create($resolvedOutput)
try {
    $gzipStream = [System.IO.Compression.GZipStream]::new($fileStream,
        [System.IO.Compression.CompressionLevel]::Optimal, $false)
    try { $gzipStream.Write($serialized, 0, $serialized.Length) }
    finally { $gzipStream.Dispose() }
}
finally { $fileStream.Dispose() }
Write-Output "Wrote $($byFunction.Count) source functions and $($uniqueReferences.Count) object/function reference sites to $resolvedOutput"
