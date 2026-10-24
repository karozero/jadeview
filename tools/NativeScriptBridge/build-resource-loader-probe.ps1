param(
    [string] $ManifestPath = 'artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/manifest.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestFullPath = (Resolve-Path (Join-Path $repoRoot $ManifestPath)).Path
$manifestDirectory = [System.IO.Path]::GetDirectoryName($manifestFullPath)
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$fixture = $manifest.Fixture
$resourcesByFile = @{}
foreach ($resource in $fixture.Resources) {
    $resourcesByFile[$resource.File] = $resource
}

function Resolve-ManifestResource {
    param([string] $Role)

    $link = @($fixture.Links | Where-Object { $_.Role -eq $Role })
    if ($link.Count -ne 1 -or $link[0].Status -ne 'Resolved') {
        throw "Expected exactly one resolved manifest link for role '$Role'."
    }

    $resource = $resourcesByFile[$link[0].File]
    if (-not $resource -or $resource.Status -ne 'Resolved') {
        throw "Manifest link '$Role' does not identify a resolved resource entry."
    }

    $resourcePath = [System.IO.Path]::GetFullPath((Join-Path $manifestDirectory $resource.File))
    $manifestPrefix = $manifestDirectory.TrimEnd('\') + '\'
    if (-not $resourcePath.StartsWith($manifestPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest resource path escaped its fixture directory: $($resource.File)"
    }

    $resourceBytes = [System.IO.File]::ReadAllBytes($resourcePath)
    $resourceHash = (Get-FileHash -LiteralPath $resourcePath -Algorithm SHA256).Hash
    if ($resourceBytes.Length -ne [int]$resource.Size -or $resourceHash -ne $resource.Sha256) {
        throw "Manifest resource integrity check failed for $($resource.File)."
    }

    return [pscustomobject]@{
        Key = [string]$link[0].Key
        EntryIndex = $link[0].EntryIndex
        Path = $resourcePath
        Entry = $resource
    }
}

$instanceResource = Resolve-ManifestResource -Role 'instance'
$instanceVariablesResource = Resolve-ManifestResource -Role 'instance-variables'
$modelResource = Resolve-ManifestResource -Role 'model'
$modelVariablesResource = Resolve-ManifestResource -Role 'model-entry:.ova'
$modelFunctionResource = Resolve-ManifestResource -Role 'model-entry:.ofc'
if ($instanceResource.Entry.Kind -ne 'q' -or
    $instanceVariablesResource.Entry.Kind -ne 't' -or
    $modelResource.Entry.Kind -ne 's' -or
    $modelVariablesResource.Entry.Kind -ne 't' -or
    $modelFunctionResource.Entry.Kind -ne 'r') {
    throw 'Manifest resource kinds do not match the bounded AIC/AIM reference chain.'
}
if ($instanceResource.Entry.Size -ne 8 -or
    $modelResource.Entry.Size -le 0 -or
    ([int]$modelResource.Entry.Size % 8) -ne 0) {
    throw 'AIC/AIM manifest payload lengths do not meet the bounded reader prerequisites.'
}
if ($modelVariablesResource.EntryIndex -ne 0 -or $modelFunctionResource.EntryIndex -ne 1) {
    throw 'AIM manifest entries no longer have the expected OVA/OFC order for this probe.'
}

$compilerEnvironment = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat'
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\resource-loader-probe'
$executablePath = Join-Path $outputDirectory 'resource_loader_probe.exe'
$runLogName = 'run-{0}-{1}.log' -f $manifest.Container, $fixture.OwnerKey
$runLog = Join-Path $outputDirectory $runLogName
if (-not (Test-Path -LiteralPath $compilerEnvironment)) {
    throw "x86 MSVC environment was not found: $compilerEnvironment"
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'extract-resource-loader-slice.ps1') | Out-Host

    $compileLine = @(
        'call', "`"$compilerEnvironment`"", '>nul', '&&',
        'cl', '/nologo', '/TC', '/W3', '/Gy', '/O1', '/MD',
        '/Iexternal\jaded\src\dlls',
        '/Iexternal\jaded\src\dlls\BINK',
        '/Iexternal\jaded\src\libraries\XenonMeshProcessing\Sources',
        '/Foartifacts\native-script-bridge\resource-loader-probe\',
        '/Fe:artifacts\native-script-bridge\resource-loader-probe\resource_loader_probe.exe',
        'tools\NativeScriptBridge\resource_loader_probe.c',
        '/link', '/OPT:REF', '/INCREMENTAL:NO'
    ) -join ' '
    & $env:ComSpec /d /c $compileLine
    if ($LASTEXITCODE -ne 0) {
        throw "Native resource-loader probe failed to compile/link with exit code $LASTEXITCODE."
    }

    $runArguments = @(
        $modelResource.Key,
        $instanceVariablesResource.Key,
        $modelVariablesResource.Key,
        $modelFunctionResource.Key,
        $instanceResource.Path,
        $modelResource.Path
    )
    $outputLines = & $executablePath @runArguments 2>&1
    $exitCode = $LASTEXITCODE
    $outputLines | Set-Content -LiteralPath $runLog -Encoding UTF8
    if ($exitCode -ne 0) {
        throw "Native source callbacks exited ${exitCode}: $($outputLines -join [Environment]::NewLine)"
    }

    $expectedLines = @(
        ('INSTANCE_MODEL_REF=' + $modelResource.Key),
        ('INSTANCE_VARS_REF=' + $instanceVariablesResource.Key),
        ('MODEL_VARS_REF=' + $modelVariablesResource.Key),
        ('MODEL_FUNCTION_REF=' + $modelFunctionResource.Key),
        'RESULT=source_callback_resource_references_only'
    )
    $observedLines = @($outputLines | ForEach-Object { $_.ToString().Trim() })
    foreach ($expectedLine in $expectedLines) {
        if ($observedLines -notcontains $expectedLine) {
            throw "Native callback output did not match manifest link '$expectedLine'. Actual output: $($observedLines -join ', ')"
        }
    }

    Write-Output "Native loader reference probe passed for container $($manifest.Container), owner $($fixture.OwnerKey)."
    Write-Output "Verified fixture resources: $($instanceResource.Entry.Key) ($($instanceResource.Entry.Sha256)), $($modelResource.Entry.Key) ($($modelResource.Entry.Sha256))."
    Write-Output "Output: $runLog"
}
finally {
    Pop-Location
}
