$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$auditScript = Join-Path $PSScriptRoot 'measure-callback-imports.ps1'
$summaryDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\dependency-audit'

function Invoke-FunctionAudit {
    param([string] $FunctionName)

    & $auditScript -SourcePath external/irisEng/ai_common.c -FunctionName $FunctionName | Out-Null

    return Get-Content -LiteralPath (Join-Path $summaryDirectory "ai_common-$FunctionName-imports.txt")
}

function Assert-ContainsLine {
    param([string[]] $Lines, [string] $Expected)

    if ($Lines -notcontains $Expected) {
        throw "Expected audit output line was missing: $Expected"
    }
}

$modelAudit = Invoke-FunctionAudit -FunctionName 'AI_ul_CallbackLoadModel'
Assert-ContainsLine -Lines $modelAudit -Expected 'Direct references to functions defined in this translation unit: 2'
Assert-ContainsLine -Lines $modelAudit -Expected '  _AI_ul_CallbackLoadFunction'
Assert-ContainsLine -Lines $modelAudit -Expected '  _AI_ul_CallbackLoadVars'

$instanceAudit = Invoke-FunctionAudit -FunctionName 'AI_ResolveInstanceRef'
Assert-ContainsLine -Lines $instanceAudit -Expected 'Direct references to functions defined in this translation unit: 2'
Assert-ContainsLine -Lines $instanceAudit -Expected '  _AI_InstanceAfterLoading'
Assert-ContainsLine -Lines $instanceAudit -Expected '  _AI_ResolveModelRef'

$unknownFunctionFailed = $false
try {
    & $auditScript -SourcePath external/irisEng/ai_common.c -FunctionName AI_FunctionThatDoesNotExist | Out-Null
}
catch {
    $unknownFunctionFailed = $true
    $unknownOutput = $_.Exception.Message
}
if (-not $unknownFunctionFailed) {
    throw 'Unknown function names must fail instead of producing a translation-unit report.'
}
if ($unknownOutput -notmatch 'was not emitted as a COFF section') {
    throw 'Unknown function failure did not explain that no COFF section was emitted.'
}

Write-Output 'Native dependency audit regression checks passed.'
