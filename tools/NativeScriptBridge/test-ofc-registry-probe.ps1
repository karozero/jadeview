param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\ofc-registry-probe'
$fixturePaths = @(
    'artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/manifest.json',
    'artifacts/export-verification/FF006BFC-8F0027AF-script-owner-fixture/manifest.json'
)
$extractorPath = Join-Path $PSScriptRoot 'extract-ofc-registry-slice.ps1'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

foreach ($fixturePath in $fixturePaths) {
    & (Join-Path $PSScriptRoot 'build-ofc-registry-probe.ps1') -ManifestPath $fixturePath | Out-Host
}

function Write-NegativeManifest {
    param(
        [string] $SourcePath,
        [string] $OutputName,
        [scriptblock] $Mutate
    )

    $sourceFullPath = Join-Path $repoRoot $SourcePath
    $manifestObject = Get-Content -LiteralPath $sourceFullPath -Raw | ConvertFrom-Json
    & $Mutate $manifestObject
    $sourceDirectory = [System.IO.Path]::GetDirectoryName($sourceFullPath)
    $outputPath = Join-Path $sourceDirectory $OutputName
    $manifestObject | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $outputPath -Encoding UTF8
    return $outputPath.Substring($repoRoot.Length + 1).Replace('\', '/')
}

function Assert-ExtractorRejects {
    param([string] $ManifestPath, [string] $ExpectedText)

    $failureText = ''
    try {
        & $extractorPath -ManifestPath $ManifestPath | Out-Null
    }
    catch {
        $failureText = $_.Exception.Message
    }
    if ($failureText -notlike "*$ExpectedText*") {
        throw "Extractor negative case '$ManifestPath' did not fail with expected diagnostic '$ExpectedText'. Got '$failureText'."
    }
}

$unknownRegistryPath = Write-NegativeManifest `
    -SourcePath $fixturePaths[0] `
    -OutputName 'unknown-registry-key.json' `
    -Mutate {
        param($manifestObject)
        $functionLink = @($manifestObject.Fixture.Links | Where-Object { $_.Role -eq 'model-entry:.ofc' })
        if ($functionLink.Count -ne 1) { throw 'Fixture manifest has no unique OFC link for negative test.' }
        $functionLink[0].Key = 'DEADBEEF'
    }
Assert-ExtractorRejects -ManifestPath $unknownRegistryPath -ExpectedText 'does not have exactly one source registry row'

$badHashPath = Write-NegativeManifest `
    -SourcePath $fixturePaths[0] `
    -OutputName 'bad-ofc-hash.json' `
    -Mutate {
        param($manifestObject)
        $functionLink = @($manifestObject.Fixture.Links | Where-Object { $_.Role -eq 'model-entry:.ofc' })
        $functionResource = @($manifestObject.Fixture.Resources | Where-Object { $_.File -eq $functionLink[0].File })
        if ($functionResource.Count -ne 1) { throw 'Fixture manifest has no unique OFC resource for negative test.' }
        $functionResource[0].Sha256 = '0000000000000000000000000000000000000000000000000000000000000000'
    }
Assert-ExtractorRejects -ManifestPath $badHashPath -ExpectedText 'does not match the manifest size and SHA-256'

Write-Output 'OFC registry probe regression passed: two fixtures and fail-closed unknown-key/hash cases.'
Write-Output "Logs: $outputDirectory"
