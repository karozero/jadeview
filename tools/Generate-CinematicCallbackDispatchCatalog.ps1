param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$callPattern = [regex]'\b(?<api>AI_ExecCallbackAll|AI_ExecCallback)\s*\((?<arguments>[^;]*?)\)\s*;'
$functionPattern = [regex]'(?m)^[ \t]*(?:[A-Za-z_][A-Za-z0-9_ \t\*]*[ \t]+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*\([^;{}]*\)[ \t]*(?:\r?\n[ \t]*)?\{'
$sites = [System.Collections.Generic.List[object]]::new()

function Get-ExecutionContext([string]$relativePath, [string]$functionName, [int]$callbackId) {
    if ($functionName -eq 'ENG_OneWorldEngineCall' -and $callbackId -eq 2) {
        return @{
            Label = 'World update after REC and hierarchy processing'
            Evidence = 'Iris engine.c:226-288'
            Dispatcher = 'Engine running; iterates current-world AICallbacksTablePF, skips invalid pointers, then applies AI_ExecCallback gates.'
            Caller = 'This world callback-all pass follows dynamics, collision, records, and hierarchy processing in ENG_OneWorldEngineCall.'
            Target = 'Each valid object currently in the world AI-callback EOT table.'
        }
    }
    if ($functionName -eq 'ANI_BeforeRender' -and $callbackId -eq 3) {
        return @{
            Label = 'Actor pre-render animation callback'
            Evidence = 'Iris animation.c:2390-2392,2396-2410'
            Dispatcher = 'AI_ExecCallback requires extended+AI instance, both blocking flags2 bits clear, a model, and at least one fixed or dynamic callback; fixed matching slot runs before matching dynamic entries.'
            Caller = 'Reached through visible-object traversal; object must be valid, flags2 0x4 clear, GAO flags 0x2 set, and have a skeleton group.'
            Target = 'The visible actor passed to ANI_BeforeRender.'
        }
    }
    if ($functionName -eq 'ANI_Render' -and $callbackId -eq 3) {
        return @{
            Label = 'Actor animation render callback'
            Evidence = 'Iris animation.c:2631-2648'
            Dispatcher = 'AI_ExecCallback requires extended+AI instance, both blocking flags2 bits clear, a model, and at least one fixed or dynamic callback; fixed matching slot runs before matching dynamic entries.'
            Caller = 'Runs only when animPtr[0] exists and the actor was not initialized in ANI_MainCall; the lead animation is advanced at zero delta before dispatch.'
            Target = 'The actor being rendered.'
        }
    }
    if ($functionName -eq 'WOR_Render_All_GO' -and $callbackId -eq 3) {
        return @{
            Label = 'Per-object world-render callback'
            Evidence = 'Iris world.c:2419-2465'
            Dispatcher = 'AI_ExecCallback requires extended+AI instance, both blocking flags2 bits clear, a model, and at least one fixed or dynamic callback; fixed matching slot runs before matching dynamic entries.'
            Caller = 'Runs during visible-world-object traversal for a valid object when flags2 0x4 and display flag 0x80000 are clear.'
            Target = 'The visible world object passed to WOR_Render_All_GO.'
        }
    }
    if ($functionName -eq 'GEO_Render' -and $callbackId -eq 0) {
        return @{
            Label = 'Per-GAO geometry callback before geometry processing'
            Evidence = 'Iris graphicsobjects/geo.c:761-780'
            Dispatcher = 'AI_ExecCallback requires extended+AI instance, both blocking flags2 bits clear, a model, and at least one fixed or dynamic callback; fixed matching slot runs before matching dynamic entries.'
            Caller = 'Only inside the material/draw-list branch before ordinary geometry processing.'
            Target = 'The visual GAO if it has AI; otherwise a bone GAO may route to its ANI initialization/reference GAO when that reference has AI.'
        }
    }
    if ($functionName -eq 'GEO_Render' -and $callbackId -eq 1) {
        return @{
            Label = 'Per-GAO geometry callback after geometry processing'
            Evidence = 'Iris graphicsobjects/geo.c:794,879-895'
            Dispatcher = 'AI_ExecCallback requires extended+AI instance, both blocking flags2 bits clear, a model, and at least one fixed or dynamic callback; fixed matching slot runs before matching dynamic entries.'
            Caller = 'Only after geometry processing inside the valid-vertices path.'
            Target = 'The visual GAO if it has AI; otherwise a bone GAO may route to its ANI initialization/reference GAO when that reference has AI.'
        }
    }
    if ($functionName -eq 'STR_Render' -and $callbackId -eq 0) {
        return @{
            Label = 'Per-object string-render callback before string rendering'
            Evidence = 'Iris graphicsobjects/geostr.c:2879-2895'
            Dispatcher = 'AI_ExecCallback requires extended+AI instance, both blocking flags2 bits clear, a model, and at least one fixed or dynamic callback; fixed matching slot runs before matching dynamic entries.'
            Caller = 'Runs when the string render counter differs and the string material exists.'
            Target = 'The string GAO.'
        }
    }
    if ($functionName -eq 'STR_Render' -and $callbackId -eq 1) {
        return @{
            Label = 'Per-object string-render callback after string rendering'
            Evidence = 'Iris graphicsobjects/geostr.c:3134-3144'
            Dispatcher = 'AI_ExecCallback requires extended+AI instance, both blocking flags2 bits clear, a model, and at least one fixed or dynamic callback; fixed matching slot runs before matching dynamic entries.'
            Caller = 'Runs after STR/GFX rendering when the rendered GAO has an extended component and AI instance.'
            Target = 'The string GAO.'
        }
    }

    return @{
        Label = 'Caller function indexed; execution phase unresolved'
        Evidence = "Source context: $relativePath :: $functionName"
        Dispatcher = 'AI_ExecCallback uses the Iris per-object eligibility checks; confirm the call implementation before relying on them.'
        Caller = 'Caller-specific gate is not indexed.'
        Target = 'Direct callback target is determined by the call argument and remains unresolved here.'
    }
}

foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -Filter '*.c' -File -Recurse) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in $callPattern.Matches($text)) {
        $arguments = $match.Groups['arguments'].Value
        $argumentParts = $arguments.Split(',')
        if ($argumentParts.Length -lt 2 -or $argumentParts[1].Trim() -notmatch '^-?\d+$') { continue }

        $lineNumber = 1 + [regex]::Matches($text.Substring(0, $match.Index), "`n").Count
        $relativePath = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
        $enclosingFunction = $null
        foreach ($functionMatch in $functionPattern.Matches($text)) {
            if ($functionMatch.Index -ge $match.Index) { break }
            if ($functionMatch.Groups['name'].Value -notin @('if', 'for', 'while', 'switch')) {
                $enclosingFunction = $functionMatch
            }
        }
        $functionName = if ($enclosingFunction) { $enclosingFunction.Groups['name'].Value } else { 'unknown function' }
        $callContext = Get-ExecutionContext $relativePath $functionName ([int]$argumentParts[1].Trim())
        $sites.Add([ordered]@{
            CallbackId = [int]$argumentParts[1].Trim()
            Api = $match.Groups['api'].Value
            Source = $relativePath
            Line = $lineNumber
            Call = "$($match.Groups['api'].Value)($($arguments.Trim()))"
            EnclosingFunction = $functionName
            ExecutionContext = $callContext.Label
            ContextEvidence = $callContext.Evidence
            DispatcherEligibility = $callContext.Dispatcher
            CallerEligibility = $callContext.Caller
            TargetResolution = $callContext.Target
        })
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$json = ConvertTo-Json -InputObject @($sites) -Depth 5
[System.IO.File]::WriteAllText($resolvedOutput, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote $($sites.Count) source callback dispatch sites to $resolvedOutput"
