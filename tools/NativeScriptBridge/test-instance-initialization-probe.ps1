param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$positive = 'artifacts/export-verification/FF006BFC-8F0027AF-script-owner-fixture/manifest.json'
$negative = 'artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/manifest.json'
$extractor = Join-Path $PSScriptRoot 'extract-instance-initialization-slice.ps1'
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\instance-initialization-probe'
$exe = Join-Path $outputDirectory 'instance_initialization_probe.exe'

function Get-ProbeArguments {
    param([string] $ManifestPath)
    $fullPath = (Resolve-Path (Join-Path $repoRoot $ManifestPath)).Path
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    $manifest = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
    $fixture = $manifest.Fixture
    $selected = @{}
    foreach ($role in @('owner', 'instance', 'model', 'instance-variables', 'model-entry:.ova', 'model-entry:.ofc')) {
        $links = @($fixture.Links | Where-Object { $_.Role -eq $role })
        if ($links.Count -ne 1 -or $links[0].Status -ne 'Resolved') { throw "Fixture link '$role' is absent or unresolved." }
        $entries = @($fixture.Resources | Where-Object { $_.File -eq $links[0].File })
        if ($entries.Count -ne 1 -or $entries[0].Status -ne 'Resolved' -or $entries[0].Key -ne $links[0].Key) {
            throw "Fixture resource '$role' is absent or disagrees with its link."
        }
        $entry = $entries[0]
        $path = [System.IO.Path]::GetFullPath((Join-Path $directory $entry.File))
        if (-not $path.StartsWith($directory.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Fixture resource '$role' escapes its manifest directory."
        }
        if ((Get-Item -LiteralPath $path).Length -ne [int]$entry.Size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) {
            throw "Fixture resource '$role' failed size/SHA-256 verification."
        }
        $selected[$role] = [pscustomobject]@{ Link = $links[0]; Entry = $entry; Path = $path }
    }
    $numeric = @(
        ('0x' + [string]$selected['owner'].Link.Key),
        ('0x' + [string]$selected['instance'].Link.Key), ('0x{0:X8}' -f [uint32]$selected['instance'].Entry.Offset),
        ('0x' + [string]$selected['model'].Link.Key), ('0x{0:X8}' -f [uint32]$selected['model'].Entry.Offset),
        ('0x' + [string]$selected['instance-variables'].Link.Key), ('0x{0:X8}' -f [uint32]$selected['instance-variables'].Entry.Offset),
        ('0x' + [string]$selected['model-entry:.ova'].Link.Key), ('0x{0:X8}' -f [uint32]$selected['model-entry:.ova'].Entry.Offset),
        ('0x' + [string]$selected['model-entry:.ofc'].Link.Key), ('0x{0:X8}' -f [uint32]$selected['model-entry:.ofc'].Entry.Offset)
    )
    $paths = @(
        $selected['instance'].Path, $selected['model'].Path, $selected['instance-variables'].Path,
        $selected['model-entry:.ova'].Path, $selected['model-entry:.ofc'].Path
    )
    return ,(@($numeric) + @($paths))
}

function Assert-ExtractorRejects {
    param([string] $ManifestPath, [string] $Expected)
    $failure = ''
    try { & $extractor -ManifestPath $ManifestPath | Out-Null }
    catch { $failure = $_.Exception.Message }
    if ($failure -notlike "*$Expected*") { throw "Extractor negative case expected '$Expected'; got '$failure'." }
}

Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'build-instance-initialization-probe.ps1') -ManifestPath $positive | Out-Host
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Native initialization probe executable was not produced.' }
    $positiveLog = Join-Path $outputDirectory 'run-FF006BFC-8F0027AF.log'
    $positiveText = Get-Content -LiteralPath $positiveLog -Raw
    foreach ($expected in @(
        'INITIALIZATION=ResolveInstanceRef_then_ReinitInstance',
        'CALLBACK_SLOT_2=source_registry_pointer;not_executed',
        'MODEL_DEFAULTS=initial_and_current_buffers_match',
        'INSTANCE_OVA=empty_and_released',
        'INSTANCE_COUNT=1',
        'RESULT=bounded_source_instance_initialization_only'
    )) {
        if (-not $positiveText.Contains($expected)) { throw "Positive native run did not report '$expected'." }
    }

    $negativeArgs = Get-ProbeArguments -ManifestPath $negative
    $negativeOutput = & $exe @negativeArgs 2>&1
    $negativeExit = $LASTEXITCODE
    $negativeLog = Join-Path $outputDirectory 'run-FF001FBE-10001E0C-negative.log'
    $negativeOutput | Set-Content -LiteralPath $negativeLog -Encoding UTF8
    $negativeText = $negativeOutput -join [Environment]::NewLine
    if ($negativeExit -ne 5 -or -not $negativeText.Contains('UNSUPPORTED_NONNULL_GAO_REFERENCE') -or
        -not $negativeText.Contains('SOURCE_DISPATCH_COUNT=0')) {
        throw "Nonzero GAO fixture was not rejected before source dispatch (exit=$negativeExit): $negativeText"
    }

    $positiveFull = Join-Path $repoRoot $positive
    $fixtureDirectory = [System.IO.Path]::GetDirectoryName((Resolve-Path $positiveFull).Path)
    $hashTamperName = 'probe-bad-hash-{0}.json' -f $PID
    $hashTamperPath = Join-Path $fixtureDirectory $hashTamperName
    $hashTamper = Get-Content -LiteralPath $positiveFull -Raw | ConvertFrom-Json
    $functionResource = @($hashTamper.Fixture.Resources | Where-Object { $_.Key -eq 'C100B3C5' })
    if ($functionResource.Count -ne 1) { throw 'Positive manifest does not have a unique AIC resource for the tamper check.' }
    $functionResource[0].Sha256 = '0000000000000000000000000000000000000000000000000000000000000000'
    $hashTamper | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $hashTamperPath -Encoding UTF8
    try { Assert-ExtractorRejects -ManifestPath ('artifacts/export-verification/FF006BFC-8F0027AF-script-owner-fixture/' + $hashTamperName) -Expected 'failed manifest size/SHA-256 verification' }
    finally { Remove-Item -LiteralPath $hashTamperPath -Force -ErrorAction SilentlyContinue }

    $truncatedName = 'probe-truncated-instance-ova-{0}.bin' -f $PID
    $truncatedPath = Join-Path $fixtureDirectory $truncatedName
    $truncatedManifestName = 'probe-truncated-instance-ova-{0}.json' -f $PID
    $truncatedManifestPath = Join-Path $fixtureDirectory $truncatedManifestName
    $truncatedManifest = Get-Content -LiteralPath $positiveFull -Raw | ConvertFrom-Json
    [System.IO.File]::WriteAllBytes($truncatedPath, [byte[]]@())
    $emptyHash = (Get-FileHash -LiteralPath $truncatedPath -Algorithm SHA256).Hash
    $truncatedLink = @($truncatedManifest.Fixture.Links | Where-Object { $_.Role -eq 'instance-variables' })
    $truncatedResource = @($truncatedManifest.Fixture.Resources | Where-Object { $_.Key -eq $truncatedLink[0].Key })
    if ($truncatedLink.Count -ne 1 -or $truncatedResource.Count -ne 1) { throw 'Positive manifest has no unique instance OVA link/resource for truncation test.' }
    $truncatedLink[0].File = $truncatedName
    $truncatedResource[0].File = $truncatedName
    $truncatedResource[0].Size = 0
    $truncatedResource[0].Sha256 = $emptyHash
    $truncatedManifest | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $truncatedManifestPath -Encoding UTF8
    try {
        $truncatedArgs = Get-ProbeArguments -ManifestPath ('artifacts/export-verification/FF006BFC-8F0027AF-script-owner-fixture/' + $truncatedManifestName)
        $truncatedOutput = & $exe @truncatedArgs 2>&1
        $truncatedExit = $LASTEXITCODE
        $truncatedText = $truncatedOutput -join [Environment]::NewLine
        if ($truncatedExit -ne 5 -or -not $truncatedText.Contains('OVA payload is too short') -or
            -not $truncatedText.Contains('SOURCE_DISPATCH_COUNT=0')) {
            throw "Truncated OVA was not rejected before source dispatch (exit=$truncatedExit): $truncatedText"
        }
    }
    finally {
        Remove-Item -LiteralPath $truncatedManifestPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $truncatedPath -Force -ErrorAction SilentlyContinue
    }

    Write-Output 'Native instance initialization regression passed: FF006BFC positive; nonzero GAO, manifest hash, and truncated OVA fail closed.'
    Write-Output "Positive log: $positiveLog"
    Write-Output "Negative log: $negativeLog"
}
finally { Pop-Location }
