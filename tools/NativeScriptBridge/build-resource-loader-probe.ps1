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

function Get-LittleEndianWordHex {
    param([byte[]] $Bytes, [int] $Offset)

    if ($Offset -lt 0 -or $Offset -gt ($Bytes.Length - 4)) {
        throw 'OVA callback/reference words are truncated.'
    }

    return [BitConverter]::ToUInt32($Bytes, $Offset).ToString('X8')
}

$instanceResource = Resolve-ManifestResource -Role 'instance'
$instanceVariablesResource = Resolve-ManifestResource -Role 'instance-variables'
$modelResource = Resolve-ManifestResource -Role 'model'
$modelVariablesResource = Resolve-ManifestResource -Role 'model-entry:.ova'
$modelFunctionResource = Resolve-ManifestResource -Role 'model-entry:.ofc'
$instanceOvaResource = $instanceVariablesResource
if ($instanceResource.Entry.Kind -ne 'q' -or
    $instanceVariablesResource.Entry.Kind -ne 't' -or
    $modelResource.Entry.Kind -ne 's' -or
    $modelVariablesResource.Entry.Kind -ne 't' -or
    $modelFunctionResource.Entry.Kind -ne 'r') {
    throw 'Manifest resource kinds do not match the bounded AIC/AIM reference chain.'
}
if ($instanceOvaResource.Entry.Kind -ne 't' -or
    $instanceOvaResource.Entry.OvaSummary.Status -ne 'DecodedStorage' -or
    $modelVariablesResource.Entry.OvaSummary.Status -ne 'DecodedStorage') {
    throw 'Manifest OVA summaries are missing or unresolved; refusing to run the native OVA callbacks.'
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
        'tools\NativeScriptBridge\resource_loader_probe_host.c',
        'tools\NativeScriptBridge\resource_loader_probe_ova.c',
        'tools\NativeScriptBridge\resource_loader_probe_report.c',
        'external\irisEng\ai\ai_types.c',
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
        $modelResource.Path,
        $instanceOvaResource.Path,
        $modelVariablesResource.Path
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
        'RESULT=source_AIC_AIM_OVA_resource_references_only'
    )
    $observedLines = @($outputLines | ForEach-Object { $_.ToString().Trim() })
    foreach ($expectedLine in $expectedLines) {
        if ($observedLines -notcontains $expectedLine) {
            throw "Native callback output did not match manifest link '$expectedLine'. Actual output: $($observedLines -join ', ')"
        }
    }

    foreach ($ovaCase in @(
        [pscustomobject]@{ Label = 'INSTANCE'; Resource = $instanceOvaResource },
        [pscustomobject]@{ Label = 'MODEL'; Resource = $modelVariablesResource }
    )) {
        $summary = $ovaCase.Resource.Entry.OvaSummary
        $beginPrefix = 'OVA_BEGIN={0}|{1}|{2}' -f $ovaCase.Label, $summary.DescriptorCount, $summary.StorageLength
        if ($observedLines -notcontains $beginPrefix) {
            throw "Native OVA callback descriptor/storage counts differ from C# summary for $($ovaCase.Label)."
        }
        $nativeStorageLine = @($observedLines | Where-Object { $_.StartsWith("OVA_STORAGE=$($ovaCase.Label)|", [System.StringComparison]::Ordinal) })
        $expectedStorageBuilder = [System.Text.StringBuilder]::new()
        foreach ($storageWord in $summary.UntypedStorageWordsLittleEndian) {
            $wordHex = [string]$storageWord
            if ($wordHex.Length -ne 8 -or $wordHex -notmatch '^[0-9A-Fa-f]{8}$') {
                throw "C# OVA storage summary contains an invalid 32-bit word for $($ovaCase.Label)."
            }
            for ($byteOffset = 6; $byteOffset -ge 0; $byteOffset -= 2) {
                [void]$expectedStorageBuilder.Append($wordHex.Substring($byteOffset, 2).ToUpperInvariant())
            }
        }
        $expectedStorage = $expectedStorageBuilder.ToString()
        if ($nativeStorageLine.Count -ne 1 -or $nativeStorageLine[0] -ne "OVA_STORAGE=$($ovaCase.Label)|$expectedStorage") {
            throw "Native OVA callback default storage differs from C# summary for $($ovaCase.Label)."
        }

        foreach ($descriptor in $summary.Descriptors) {
            $descriptorLine = @($observedLines | Where-Object {
                $_ -match ('^OVA_VAR=' + [regex]::Escape($ovaCase.Label) + '\|' + $descriptor.Index + '\|')
            })
            if ($descriptorLine.Count -ne 1) {
                throw "Native OVA callback did not report one descriptor index $($descriptor.Index) for $($ovaCase.Label)."
            }
            $parts = $descriptorLine[0].Substring('OVA_VAR='.Length).Split('|')
            $descriptorTail = [string]$descriptor.DescriptorTailHex
            $expectedFlags = [Convert]::ToUInt16($descriptorTail, 16)
            $valuePreview = [string]$descriptor.ValuePreview
            if ($valuePreview.StartsWith('0x', [System.StringComparison]::OrdinalIgnoreCase)) {
                $expectedValue = [Convert]::ToUInt32($valuePreview.Substring(2), 16)
            }
            else {
                $expectedValue = [uint32]::Parse($valuePreview, [System.Globalization.CultureInfo]::InvariantCulture)
            }
            if ($parts[2] -ne $descriptor.Name -or
                [int]$parts[3] -ne [int]$descriptor.Type -or
                [int]$parts[4] -ne [int]$descriptor.StorageOffset -or
                [uint32]$parts[5] -ne [Convert]::ToUInt32([string]$descriptor.PackedArrayCount, 16) -or
                [Convert]::ToUInt16($parts[6], 16) -ne $expectedFlags -or
                [Convert]::ToUInt32($parts[7], 16) -ne $expectedValue) {
                throw "Native OVA descriptor/default value differs from C# summary for $($ovaCase.Label) index $($descriptor.Index)."
            }
        }

        $ovaBytes = [System.IO.File]::ReadAllBytes($ovaCase.Resource.Path)
        $callbackStart = [int]$summary.StorageStart + [int]$summary.StorageLength
        $expectedCallbacks = @()
        for ($slotIndex = 0; $slotIndex -lt 5; $slotIndex++) {
            $callbackKey = Get-LittleEndianWordHex -Bytes $ovaBytes -Offset ($callbackStart + ($slotIndex * 4))
            if ($callbackKey -ne '00000000' -and $callbackKey -ne 'FFFFFFFF') {
                $expectedCallbacks += [pscustomobject]@{ Slot = $slotIndex; Key = $callbackKey; IsInstance = $false }
            }
        }

        $functionListStart = $callbackStart + 20
        if ($functionListStart -lt $ovaBytes.Length) {
            $instanceFunctionCount = [Convert]::ToUInt32((Get-LittleEndianWordHex -Bytes $ovaBytes -Offset $functionListStart), 16)
            if ($instanceFunctionCount -gt 4096 -or ($functionListStart + 4 + (4 * $instanceFunctionCount)) -ne $ovaBytes.Length) {
                throw "OVA instance-function list is malformed for $($ovaCase.Label)."
            }
            for ($functionIndex = 0; $functionIndex -lt $instanceFunctionCount; $functionIndex++) {
                $functionKey = Get-LittleEndianWordHex -Bytes $ovaBytes -Offset ($functionListStart + 4 + (4 * $functionIndex))
                if ($functionKey -ne '00000000' -and $functionKey -ne 'FFFFFFFF') {
                    $expectedCallbacks += [pscustomobject]@{ Slot = $functionIndex; Key = $functionKey; IsInstance = $true }
                }
            }
        }

        $actualCallbacks = @($observedLines | Where-Object {
            $_.StartsWith("OVA_CALLBACK=$($ovaCase.Label)|", [System.StringComparison]::Ordinal)
        } | ForEach-Object {
            $parts = $_.Substring('OVA_CALLBACK='.Length).Split('|')
            [pscustomobject]@{ Slot = [int]$parts[1]; Key = $parts[2]; Flags = [int]$parts[3]; IsInstance = $false }
        })
        $actualInstanceFunctions = @($observedLines | Where-Object {
            $_.StartsWith("OVA_INSTANCE_FUNCTION=$($ovaCase.Label)|", [System.StringComparison]::Ordinal)
        } | ForEach-Object {
            $parts = $_.Substring('OVA_INSTANCE_FUNCTION='.Length).Split('|')
            [pscustomobject]@{ Slot = [int]$parts[1]; Key = $parts[2]; Flags = [int]$parts[3]; IsInstance = $true }
        })
        $allActualReferences = @($actualCallbacks) + @($actualInstanceFunctions)
        if ($allActualReferences.Count -ne $expectedCallbacks.Count) {
            throw "Native OVA callback reference count differs from raw source fields for $($ovaCase.Label)."
        }
        foreach ($expectedCallback in $expectedCallbacks) {
            $matchingReference = @($allActualReferences | Where-Object {
                $_.Slot -eq $expectedCallback.Slot -and $_.Key -eq $expectedCallback.Key -and $_.IsInstance -eq $expectedCallback.IsInstance -and $_.Flags -eq 1
            })
            if ($matchingReference.Count -ne 1) {
                throw "Native OVA callback key/slot/flag differs from raw source for $($ovaCase.Label), slot $($expectedCallback.Slot)."
            }
        }
    }

    Write-Output "Native loader reference probe passed for container $($manifest.Container), owner $($fixture.OwnerKey)."
    Write-Output "Verified fixture resources: $($instanceResource.Entry.Key) ($($instanceResource.Entry.Sha256)), $($modelResource.Entry.Key) ($($modelResource.Entry.Sha256))."
    Write-Output "Output: $runLog"
}
finally {
    Pop-Location
}
