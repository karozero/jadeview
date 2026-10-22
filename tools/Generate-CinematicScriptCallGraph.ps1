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
$knownFunctionNamesByKey = @{}
(Get-Content -LiteralPath $resolvedFunctionNames -Raw | ConvertFrom-Json).PSObject.Properties |
    ForEach-Object {
        $knownFunctions[$_.Value] = $true
        $knownFunctionNamesByKey[$_.Name.ToUpperInvariant()] = $_.Value
    }

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

$definitions = @{}
$definitionPattern = [regex]'(?m)^[ \t]*(?:int|void)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{'
foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -Filter 'ai_compiledscripts_*.c' -File -Recurse) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $lineStartList = [System.Collections.Generic.List[int]]::new()
    $lineStartList.Add(0)
    for ($index = 0; $index -lt $text.Length; $index++) {
        if ($text[$index] -eq "`n") { $lineStartList.Add($index + 1) }
    }
    $lineStarts = $lineStartList.ToArray()
    foreach ($match in $definitionPattern.Matches($text)) {
        $functionName = $match.Groups[1].Value
        if (-not $knownFunctions.ContainsKey($functionName)) { continue }
        $openBrace = $match.Index + $match.Length - 1
        $closeBrace = Get-MatchingBrace $text $openBrace
        if ($closeBrace -lt 0) { continue }
        $bodyStart = $openBrace + 1
        $sourceFile = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
        $definitions[$functionName] = [pscustomobject]@{
            Text = $text
            Body = $text.Substring($bodyStart, $closeBrace - $bodyStart)
            BodyStart = $bodyStart
            Source = $sourceFile
            FunctionLine = Get-LineNumberAtIndex $lineStarts $match.Index
            LineStarts = $lineStarts
        }
    }
}

$fieldBindings = @{}
$functionReferenceBindings = @{}
$fieldAssignmentPattern = [regex]'(?m)(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*(?<expression>[^;{}]+);'
$resolvedFunctionAssignmentPattern = [regex]'(?m)(?<variable>fctr_[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\(int\)\s*AI_ul_RealResolveOneRefFunction\s*\(\s*(?<key>0x[0-9A-Fa-f]+)u?\s*\)\s*;'
foreach ($functionName in $definitions.Keys) {
    $definition = $definitions[$functionName]
    $referencePattern = [regex]('\bfctr?_' + [regex]::Escape($functionName) + '_([A-Za-z_][A-Za-z0-9_]*)\b')
    foreach ($assignment in $resolvedFunctionAssignmentPattern.Matches($definition.Body)) {
        $variable = $assignment.Groups['variable'].Value
        $reference = $referencePattern.Match($variable)
        if (-not $reference.Success) { continue }
        $targetFunction = $reference.Groups[1].Value
        $resolvedName = $knownFunctionNamesByKey[$assignment.Groups['key'].Value.Substring(2).ToUpperInvariant()]
        if ($resolvedName -ne $targetFunction) { continue }
        $functionReferenceBindings["$($definition.Source)|$variable"] = [pscustomobject]@{
            Target = $targetFunction
            Variable = $variable
            Key = $assignment.Groups['key'].Value.Substring(2).ToUpperInvariant()
            AssignmentFunction = $functionName
            Source = $definition.Source
            Line = Get-LineNumberAtIndex $definition.LineStarts ($definition.BodyStart + $assignment.Index)
        }
    }
    foreach ($assignment in $fieldAssignmentPattern.Matches($definition.Body)) {
        foreach ($reference in $referencePattern.Matches($assignment.Groups['expression'].Value)) {
            $targetFunction = $reference.Groups[1].Value
            if (-not $knownFunctions.ContainsKey($targetFunction)) { continue }
            $field = $assignment.Groups['field'].Value
            $bindingKey = "$($definition.Source)|$field"
            if (-not $fieldBindings.ContainsKey($bindingKey)) {
                $fieldBindings[$bindingKey] = [System.Collections.Generic.List[object]]::new()
            }
            $fieldBindings[$bindingKey].Add([pscustomobject]@{
                Target = $targetFunction
                Field = $field
                AssignmentFunction = $functionName
                Source = $definition.Source
                Line = Get-LineNumberAtIndex $definition.LineStarts ($definition.BodyStart + $assignment.Index)
            })
        }
    }
}

