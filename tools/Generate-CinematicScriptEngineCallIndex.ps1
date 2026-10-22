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

function Get-ControlStatementEnd([string]$text, [int]$statementStart, [int]$depth = 0) {
    if ($depth -ge 32) { return -1 }
    while ($statementStart -lt $text.Length -and [char]::IsWhiteSpace($text[$statementStart])) {
        $statementStart++
    }
    if ($statementStart -ge $text.Length) { return -1 }
    if ($text[$statementStart] -eq '{') { return (Get-MatchingBrace $text $statementStart) }

    $remaining = $text.Substring($statementStart)
    if ([regex]::IsMatch($remaining, '^if\s*\(')) {
        $conditionOpen = $text.IndexOf('(', $statementStart)
        $conditionClose = Get-MatchingParenthesis $text $conditionOpen
        if ($conditionClose -lt 0) { return -1 }
        $thenEnd = Get-ControlStatementEnd $text ($conditionClose + 1) ($depth + 1)
        if ($thenEnd -lt 0) { return -1 }

        $elseStart = $thenEnd + 1
        while ($elseStart -lt $text.Length -and [char]::IsWhiteSpace($text[$elseStart])) { $elseStart++ }
        if ($elseStart + 4 -gt $text.Length -or $text.Substring($elseStart, 4) -ne 'else') {
            return $thenEnd
        }
        if ($elseStart + 4 -lt $text.Length -and
            [char]::IsLetterOrDigit($text[$elseStart + 4])) { return $thenEnd }
        return (Get-ControlStatementEnd $text ($elseStart + 4) ($depth + 1))
    }

    return $text.IndexOf(';', $statementStart)
}

function Split-CallArguments([string]$text) {
    $arguments = [System.Collections.Generic.List[string]]::new()
    $start = 0
    $parentheses = 0
    $brackets = 0
    $braces = 0
    $state = 'code'
    $escaped = $false
    for ($position = 0; $position -lt $text.Length; $position++) {
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
        switch ($current) {
            '(' { $parentheses++ }
            ')' { $parentheses-- }
            '[' { $brackets++ }
            ']' { $brackets-- }
            '{' { $braces++ }
            '}' { $braces-- }
            ',' {
                if ($parentheses -eq 0 -and $brackets -eq 0 -and $braces -eq 0) {
                    $arguments.Add(($text.Substring($start, $position - $start) -replace '\s+', ' ').Trim())
                    $start = $position + 1
                }
            }
        }
    }
    if ($text.Trim().Length -gt 0) {
        $arguments.Add(($text.Substring($start) -replace '\s+', ' ').Trim())
    }
    return ,$arguments.ToArray()
}

function Test-CIdentifier([string]$text, [string]$identifier) {
    $pattern = '(?<![A-Za-z0-9_])' + [regex]::Escape($identifier) + '(?![A-Za-z0-9_])'
    return [regex]::IsMatch($text, $pattern)
}

