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

function Get-LineNumberAtIndex([int[]]$lineStarts, [int]$index) {
    $lineIndex = [Array]::BinarySearch($lineStarts, $index)
    if ($lineIndex -lt 0) { $lineIndex = -$lineIndex - 2 }
    return $lineIndex + 1
}

function Get-MatchingDelimiter([string]$text, [int]$openIndex, [char]$open, [char]$close) {
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
        if ($current -eq $open) { $depth++ }
        if ($current -eq $close) {
            $depth--
            if ($depth -eq 0) { return $position }
        }
    }
    return -1
}

function Get-ControlStatementEnd([string]$text, [int]$statementStart, [int]$depth = 0) {
    if ($depth -ge 32) { return -1 }
    while ($statementStart -lt $text.Length -and [char]::IsWhiteSpace($text[$statementStart])) { $statementStart++ }
    if ($statementStart -ge $text.Length) { return -1 }
    if ($text[$statementStart] -eq '{') { return (Get-MatchingDelimiter $text $statementStart '{' '}') }
    $remaining = $text.Substring($statementStart)
    if ([regex]::IsMatch($remaining, '^if\s*\(')) {
        $conditionOpen = $text.IndexOf('(', $statementStart)
        $conditionClose = Get-MatchingDelimiter $text $conditionOpen '(' ')'
        if ($conditionClose -lt 0) { return -1 }
        $thenEnd = Get-ControlStatementEnd $text ($conditionClose + 1) ($depth + 1)
        if ($thenEnd -lt 0) { return -1 }
        $elseStart = $thenEnd + 1
        while ($elseStart -lt $text.Length -and [char]::IsWhiteSpace($text[$elseStart])) { $elseStart++ }
        if ($elseStart + 4 -gt $text.Length -or $text.Substring($elseStart, 4) -ne 'else') { return $thenEnd }
        if ($elseStart + 4 -lt $text.Length -and [char]::IsLetterOrDigit($text[$elseStart + 4])) { return $thenEnd }
        return (Get-ControlStatementEnd $text ($elseStart + 4) ($depth + 1))
    }
    return $text.IndexOf(';', $statementStart)
}

function Get-AssignmentAliases([string]$body) {
    $assignmentPattern = [regex]'(?m)(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*(?<expression>[^;{}]+);'
    $assignments = $assignmentPattern.Matches($body)
    $assignmentsByVariable = @{}
    foreach ($group in ($assignments | Group-Object { $_.Groups['variable'].Value })) {
        $assignmentsByVariable[$group.Name] = @($group.Group)
    }

    $aliases = @{}
    for ($pass = 0; $pass -lt $assignmentsByVariable.Count; $pass++) {
        $changed = $false
        foreach ($variable in $assignmentsByVariable.Keys) {
            if ($aliases.ContainsKey($variable)) { continue }
            $sources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $hasUnknownAssignment = $false
            foreach ($assignment in $assignmentsByVariable[$variable]) {
                $expression = $assignment.Groups['expression'].Value
                if ($expression -match '(?:->|\.)\s*instanceVarData(Current|Model)\b') {
                    [void]$sources.Add($(if ($Matches[1] -eq 'Current') { 'instance' } else { 'model' }))
                    continue
                }
                if ($expression -match '^\s*(?:\([^()]*\)\s*)*(?:0|NULL|nullptr)\s*$') { continue }
                $referencedAliases = @([regex]::Matches($expression, '\b[A-Za-z_][A-Za-z0-9_]*\b') |
                    ForEach-Object { $_.Value } | Where-Object { $aliases.ContainsKey($_) } | Select-Object -Unique)
                if ($referencedAliases.Count -eq 1) {
                    [void]$sources.Add($aliases[$referencedAliases[0]])
                    continue
                }
                $hasUnknownAssignment = $true
            }
            if (-not $hasUnknownAssignment -and $sources.Count -eq 1) {
                $aliases[$variable] = @($sources)[0]
                $changed = $true
            }
        }
        if (-not $changed) { break }
    }
    return @{ Assignments = $assignments; Aliases = $aliases }
}

