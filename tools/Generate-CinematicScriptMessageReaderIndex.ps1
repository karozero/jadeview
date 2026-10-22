param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$FunctionNamesPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$functionNames = (Resolve-Path -LiteralPath $FunctionNamesPath).Path
$knownFunctions = @{}
(Get-Content -LiteralPath $functionNames -Raw | ConvertFrom-Json).PSObject.Properties |
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
            if ($state -eq 'string' -and $current -eq '"' -or $state -eq 'character' -and $current -eq [char]39) { $state = 'code' }
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

function Get-LineNumber([string]$text, [int]$index) {
    $line = 1
    for ($position = 0; $position -lt $index; $position++) {
        if ($text[$position] -eq "`n") { $line++ }
    }
    return $line
}

function Test-CurrentObjectGet([System.Text.RegularExpressions.Match]$call) {
    if ($call.Groups['current'].Success) { return $true }
    $arguments = $call.Groups['arguments'].Value.Trim()
    return $arguments -eq 'AI_gpst_CurrentGameObject'
}

$definitionPattern = [regex]'(?m)^[ \t]*(?:int|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{'
$messageGetPattern = [regex]'(?<pointer>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:\([^;{}]*\)\s*)?AI_EvalFunc_MSGGet_C(?<current>_CURRENT)?\s*\((?<arguments>[^()]*)\)'
$copyPattern = [regex]'\bmemcpy\s*\(\s*&\s*(?<message>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*(?<pointer>[A-Za-z_][A-Za-z0-9_]*)\s*,'
$idReadPattern = [regex]'(?<message>[A-Za-z_][A-Za-z0-9_]*)\s*(?:->|\.)\s*msgID\s*==\s*(?<id>-?(?:0[xX][0-9A-Fa-f]+|[0-9]+))\b'
$reads = [System.Collections.Generic.List[object]]::new()

foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -Filter 'ai_compiledscripts_*.c' -File -Recurse) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $sourceFile = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
    if (-not $sourceFile.StartsWith('ai/ai_bge/', [StringComparison]::Ordinal)) { continue }
    foreach ($definition in $definitionPattern.Matches($text)) {
        $functionName = $definition.Groups['name'].Value
        if (-not $knownFunctions.ContainsKey($functionName)) { continue }
        $openBrace = $definition.Index + $definition.Length - 1
        $closeBrace = Get-MatchingBrace $text $openBrace
        if ($closeBrace -lt 0) { continue }
        $bodyStart = $openBrace + 1
        $body = $text.Substring($bodyStart, $closeBrace - $bodyStart)

        $queuePointers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($getCall in $messageGetPattern.Matches($body)) {
            if (Test-CurrentObjectGet $getCall) { [void]$queuePointers.Add($getCall.Groups['pointer'].Value) }
        }
        if ($queuePointers.Count -eq 0) { continue }

        $messageVariables = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($copy in $copyPattern.Matches($body)) {
            if ($queuePointers.Contains($copy.Groups['pointer'].Value)) {
                [void]$messageVariables.Add($copy.Groups['message'].Value)
            }
        }
        if ($messageVariables.Count -eq 0) { continue }

        foreach ($idRead in $idReadPattern.Matches($body)) {
            if (-not $messageVariables.Contains($idRead.Groups['message'].Value)) { continue }
            $rawId = $idRead.Groups['id'].Value
            $messageId = if ($rawId.StartsWith('0x', [StringComparison]::OrdinalIgnoreCase)) {
                [Convert]::ToInt32($rawId.Substring(2), 16)
            } else { [int]$rawId }
            $reads.Add([ordered]@{
                MessageId = $messageId
                FunctionName = $functionName
                Source = $sourceFile
                Line = Get-LineNumber $text ($bodyStart + $idRead.Index)
                QueueObject = 'AI_gpst_CurrentGameObject'
                ReadApi = 'MSGGet'
            })
        }
    }
}

$uniqueReads = @($reads | Group-Object {
    "$($_.FunctionName)|$($_.MessageId)|$($_.Source)|$($_.Line)|$($_.QueueObject)"
} | ForEach-Object { $_.Group[0] })
if ($uniqueReads.Count -eq 0) {
    throw 'No source-confirmed generic message reads were found; refusing to write an empty index.'
}

$byFunction = [ordered]@{}
foreach ($read in $uniqueReads) {
    if (-not $byFunction.Contains($read.FunctionName)) {
        $byFunction[$read.FunctionName] = [System.Collections.Generic.List[object]]::new()
    }
    $byFunction[$read.FunctionName].Add([ordered]@{
        MessageId = $read.MessageId
        Source = $read.Source
        Line = $read.Line
        QueueObject = $read.QueueObject
        ReadApi = $read.ReadApi
    })
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$json = ConvertTo-Json -InputObject $byFunction -Depth 8 -Compress
$serialized = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
$fileStream = [System.IO.File]::Create($resolvedOutput)
try {
    $gzipStream = [System.IO.Compression.GZipStream]::new($fileStream,
        [System.IO.Compression.CompressionLevel]::Optimal, $false)
    try { $gzipStream.Write($serialized, 0, $serialized.Length) }
    finally { $gzipStream.Dispose() }
}
finally { $fileStream.Dispose() }
Write-Output "Wrote $($uniqueReads.Count) source-confirmed message reads in $($byFunction.Count) functions to $resolvedOutput"