$definitionPattern = [regex]'(?m)^[ \t]*(?:int|void)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{'
$callPattern = [regex]'\bAI_EvalFunc_(?<api>[A-Za-z0-9_]+?)_C(?<variant>_(?:CURRENT|CURGLOB|C))?\s*\(|\bACTKitMerge(?<mergeMode>NoOver|Over)\s*\('
$calls = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -Filter 'ai_compiledscripts_*.c' -File -Recurse) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $lineStartList = [System.Collections.Generic.List[int]]::new()
    $lineStartList.Add(0)
    for ($index = 0; $index -lt $text.Length; $index++) {
        if ($text[$index] -eq "`n") { $lineStartList.Add($index + 1) }
    }
    $lineStarts = $lineStartList.ToArray()
    foreach ($definition in $definitionPattern.Matches($text)) {
        $functionName = $definition.Groups[1].Value
        if (-not $knownFunctions.ContainsKey($functionName)) { continue }
        $openBrace = $definition.Index + $definition.Length - 1
        $closeBrace = Get-MatchingBrace $text $openBrace
        if ($closeBrace -lt 0) { continue }
        $bodyStart = $openBrace + 1
        $body = $text.Substring($bodyStart, $closeBrace - $bodyStart)
        $functionLine = Get-LineNumberAtIndex $lineStarts $definition.Index
        $memberAssignments = [regex]'(?m)(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*(?<expression>[^;{}]+);'
        $allAssignments = $memberAssignments.Matches($body)
        $assignmentsByVariable = @{}
        foreach ($assignmentGroup in ($allAssignments | Group-Object { $_.Groups['variable'].Value })) {
            $assignmentsByVariable[$assignmentGroup.Name] = @($assignmentGroup.Group)
        }
        $memberFieldPattern = [regex]'->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)'
        $memberAliases = @{}
        foreach ($assignmentGroup in ($allAssignments | Group-Object { $_.Groups['variable'].Value })) {
            $fieldAssignments = foreach ($assignment in $assignmentGroup.Group) {
                foreach ($fieldMatch in $memberFieldPattern.Matches($assignment.Groups['expression'].Value)) {
                    [pscustomobject]@{
                        Field = $fieldMatch.Groups['field'].Value
                        Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $assignment.Index)
                    }
                }
            }
            $distinctFields = @($fieldAssignments | Group-Object Field)
            if ($distinctFields.Count -eq 1) {
                $memberAliases[$assignmentGroup.Name] = [pscustomobject]@{
                    Field = $distinctFields[0].Name
                    Line = $distinctFields[0].Group[0].Line
                }
            }
        }
        $ifConditions = [System.Collections.Generic.List[object]]::new()
        foreach ($ifMatch in ([regex]'\bif\s*\(').Matches($body)) {
            $conditionOpen = $ifMatch.Index + $ifMatch.Length - 1
            $conditionClose = Get-MatchingParenthesis $body $conditionOpen
            if ($conditionClose -lt 0) { continue }
            $thenEnd = Get-ControlStatementEnd $body ($conditionClose + 1)
            if ($thenEnd -lt 0) { continue }
            $conditionText = $body.Substring($conditionOpen + 1, $conditionClose - $conditionOpen - 1)
            $ifConditions.Add([pscustomobject]@{
                Start = $ifMatch.Index
                End = $thenEnd
                Kind = 'if'
                Expression = ($conditionText -replace '\s+', ' ').Trim()
                Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $ifMatch.Index)
            })
            $elseStart = $thenEnd + 1
            while ($elseStart -lt $body.Length -and [char]::IsWhiteSpace($body[$elseStart])) { $elseStart++ }
            $hasElseKeyword = $elseStart + 4 -le $body.Length -and $body.Substring($elseStart, 4) -eq 'else'
            $hasElseBoundary = $elseStart + 4 -ge $body.Length
            if (-not $hasElseBoundary) {
                $hasElseBoundary = -not [char]::IsLetterOrDigit($body[$elseStart + 4])
            }
            if ($hasElseKeyword -and $hasElseBoundary) {
                $elseBodyStart = $elseStart + 4
                while ($elseBodyStart -lt $body.Length -and [char]::IsWhiteSpace($body[$elseBodyStart])) { $elseBodyStart++ }
                if ($elseBodyStart -lt $body.Length) {
                    $elseBlockClose = Get-ControlStatementEnd $body $elseBodyStart
                    if ($elseBlockClose -ge $elseBodyStart) {
                        $ifConditions.Add([pscustomobject]@{
                            Start = $elseBodyStart
                            End = $elseBlockClose
                            Kind = 'else'
                            Expression = ($conditionText -replace '\s+', ' ').Trim()
                            Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $ifMatch.Index)
                        })
                    }
                }
            }
        }
        $loopContexts = [System.Collections.Generic.List[object]]::new()
        foreach ($loopMatch in ([regex]'\b(?<kind>for|while)\s*\(').Matches($body)) {
            $conditionOpen = $loopMatch.Index + $loopMatch.Length - 1
            $conditionClose = Get-MatchingParenthesis $body $conditionOpen
            if ($conditionClose -lt 0) { continue }
            $loopEnd = Get-ControlStatementEnd $body ($conditionClose + 1)
            if ($loopEnd -lt 0) { continue }
            $loopExpression = ($body.Substring($conditionOpen + 1, $conditionClose - $conditionOpen - 1) -replace '\s+', ' ').Trim()
            $loopInitializer = ($loopExpression -split ';', 2)[0]
            $loopVariableMatch = [regex]::Match($loopInitializer, '^(?:int\s+)?(?<variable>[A-Za-z_]\w*)\s*=')
            $loopVariableModified = $false
            if ($loopVariableMatch.Success) {
                $loopVariable = [regex]::Escape($loopVariableMatch.Groups['variable'].Value)
                $loopBodyStart = $conditionClose + 1
                $loopBodyText = $body.Substring($loopBodyStart, $loopEnd - $loopBodyStart + 1)
                $loopVariableModified = [regex]::IsMatch($loopBodyText,
                    "(?:\+\+\s*$loopVariable\b|--\s*$loopVariable\b|\b$loopVariable\s*(?:\+\+|--|\+=|-=|=(?!=)))")
            }
            $loopContexts.Add([pscustomobject]@{
                Start = $loopMatch.Index
                End = $loopEnd
                Kind = $loopMatch.Groups['kind'].Value
                Expression = $loopExpression
                Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $loopMatch.Index)
                InductionVariableModified = $loopVariableModified
            })
        }
        foreach ($loopMatch in ([regex]'\bdo\b').Matches($body)) {
            $loopEnd = Get-ControlStatementEnd $body ($loopMatch.Index + $loopMatch.Length)
            if ($loopEnd -lt 0) { continue }
            $loopContexts.Add([pscustomobject]@{
                Start = $loopMatch.Index
                End = $loopEnd
                Kind = 'do'
                Expression = ''
                Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $loopMatch.Index)
            })
        }
        foreach ($call in $callPattern.Matches($body)) {
            $apiName = if ($call.Groups['mergeMode'].Success) { 'ACTKitMerge' } else { $call.Groups['api'].Value }
            $apiVariant = if ($call.Groups['mergeMode'].Success) {
                if ($call.Groups['mergeMode'].Value -eq 'NoOver') { 'NO_OVERWRITE' } else { 'OVERWRITE' }
            } else { $call.Groups['variant'].Value.TrimStart('_') }
            $branchConditions = @($ifConditions | Where-Object {
                $_.Start -le $call.Index -and $_.End -gt $call.Index
            } | Sort-Object Start | ForEach-Object {
                [ordered]@{
                    Kind = $_.Kind
                    Expression = $_.Expression
                    Line = $_.Line
                    InductionVariableModified = $_.InductionVariableModified
                }
            })
            $openParenthesis = $call.Index + $call.Length - 1
            $closeParenthesis = Get-MatchingParenthesis $body $openParenthesis
            if ($closeParenthesis -lt 0) { continue }
            $arguments = Split-CallArguments $body.Substring($openParenthesis + 1,
                $closeParenthesis - $openParenthesis - 1)
            $argumentFields = [System.Collections.Generic.List[object]]::new()
            $argumentText = $body.Substring($openParenthesis + 1,
                $closeParenthesis - $openParenthesis - 1)
            foreach ($fieldMatch in $memberFieldPattern.Matches($argumentText)) {
                $argumentFields.Add([ordered]@{
                    Name = $fieldMatch.Groups['field'].Value
                    AliasVariable = $null
                    AssignmentLine = $null
                })
            }
            foreach ($alias in $memberAliases.GetEnumerator()) {
                if (Test-CIdentifier $argumentText $alias.Key) {
                    $argumentFields.Add([ordered]@{
                        Name = $alias.Value.Field
                        AliasVariable = $alias.Key
                        AssignmentLine = $alias.Value.Line
                    })
                }
            }
            $argumentAssignments = [System.Collections.Generic.List[object]]::new()
            $pendingAssignments = [System.Collections.Generic.Queue[object]]::new()
            $visitedAssignments = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $localIdentifiers = [regex]::Matches($argumentText, '\b[A-Za-z_][A-Za-z0-9_]*\b') |
                ForEach-Object { $_.Value } | Sort-Object -Unique
            foreach ($identifier in $localIdentifiers) {
                if ($assignmentsByVariable.ContainsKey($identifier)) {
                    $pendingAssignments.Enqueue([pscustomobject]@{
                        Variable = $identifier
                        BeforeIndex = $call.Index
                        Depth = 0
                    })
                }
            }
            while ($pendingAssignments.Count -gt 0 -and $argumentAssignments.Count -lt 24) {
                $pending = $pendingAssignments.Dequeue()
                $assignments = @($assignmentsByVariable[$pending.Variable] | Where-Object {
                    $_.Index -lt $pending.BeforeIndex
                })
                if ($assignments.Count -eq 0 -or $assignments[-1] -eq $null) { continue }
                $assignment = $assignments[-1]
                $assignmentIdentity = "$($pending.Variable)|$($assignment.Index)"
                if (-not $visitedAssignments.Add($assignmentIdentity)) { continue }
                $argumentAssignments.Add([ordered]@{
                    Variable = $pending.Variable
                    Expression = ($assignment.Groups['expression'].Value -replace '\s+', ' ').Trim()
                    Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $assignment.Index)
                    Depth = $pending.Depth
                    IsUnique = $assignments.Count -eq 1
                })
                if ($pending.Depth -ge 3) { continue }
                $expressionIdentifiers = [regex]::Matches($assignment.Groups['expression'].Value,
                    '\b[A-Za-z_][A-Za-z0-9_]*\b') | ForEach-Object { $_.Value } | Sort-Object -Unique
                foreach ($expressionIdentifier in $expressionIdentifiers) {
                    if ($assignmentsByVariable.ContainsKey($expressionIdentifier)) {
                        $pendingAssignments.Enqueue([pscustomobject]@{
                            Variable = $expressionIdentifier
                            BeforeIndex = $assignment.Index
                            Depth = $pending.Depth + 1
                        })
                    }
                }
            }
            $assignmentAlternatives = [System.Collections.Generic.List[object]]::new()
            $ovaFieldWrites = [System.Collections.Generic.List[object]]::new()
            if ($apiName -eq 'ACTKitMerge') {
            $instanceDataAliases = @($memberAliases.GetEnumerator() | Where-Object {
                $_.Value.Field -eq 'instanceVarDataCurrent'
            } | ForEach-Object { $_.Key })
            $ownerFieldWritePattern = [regex]'(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*(?<value>[^;]+);'
            $ownerFieldWriteMatches = @($ownerFieldWritePattern.Matches($body.Substring(0, $call.Index)) |
                Where-Object { $_.Groups['receiver'].Value -in $instanceDataAliases })
            $pendingAlternatives = [System.Collections.Generic.Queue[object]]::new()
            $visitedAlternatives = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($identifier in $localIdentifiers) {
                if ($assignmentsByVariable.ContainsKey($identifier)) {
                    $pendingAlternatives.Enqueue([pscustomobject]@{
                        Variable = $identifier
                        BeforeIndex = $call.Index
                        Depth = 0
                    })
                }
            }
            foreach ($write in $ownerFieldWriteMatches) {
                $writeIndex = $write.Index
                foreach ($condition in ($ifConditions | Where-Object {
                    $_.Start -le $writeIndex -and $_.End -gt $writeIndex
                })) {
                    $conditionVariables = [regex]::Matches($condition.Expression,
                        '\b[A-Za-z_][A-Za-z0-9_]*\b') | ForEach-Object { $_.Value } | Sort-Object -Unique
                    foreach ($conditionVariable in $conditionVariables) {
                        if ($assignmentsByVariable.ContainsKey($conditionVariable)) {
                            $pendingAlternatives.Enqueue([pscustomobject]@{
                                Variable = $conditionVariable
                                BeforeIndex = $writeIndex
                                Depth = 0
                            })
                        }
                    }
                }
            }
            while ($pendingAlternatives.Count -gt 0 -and $assignmentAlternatives.Count -lt 64) {
                $pending = $pendingAlternatives.Dequeue()
                $assignments = @($assignmentsByVariable[$pending.Variable] | Where-Object {
                    $_.Index -lt $pending.BeforeIndex
                } | Sort-Object Index -Descending)
                foreach ($assignment in $assignments) {
                    $assignmentIdentity = "$($pending.Variable)|$($assignment.Index)"
                    if (-not $visitedAlternatives.Add($assignmentIdentity)) { continue }
                    $assignmentIndex = $assignment.Index
                    $assignmentAlternatives.Add([ordered]@{
                        Variable = $pending.Variable
                        Expression = ($assignment.Groups['expression'].Value -replace '\s+', ' ').Trim()
                        Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $assignmentIndex)
                        BranchConditions = [object[]]@($ifConditions | Where-Object {
                            $_.Start -le $assignmentIndex -and $_.End -gt $assignmentIndex
                        } | Sort-Object Start | ForEach-Object {
                            [ordered]@{ Kind = $_.Kind; Expression = $_.Expression; Line = $_.Line }
                        })
                    })
                    if ($pending.Depth -ge 3) { continue }
                    $expressionIdentifiers = [regex]::Matches($assignment.Groups['expression'].Value,
                        '\b[A-Za-z_][A-Za-z0-9_]*\b') | ForEach-Object { $_.Value } | Sort-Object -Unique
                    foreach ($expressionIdentifier in $expressionIdentifiers) {
                        if ($assignmentsByVariable.ContainsKey($expressionIdentifier)) {
                            $pendingAlternatives.Enqueue([pscustomobject]@{
                                Variable = $expressionIdentifier
                                BeforeIndex = $assignmentIndex
                                Depth = $pending.Depth + 1
                            })
                        }
                    }
                    $assignmentBranchConditions = @($ifConditions | Where-Object {
                        $_.Start -le $assignmentIndex -and $_.End -gt $assignmentIndex
                    })
                    foreach ($condition in $assignmentBranchConditions) {
                        $conditionVariables = [regex]::Matches($condition.Expression,
                            '\b[A-Za-z_][A-Za-z0-9_]*\b') | ForEach-Object { $_.Value } | Sort-Object -Unique
                        foreach ($conditionVariable in $conditionVariables) {
                            if ($assignmentsByVariable.ContainsKey($conditionVariable)) {
                                $pendingAlternatives.Enqueue([pscustomobject]@{
                                    Variable = $conditionVariable
                                    BeforeIndex = $assignmentIndex
                                    Depth = $pending.Depth + 1
                                })
                            }
                        }
                    }
                }
            }
            foreach ($write in $ownerFieldWriteMatches) {
                $writeIndex = $write.Index
                $ovaFieldWrites.Add([ordered]@{
                    VariableName = $write.Groups['field'].Value
                    Expression = ($write.Groups['value'].Value -replace '\s+', ' ').Trim()
                    Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $writeIndex)
                    BranchConditions = [object[]]@($ifConditions | Where-Object {
                        $_.Start -le $writeIndex -and $_.End -gt $writeIndex
                    } | Sort-Object Start | ForEach-Object {
                        [ordered]@{ Kind = $_.Kind; Expression = $_.Expression; Line = $_.Line }
                    })
                })
            }
            }
            $uniqueArgumentFields = @($argumentFields | Group-Object {
                "$($_.Name)|$($_.AliasVariable)|$($_.AssignmentLine)"
            } | ForEach-Object { $_.Group[0] })
            $messageFields = [System.Collections.Generic.List[object]]::new()
            $messageFieldWrites = [System.Collections.Generic.List[object]]::new()
            if ($apiName -eq 'MSGSend' -and $arguments.Count -gt 1) {
                $messageArgument = [regex]::Match($arguments[1], '^\s*&\s*(?<path>[A-Za-z_][A-Za-z0-9_]*(?:->[A-Za-z_][A-Za-z0-9_]*)*)\s*$')
                if ($messageArgument.Success) {
                    $messagePath = $messageArgument.Groups['path'].Value
                    $assignmentPattern = [regex]'(?<path>[A-Za-z_][A-Za-z0-9_]*(?:->[A-Za-z_][A-Za-z0-9_]*)*)\.(?<field>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*=(?!=)\s*(?<value>[^;]+);'
                    $segment = $body.Substring(0, $call.Index)
                    $fieldAssignments = $assignmentPattern.Matches($segment) |
                        Where-Object { $_.Groups['path'].Value -eq $messagePath }
                    foreach ($fieldAssignment in $fieldAssignments) {
                        $writeIndex = $fieldAssignment.Index
                        $messageFieldWrites.Add([ordered]@{
                            Name = $fieldAssignment.Groups['field'].Value
                            Expression = ($fieldAssignment.Groups['value'].Value -replace '\s+', ' ').Trim()
                            Line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $writeIndex)
                            BranchConditions = [object[]]@($ifConditions | Where-Object {
                                $_.Start -le $writeIndex -and $_.End -gt $writeIndex
                            } | Sort-Object Start | ForEach-Object {
                                [ordered]@{ Kind = $_.Kind; Expression = $_.Expression; Line = $_.Line }
                            })
                        })
                    }
                    foreach ($fieldGroup in ($fieldAssignments | Group-Object { $_.Groups['field'].Value })) {
                        $assignment = $fieldGroup.Group[-1]
                        $line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $assignment.Index)
                        $messageFields.Add([ordered]@{
                            Name = $assignment.Groups['field'].Value
                            Expression = ($assignment.Groups['value'].Value -replace '\s+', ' ').Trim()
                            Line = $line
                        })
                    }
                }
            }
            $line = Get-LineNumberAtIndex $lineStarts ($bodyStart + $call.Index)
            $sourceFile = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
            $enclosingLoops = @($loopContexts | Where-Object {
                $_.Start -le $call.Index -and $_.End -gt $call.Index
            } | ForEach-Object {
                [ordered]@{ Kind = $_.Kind; Expression = $_.Expression; Line = $_.Line }
            })
            $calls.Add([ordered]@{
                FunctionName = $functionName
                Api = $apiName
                Variant = $apiVariant
                Arguments = [string[]]@($arguments)
                ArgumentAssignments = [object[]]@($argumentAssignments.ToArray())
                ArgumentAssignmentAlternatives = [object[]]@($assignmentAlternatives.ToArray())
                OvaFieldWrites = [object[]]@($ovaFieldWrites.ToArray())
                ArgumentFields = [object[]]@($uniqueArgumentFields)
                MessageFields = [object[]]@($messageFields.ToArray())
                MessageFieldWrites = [object[]]@($messageFieldWrites.ToArray())
                BranchConditions = [object[]]@($branchConditions)
                LoopContexts = [object[]]@($enclosingLoops)
                Source = $sourceFile
                Line = $line
            })
        }
    }
}

