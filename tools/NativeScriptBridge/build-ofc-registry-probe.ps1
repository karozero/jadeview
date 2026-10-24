param(
    [string] $ManifestPath = 'artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/manifest.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestFullPath = (Resolve-Path (Join-Path $repoRoot $ManifestPath)).Path
$manifestDirectory = [System.IO.Path]::GetDirectoryName($manifestFullPath)
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$fixture = $manifest.Fixture
$functionLinks = @($fixture.Links | Where-Object { $_.Role -eq 'model-entry:.ofc' })
if ($functionLinks.Count -ne 1 -or $functionLinks[0].Status -ne 'Resolved') {
    throw 'Expected one resolved model OFC link in the selected manifest.'
}

$functionResources = @($fixture.Resources | Where-Object { $_.File -eq $functionLinks[0].File })
if ($functionResources.Count -ne 1 -or $functionResources[0].Status -ne 'Resolved' -or $functionResources[0].Kind -ne 'r') {
    throw 'The selected OFC link does not resolve to one resource of kind r.'
}
$functionResource = $functionResources[0]
$resourcePath = [System.IO.Path]::GetFullPath((Join-Path $manifestDirectory $functionResource.File))
$manifestPrefix = $manifestDirectory.TrimEnd('\') + '\'
if (-not $resourcePath.StartsWith($manifestPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The selected OFC resource path escapes its fixture directory.'
}
$resourceBytes = [System.IO.File]::ReadAllBytes($resourcePath)
if ($resourceBytes.Length -ne [int]$functionResource.Size -or
    (Get-FileHash -LiteralPath $resourcePath -Algorithm SHA256).Hash -ne $functionResource.Sha256) {
    throw 'The selected OFC resource failed manifest size/SHA-256 verification.'
}
if ($resourceBytes.Length -ne 12 -or
    [BitConverter]::ToUInt32($resourceBytes, 4) -eq 0 -or
    ([BitConverter]::ToUInt32($resourceBytes, 4) % 8) -ne 0 -or
    [BitConverter]::ToUInt32($resourceBytes, 8) -ne 0) {
    throw 'The probe requires a 12-byte compact metadata header, aligned nonzero node-byte count and no local string data.'
}

$compilerEnvironment = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat'
if (-not (Test-Path -LiteralPath $compilerEnvironment)) {
    throw "x86 MSVC environment was not found: $compilerEnvironment"
}
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\ofc-registry-probe'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$runLog = Join-Path $outputDirectory ('run-{0}-{1}.log' -f $manifest.Container, $fixture.OwnerKey)

Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'extract-ofc-registry-slice.ps1') -ManifestPath $ManifestPath | Out-Host
    $sourceManifestPath = Join-Path $outputDirectory 'ofc_registry_probe_manifest.json'
    $sourceManifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json

    $compilerLine = @(
        'call', "`"$compilerEnvironment`"", '>nul', '&&',
        'cl', '/nologo', '/TC', '/W3', '/Gy', '/O1', '/MD',
        '/Iexternal\jaded\src\dlls',
        '/Iexternal\jaded\src\dlls\BINK',
        '/Iexternal\jaded\src\libraries\XenonMeshProcessing\Sources',
        '/Foartifacts\native-script-bridge\ofc-registry-probe\',
        '/Fe:artifacts\native-script-bridge\ofc-registry-probe\ofc_registry_probe.exe',
        'tools\NativeScriptBridge\ofc_registry_probe_main.c',
        'tools\NativeScriptBridge\ofc_registry_probe_host.c',
        'tools\NativeScriptBridge\ofc_registry_probe_loader.c',
        'artifacts\native-script-bridge\ofc-registry-probe\generated_ofc_registry_row.c',
        'external\irisEng\ai\ai_bge\ai_compiledscripts_activateur.c',
        '/link', '/OPT:REF', '/INCREMENTAL:NO'
    ) -join ' '
    & $env:ComSpec /d /c $compilerLine
    if ($LASTEXITCODE -ne 0) {
        throw "OFC registry probe failed to compile/link with exit code $LASTEXITCODE."
    }

    $functionKey = [Convert]::ToUInt32([string]$functionLinks[0].Key, 16)
    $resourceOffset = [uint32]$functionResource.Offset
    $outputLines = & (Join-Path $outputDirectory 'ofc_registry_probe.exe') `
        ('0x{0:X8}' -f $functionKey) ('0x{0:X8}' -f $resourceOffset) $resourcePath 2>&1
    $exitCode = $LASTEXITCODE
    $outputLines | Set-Content -LiteralPath $runLog -Encoding UTF8
    if ($exitCode -ne 0) {
        throw "OFC source loader probe exited ${exitCode}: $($outputLines -join [Environment]::NewLine)"
    }

    $expectedLines = @(
        ('OFC_KEY=0x{0:X8}' -f $functionKey),
        ('OFC_HEADER=stack:{0},node_bytes:{1},local_data_bytes:{2}' -f
            [BitConverter]::ToUInt32($resourceBytes, 0),
            [BitConverter]::ToUInt32($resourceBytes, 4),
            [BitConverter]::ToUInt32($resourceBytes, 8)),
        ('REGISTRY_SYMBOL=' + [string]$sourceManifest.Registry.Symbol),
        'CALLBACK_POINTER_MATCH=1',
        'CACHE_USAGE=1->2',
        'LOA_SENTINEL_KEYS=0,FFFFFFFF->clear_and_no_enqueue',
        'RESULT=source_OFC_metadata_and_callback_identity_only',
        'LIMIT=callback_not_executed;address_cache_is_controlled_harness_adapter'
    )
    $observedLines = @($outputLines | ForEach-Object { $_.ToString().Trim() })
    foreach ($expectedLine in $expectedLines) {
        if ($observedLines -notcontains $expectedLine) {
            throw "Native output did not verify expected OFC result '$expectedLine'. Actual: $($observedLines -join ', ')"
        }
    }

    Write-Output "OFC metadata + source callback pointer identity probe passed for $($manifest.Container)/$($fixture.OwnerKey)."
    Write-Output "Resource: $($functionResource.Key) ($($functionResource.Sha256)); callback body was linked but not invoked."
    Write-Output "Output: $runLog"
}
finally {
    Pop-Location
}
