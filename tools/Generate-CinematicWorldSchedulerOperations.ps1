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

function Get-MatchingParenthesis([string]$text, [int]$openIndex) {
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
        if ($current -eq '(') { $depth++ }
        if ($current -eq ')') {
            $depth--
            if ($depth -eq 0) { return $position }
        }
    }
    return -1
}

$definitionPattern = [regex]'(?m)^\s*(?:int|void)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{'
$operationPattern = [regex]'\bAI_EvalFunc_WOR(?<operation>Pause|UnPause|PlayAbsTime)_C\s*\('
$operations = [System.Collections.Generic.List[object]]::new()
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
        foreach ($operation in $operationPattern.Matches($body)) {
            $openParenthesis = $operation.Index + $operation.Length - 1
            $closeParenthesis = Get-MatchingParenthesis $body $openParenthesis
            if ($closeParenthesis -lt 0) { continue }
            $arguments = $body.Substring($openParenthesis + 1, $closeParenthesis - $openParenthesis - 1).Trim()
            $argumentSources = [System.Collections.Generic.List[string]]::new()
            foreach ($assignment in [regex]::Matches($body,
                    '\b(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<call>AI_EvalFunc_[A-Za-z0-9_]+_C(?:_(?:CURRENT|CURGLOB))?)\s*\(\s*\)\s*;')) {
                $variableName = $assignment.Groups['variable'].Value
                $variablePattern = '(?<![A-Za-z0-9_])' + [regex]::Escape($variableName) + '(?![A-Za-z0-9_])'
                if ([regex]::IsMatch($arguments, $variablePattern)) {
                    $argumentSources.Add("$variableName = $($assignment.Groups['call'].Value)")
                }
            }
            $line = 1 + [regex]::Matches($text.Substring(0, $bodyStart + $operation.Index), "`n").Count
            $sourceFile = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
            $operations.Add([ordered]@{
                FunctionName = $functionName
                Operation = $operation.Groups['operation'].Value
                Arguments = $arguments
                ArgumentSources = @($argumentSources | Select-Object -Unique)
                Source = $sourceFile
                Line = $line
            })
        }
    }
}

$uniqueOperations = @($operations | Group-Object { "$($_.FunctionName)|$($_.Operation)|$($_.Source)|$($_.Line)" } |
    ForEach-Object { $_.Group[0] })
$byFunction = [ordered]@{}
foreach ($operation in $uniqueOperations) {
    if (-not $byFunction.Contains($operation.FunctionName)) {
        $byFunction[$operation.FunctionName] = [System.Collections.Generic.List[object]]::new()
    }
    $byFunction[$operation.FunctionName].Add([ordered]@{
        Operation = $operation.Operation
        Arguments = $operation.Arguments
        ArgumentSources = $operation.ArgumentSources
        Source = $operation.Source
        Line = $operation.Line
    })
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$json = ConvertTo-Json -InputObject $byFunction -Depth 8
[System.IO.File]::WriteAllText($resolvedOutput, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote $($byFunction.Count) source functions and $($uniqueOperations.Count) world-scheduler operation sites to $resolvedOutput"
