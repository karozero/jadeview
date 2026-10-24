$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge'
$compilerEnvironment = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat'
$executable = Join-Path $outputDirectory 'activateur_capture.exe'
$runLog = Join-Path $outputDirectory 'run.log'

if (-not (Test-Path -LiteralPath $compilerEnvironment)) {
    throw "x86 MSVC environment was not found: $compilerEnvironment"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Push-Location $repoRoot
try {
    $compileLine = @(
        'call', "`"$compilerEnvironment`"", '>nul', '&&',
        'cl', '/nologo', '/TC', '/W3', '/Gy', '/O1', '/MD',
        '/Iexternal\jaded\src\dlls',
        '/Iexternal\jaded\src\dlls\BINK',
        '/Iexternal\jaded\src\libraries\XenonMeshProcessing\Sources',
        '/Foartifacts\native-script-bridge\',
        '/Fe:artifacts\native-script-bridge\activateur_capture.exe',
        'tools\NativeScriptBridge\activateur_capture.c',
        'tools\NativeScriptBridge\capture_host.c',
        'external\irisEng\ai\ai_bge\ai_compiledscripts_activateur_avec_sf.c',
        '/link', '/OPT:REF', '/INCREMENTAL:NO'
    ) -join ' '

    & $env:ComSpec /d /c $compileLine
    if ($LASTEXITCODE -ne 0) {
        throw "Native x86 compile/link failed with exit code $LASTEXITCODE."
    }

    & $executable | Tee-Object -FilePath $runLog
    if ($LASTEXITCODE -ne 0) {
        throw "Native callback capture assertions failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