$edges = [System.Collections.Generic.List[object]]::new()
$dispatchPattern = [regex]'(?s)AI_EvalFunc_(?<dispatch>[A-Za-z0-9_]+)_C\s*\((?<arguments>[^;]*?)\);'
foreach ($functionName in $definitions.Keys) {
    $definition = $definitions[$functionName]
    $body = $definition.Body
    $referencePattern = [regex]('\bfctr?_' + [regex]::Escape($functionName) + '_([A-Za-z_][A-Za-z0-9_]*)\b')
    foreach ($dispatch in $dispatchPattern.Matches($body)) {
        $dispatcher = $dispatch.Groups['dispatch'].Value
        if ($dispatcher -notmatch '^(AIExecute|AITrackChange|AITrackCurChange|AITrackCurChangeNow|AICBAdd|AICBDel)$') { continue }
        $arguments = $dispatch.Groups['arguments'].Value
        $callTargets = [System.Collections.Generic.List[object]]::new()
        foreach ($reference in $referencePattern.Matches($arguments)) {
            $targetFunction = $reference.Groups[1].Value
            $binding = $null
            if ($reference.Value.StartsWith('fctr_', [StringComparison]::Ordinal)) {
                $binding = $functionReferenceBindings["$($definition.Source)|$($reference.Value)"]
                if ($binding -eq $null -or $binding.Target -ne $targetFunction) { continue }
            }
            $callTargets.Add([pscustomobject]@{ Target = $targetFunction; Binding = $binding })
        }
        if ($callTargets.Count -eq 0) {
            $indirectField = [regex]::Match($arguments,
                '\(\s*tAIFunction\s*\*\s*\)\s*[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)')
            if ($indirectField.Success) {
                $bindingKey = "$($definition.Source)|$($indirectField.Groups['field'].Value)"
                foreach ($binding in $fieldBindings[$bindingKey]) {
                    $callTargets.Add([pscustomobject]@{ Target = $binding.Target; Binding = $binding })
                }
            }
        }
        foreach ($callTarget in $callTargets) {
            $callee = $callTarget.Target
            if (-not $knownFunctions.ContainsKey($callee)) { continue }
            $dispatchKind = switch ($dispatcher) {
                'AITrackChange' { 'track-change' }
                'AITrackCurChange' { 'set-current-track' }
                'AITrackCurChangeNow' { 'replace-current-track' }
                'AICBAdd' { 'register-callback' }
                'AICBDel' { 'unregister-callback' }
                default { 'execute' }
            }
            $trackIndex = $null
            if (($dispatcher -eq 'AITrackChange') -and ($arguments -match '^\s*(-?\d+)\s*,')) {
                $trackIndex = [int]$Matches[1]
            }
            $isCallbackRegistration = $dispatcher -in @('AICBAdd', 'AICBDel')
            $hasLiteralCallbackId = $arguments -match ',\s*(-?\d+)\s*,\s*\(tAIFunction\*\)\s*fct_'
            if ($isCallbackRegistration -and $hasLiteralCallbackId) {
                $dispatchKind += ":$($Matches[1])"
            }
            $edges.Add([ordered]@{
                From = $functionName
                To = $callee
                Dispatch = $dispatchKind
                TrackIndex = $trackIndex
                IndirectField = $callTarget.Binding?.Field
                AssignmentFunction = $callTarget.Binding?.AssignmentFunction
                AssignmentSource = $callTarget.Binding?.Source
                AssignmentLine = $callTarget.Binding?.Line
                ReferenceVariable = $callTarget.Binding?.Variable
                ResolvedFunctionKey = $callTarget.Binding?.Key
            })
        }
    }
}

$graph = [ordered]@{}
$uniqueEdges = @($edges | Group-Object { "$($_.From)|$($_.To)|$($_.Dispatch)|$($_.TrackIndex)|$($_.IndirectField)|$($_.AssignmentFunction)|$($_.AssignmentLine)|$($_.ReferenceVariable)|$($_.ResolvedFunctionKey)" } |
    ForEach-Object { $_.Group[0] })
foreach ($edge in $uniqueEdges) {
    if (-not $graph.Contains($edge.From)) { $graph[$edge.From] = [System.Collections.Generic.List[object]]::new() }
    $graph[$edge.From].Add([ordered]@{
        To = $edge.To
        Dispatch = $edge.Dispatch
        TrackIndex = $edge.TrackIndex
        IndirectField = $edge.IndirectField
        AssignmentFunction = $edge.AssignmentFunction
        AssignmentSource = $edge.AssignmentSource
        AssignmentLine = $edge.AssignmentLine
        ReferenceVariable = $edge.ReferenceVariable
        ResolvedFunctionKey = $edge.ResolvedFunctionKey
    })
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$json = ConvertTo-Json -InputObject $graph -Depth 8
[System.IO.File]::WriteAllText($resolvedOutput, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote $($graph.Count) function call nodes and $($uniqueEdges.Count) unique dispatch edges from $($edges.Count) source call sites to $resolvedOutput"