$uniqueCalls = @($calls | Group-Object { "$($_.FunctionName)|$($_.Api)|$($_.Variant)|$($_.Source)|$($_.Line)" } |
    ForEach-Object { $_.Group[0] })
$byFunction = [ordered]@{}
foreach ($call in $uniqueCalls) {
    if (-not $byFunction.Contains($call.FunctionName)) {
        $byFunction[$call.FunctionName] = [System.Collections.Generic.List[object]]::new()
    }
    $byFunction[$call.FunctionName].Add([ordered]@{
        Api = $call.Api
        Variant = $call.Variant
        Arguments = [string[]]@($call.Arguments)
        ArgumentAssignments = [object[]]@($call.ArgumentAssignments)
        ArgumentAssignmentAlternatives = [object[]]@($call.ArgumentAssignmentAlternatives)
        OvaFieldWrites = [object[]]@($call.OvaFieldWrites)
        ArgumentFields = [object[]]@($call.ArgumentFields)
        MessageFields = [object[]]@($call.MessageFields)
        MessageFieldWrites = [object[]]@($call.MessageFieldWrites)
        BranchConditions = [object[]]@($call.BranchConditions)
        LoopContexts = [object[]]@($call.LoopContexts)
        Source = $call.Source
        Line = $call.Line
    })
}

if ($byFunction.Count -eq 0) {
    throw "No source engine calls were indexed; refusing to write an empty index."
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
Write-Output "Wrote $($byFunction.Count) source functions and $($uniqueCalls.Count) direct engine API calls to $resolvedOutput"
