param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$probeScript = Join-Path $PSScriptRoot 'build-resource-loader-probe.ps1'
$fixtureManifests = @(
    'artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/manifest.json',
    'artifacts/export-verification/FF006BFC-8F0027AF-script-owner-fixture/manifest.json'
)

foreach ($fixtureManifest in $fixtureManifests) {
    & $probeScript -ManifestPath $fixtureManifest
    if ($LASTEXITCODE -ne 0) { throw "Native resource-loader probe failed for $fixtureManifest." }
}

function New-TemporaryFixture {
    param([string] $SourceManifest, [string] $DestinationName)

    $sourcePath = Join-Path $repoRoot $SourceManifest
    $sourceDirectory = [System.IO.Path]::GetDirectoryName($sourcePath)
    $destinationDirectory = Join-Path $repoRoot "artifacts\native-script-bridge\negative-tests\$DestinationName"
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    $manifest = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
    foreach ($resource in $manifest.Fixture.Resources) {
        if ($resource.Status -ne 'Resolved') { continue }
        $sourceResource = Join-Path $sourceDirectory $resource.File
        Copy-Item -LiteralPath $sourceResource -Destination (Join-Path $destinationDirectory $resource.File) -Force
    }

    $destinationManifest = Join-Path $destinationDirectory 'manifest.json'
    $manifestJson = ConvertTo-Json -InputObject $manifest -Depth 20
    [System.IO.File]::WriteAllText($destinationManifest, $manifestJson, [System.Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{
        Manifest = $manifest
        Directory = $destinationDirectory
        ManifestRelativePath = $destinationManifest.Substring($repoRoot.Length + 1)
    }
}

function Assert-ProbeFailsWith {
    param([string] $ManifestRelativePath, [string] $ExpectedText)

    $failedAsExpected = $false
    $failureText = ''
    try {
        $output = & $probeScript -ManifestPath $ManifestRelativePath 2>&1
        $processExitCode = $LASTEXITCODE
        if ($processExitCode -ne 0) {
            $failedAsExpected = $true
            $failureText = $output -join [Environment]::NewLine
        }
    }
    catch {
        $failedAsExpected = $true
        $failureText = $_.ToString()
    }
    if (-not $failedAsExpected -or $failureText -notmatch [regex]::Escape($ExpectedText)) {
        throw "Expected probe failure '$ExpectedText'; output=$failureText"
    }
}

$hashFixture = New-TemporaryFixture -SourceManifest $fixtureManifests[0] -DestinationName 'hash-mismatch'
$instanceLink = @($hashFixture.Manifest.Fixture.Links | Where-Object { $_.Role -eq 'instance' })[0]
$instanceResource = @($hashFixture.Manifest.Fixture.Resources | Where-Object { $_.File -eq $instanceLink.File })[0]
$instancePath = Join-Path $hashFixture.Directory $instanceResource.File
$instanceBytes = [System.IO.File]::ReadAllBytes($instancePath)
$instanceBytes[0] = $instanceBytes[0] -bxor 0x01
[System.IO.File]::WriteAllBytes($instancePath, $instanceBytes)
Assert-ProbeFailsWith -ManifestRelativePath $hashFixture.ManifestRelativePath -ExpectedText 'integrity check failed'

$truncatedFixture = New-TemporaryFixture -SourceManifest $fixtureManifests[0] -DestinationName 'truncated-instance'
$truncatedLink = @($truncatedFixture.Manifest.Fixture.Links | Where-Object { $_.Role -eq 'instance' })[0]
$truncatedResource = @($truncatedFixture.Manifest.Fixture.Resources | Where-Object { $_.File -eq $truncatedLink.File })[0]
$truncatedPath = Join-Path $truncatedFixture.Directory $truncatedResource.File
$truncatedBytes = [System.IO.File]::ReadAllBytes($truncatedPath)
$shortBytes = [byte[]]::new($truncatedBytes.Length - 1)
[Array]::Copy($truncatedBytes, $shortBytes, $shortBytes.Length)
[System.IO.File]::WriteAllBytes($truncatedPath, $shortBytes)
$truncatedResource.Size = $shortBytes.Length
$truncatedResource.Sha256 = (Get-FileHash -LiteralPath $truncatedPath -Algorithm SHA256).Hash
$truncatedManifestPath = Join-Path $truncatedFixture.Directory 'manifest.json'
$truncatedJson = ConvertTo-Json -InputObject $truncatedFixture.Manifest -Depth 20
[System.IO.File]::WriteAllText($truncatedManifestPath, $truncatedJson, [System.Text.UTF8Encoding]::new($false))
Assert-ProbeFailsWith -ManifestRelativePath $truncatedFixture.ManifestRelativePath -ExpectedText 'AIC/AIM manifest payload lengths'

$truncatedOvaFixture = New-TemporaryFixture -SourceManifest $fixtureManifests[0] -DestinationName 'truncated-ova'
$truncatedOvaLink = @($truncatedOvaFixture.Manifest.Fixture.Links | Where-Object { $_.Role -eq 'instance-variables' })[0]
$truncatedOvaResource = @($truncatedOvaFixture.Manifest.Fixture.Resources | Where-Object { $_.File -eq $truncatedOvaLink.File })[0]
$truncatedOvaPath = Join-Path $truncatedOvaFixture.Directory $truncatedOvaResource.File
$truncatedOvaBytes = [System.IO.File]::ReadAllBytes($truncatedOvaPath)
$shortOvaBytes = [byte[]]::new($truncatedOvaBytes.Length - 1)
[Array]::Copy($truncatedOvaBytes, $shortOvaBytes, $shortOvaBytes.Length)
[System.IO.File]::WriteAllBytes($truncatedOvaPath, $shortOvaBytes)
$truncatedOvaResource.Size = $shortOvaBytes.Length
$truncatedOvaResource.Sha256 = (Get-FileHash -LiteralPath $truncatedOvaPath -Algorithm SHA256).Hash
$truncatedOvaManifestPath = Join-Path $truncatedOvaFixture.Directory 'manifest.json'
$truncatedOvaJson = ConvertTo-Json -InputObject $truncatedOvaFixture.Manifest -Depth 20
[System.IO.File]::WriteAllText($truncatedOvaManifestPath, $truncatedOvaJson, [System.Text.UTF8Encoding]::new($false))
Assert-ProbeFailsWith -ManifestRelativePath $truncatedOvaFixture.ManifestRelativePath -ExpectedText 'OVA resource is malformed or uses an unsupported compact type/layout'

$unknownTypeFixture = New-TemporaryFixture -SourceManifest $fixtureManifests[0] -DestinationName 'unknown-ova-type'
$unknownTypeLink = @($unknownTypeFixture.Manifest.Fixture.Links | Where-Object { $_.Role -eq 'model-entry:.ova' })[0]
$unknownTypeResource = @($unknownTypeFixture.Manifest.Fixture.Resources | Where-Object { $_.File -eq $unknownTypeLink.File })[0]
$unknownTypePath = Join-Path $unknownTypeFixture.Directory $unknownTypeResource.File
$unknownTypeBytes = [System.IO.File]::ReadAllBytes($unknownTypePath)
# First descriptor begins at byte 4; its serialized type is at descriptor offset 8.
$unknownTypeBytes[12] = 0xFF
$unknownTypeBytes[13] = 0xFF
[System.IO.File]::WriteAllBytes($unknownTypePath, $unknownTypeBytes)
$unknownTypeResource.Size = $unknownTypeBytes.Length
$unknownTypeResource.Sha256 = (Get-FileHash -LiteralPath $unknownTypePath -Algorithm SHA256).Hash
# Keep the prior summary intentionally: rejection must come from native payload preflight.
$unknownTypeManifestPath = Join-Path $unknownTypeFixture.Directory 'manifest.json'
$unknownTypeJson = ConvertTo-Json -InputObject $unknownTypeFixture.Manifest -Depth 20
[System.IO.File]::WriteAllText($unknownTypeManifestPath, $unknownTypeJson, [System.Text.UTF8Encoding]::new($false))
Assert-ProbeFailsWith -ManifestRelativePath $unknownTypeFixture.ManifestRelativePath -ExpectedText 'OVA resource is malformed or uses an unsupported compact type/layout'

Write-Output 'Native resource-loader probe: both fixtures passed; hash mismatch, truncated AIC, truncated OVA, and unknown OVA type were rejected.'