$definitionPattern = [regex]'(?m)^[ \t]*(?:int|void)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{'
$accessPattern = [regex]'(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)'
$accesses = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -Filter 'ai_compiledscripts_*.c' -File -Recurse) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $lineStartList = [System.Collections.Generic.List[int]]::new()
    $lineStartList.Add(0)
    for ($index = 0; $index -lt $text.Length; $index++) { if ($text[$index] -eq "`n") { $lineStartList.Add($index + 1) } }
    $lineStarts = $lineStartList.ToArray()
    foreach ($definition in $definitionPattern.Matches($text)) {
        $functionName = $definition.Groups[1].Value
        if (-not $knownFunctions.ContainsKey($functionName)) { continue }
        $openBrace = $definition.Index + $definition.Length - 1
        $closeBrace = Get-MatchingDelimiter $text $openBrace '{' '}'
        if ($closeBrace -lt 0) { continue }
        $bodyStart = $openBrace + 1
        $body = $text.Substring($bodyStart, $closeBrace - $bodyStart)
        $aliasData = Get-AssignmentAliases $body
        if ($aliasData.Aliases.Count -eq 0) { continue }

        $conditions = [System.Collections.Generic.List[object]]::new()
        foreach ($ifMatch in ([regex]'\bif\s*\(').Matches($body)) {
            $conditionOpen = $ifMatch.Index + $ifMatch.Length - 1
            $conditionClose = Get-MatchingDelimiter $body $conditionOpen '(' ')'
            if ($conditionClose -lt 0) { continue }
            $thenEnd = Get-ControlStatementEnd $body ($conditionClose + 1)
            if ($thenEnd -lt 0) { continue }
            $conditions.Add([pscustomobject]@{
                Start = $ifMatch.Index
                End = $thenEnd
                Kind = 'if'
                Expression = ($body.Substring($conditionOpen + 1, $conditionClose - $conditionOpen - 1) -replace '\s+', ' ').Trim()
                Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $ifMatch.Index)
            })
        }

        foreach ($match in $accessPattern.Matches($body)) {
            $receiver = $match.Groups['receiver'].Value
            if (-not $aliasData.Aliases.ContainsKey($receiver)) { continue }
            $field = $match.Groups['field'].Value
            $after = $match.Index + $match.Length
            while ($after -lt $body.Length -and [char]::IsWhiteSpace($body[$after])) { $after++ }
            $before = $match.Index - 1
            while ($before -ge 0 -and [char]::IsWhiteSpace($body[$before])) { $before-- }
            $nextTwo = if ($after + 1 -lt $body.Length) { $body.Substring($after, 2) } else { '' }
            $nextCharacter = if ($after -lt $body.Length) { $body[$after] } else { [char]0 }
            $previousTwo = if ($before -ge 1) { $body.Substring($before - 1, 2) } else { '' }
            $access = if ($nextTwo -eq '++' -or $nextTwo -eq '--' -or $previousTwo -in @('++', '--')) {
                'read/write'
            } elseif ($body.Substring(0, $match.Index).TrimEnd() -match '&$') {
                'addressed'
            } elseif ($nextCharacter -eq '=' -and $nextTwo -ne '==') {
                'write'
            } elseif ($nextTwo.Length -eq 2 -and $nextTwo[1] -eq '=' -and $nextTwo[0] -in @('+', '-', '*', '/', '%', '&', '|', '^')) {
                'read/write'
            } else { 'read' }
            $line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $match.Index)
            $sourceLineStart = $text.LastIndexOf("`n", $bodyStart + $match.Index)
            $sourceLineEnd = $text.IndexOf("`n", $bodyStart + $match.Index)
            if ($sourceLineStart -lt 0) { $sourceLineStart = 0 } else { $sourceLineStart++ }
            if ($sourceLineEnd -lt 0) { $sourceLineEnd = $text.Length }
            $sourceLine = ($text.Substring($sourceLineStart, $sourceLineEnd - $sourceLineStart) -replace '\s+', ' ').Trim()
            $branchConditions = @($conditions | Where-Object { $_.Start -le $match.Index -and $_.End -gt $match.Index } |
                Sort-Object Start | ForEach-Object { [ordered]@{ Kind = $_.Kind; Expression = $_.Expression; Line = $_.Line } })
            $accesses.Add([ordered]@{
                FunctionName = $functionName
                VariableName = $field
                Access = $access
                Source = $aliasData.Aliases[$receiver]
                Receiver = $receiver
                Expression = $sourceLine
                BranchConditions = [object[]]$branchConditions
                SourceFile = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
                Line = $line
            })
        }
    }
}

$index = [ordered]@{}
foreach ($group in ($accesses | Group-Object { $_['FunctionName'] })) {
    $index[$group.Name] = @($group.Group | Group-Object { "$($_['VariableName'])|$($_['Access'])|$($_['SourceFile'])|$($_['Line'])" } |
        ForEach-Object { $_.Group[0] })
}
$json = $index | ConvertTo-Json -Depth 12 -Compress
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
if ($OutputPath.EndsWith('.gz', [System.StringComparison]::OrdinalIgnoreCase)) {
    $fileStream = [System.IO.File]::Create($OutputPath)
    try {
        $gzipStream = [System.IO.Compression.GZipStream]::new($fileStream, [System.IO.Compression.CompressionLevel]::Optimal)
        try {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
            $gzipStream.Write($bytes, 0, $bytes.Length)
        } finally { $gzipStream.Dispose() }
    } finally { $fileStream.Dispose() }
} else {
    [System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))
}
Write-Output "OVA source access index: $($index.Count) functions, $($accesses.Count) accesses -> $OutputPath"
