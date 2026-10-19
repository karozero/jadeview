param(
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [string]$AssemblyPath = "$PSScriptRoot/../bin/VerifyStructural/JadeView.dll"
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$archive = [JadeView.Archive]::new($ArchivePath)
$entry = $archive.Entries | Where-Object Id -eq 'FF0057FD'
$bytes = $archive.Read($entry, [int]::MaxValue)
$document = [JadeView.SceneLoader]::Load($bytes, $entry.Name, $archive.PathName, '')
$world = $document.Worlds | Where-Object {$_.Objects.Name -contains 'OBJ_Gyro.gao'} | Select-Object -First 1
$source = $world.Objects | Where-Object Name -eq 'OBJ_Gyro.gao' | Select-Object -First 1
if ($null -eq $source) { throw 'OBJ_Gyro.gao was not found in the requested world' }
$project = [JadeView.ModProject]::new()
$keys = @($source.GaoKey)
foreach ($copyIndex in 1..2) {
    $copy = [JadeView.SceneStructureChange]::Added($source, $document.ContainerName, $world.Key)
    $copy.ObjectId = "editor-pickup-test-$copyIndex"
    $copy.GaoKey = $document.AllocateObjectKey()
    $keys += $copy.GaoKey
    $copy.SourceObjectId = $source.Id
    $copy.Name = "OBJ_Gyro.gao Test Copy $copyIndex"
    $copy.Transform.Tx += 2 * $copyIndex
    $project.SceneStructureChanges.Add($copy)
}
$rebuilt = [JadeView.PcSceneWriter]::Rebuild($bytes, $document, $project, $null)
$type = [JadeView.SceneLoader].Assembly.GetType('JadeView.PcSceneLoader')
$read = $type.GetMethod('Read', [Reflection.BindingFlags]'Static,Public,NonPublic')
$parsed = $read.Invoke($null, @($rebuilt, $archive.PathName))
$records = $parsed.GetType().GetField('Records').GetValue($parsed)
foreach ($controller in $records | Where-Object {$_.Name -in @('SCT_1.gao', 'SCT_2.gao')}) {
    if ($controller.BrainFieldOffset -ge 0 -and
        [BitConverter]::ToUInt32($rebuilt, $controller.BrainFieldOffset) -eq [uint32]::MaxValue) {
        throw 'Adding a pickup retained a legacy null-brain controller'
    }
}
$brains = @(); $variables = @(); $models = @(); $pickupFlags = @{}
function Read-PickupFlag($record, [byte[]]$buffer, [string]$fieldName) {
    $descriptorBytes = [BitConverter]::ToInt32($buffer, $record.Offset)
    $count = $descriptorBytes / 12
    $namesStart = $record.Offset + 8 + $descriptorBytes
    $valuesStart = $namesStart + $count * 30 + 4
    for ($descriptorIndex = 0; $descriptorIndex -lt $count; $descriptorIndex++) {
        $name = [Text.Encoding]::Latin1.GetString($buffer, $namesStart + $descriptorIndex * 30, 30).Split([char]0)[0]
        if ($name -eq $fieldName) {
            return [BitConverter]::ToInt32($buffer, $valuesStart + $descriptorIndex * 4)
        }
    }
    throw "Missing pickup variable $fieldName"
}
foreach ($key in $keys) {
    $gao = $records | Where-Object {$_.Key -eq $key -and $_.Kind -eq 'l'} | Select-Object -First 1
    $brainKey = ($gao.References | Where-Object Kind -eq 'q').Key
    $brains += $brainKey
    $brain = $records | Where-Object {$_.Key -eq $brainKey -and $_.Kind -eq 'q'} | Select-Object -First 1
    $variableKey = ($brain.References | Where-Object Kind -eq 't').Key
    $variables += $variableKey
    $models += ($brain.References | Where-Object Kind -eq 's').Key
    $variableRecord = $records | Where-Object {$_.Key -eq $variableKey -and $_.Kind -eq 't'} | Select-Object -First 1
    $activeFlag = Read-PickupFlag $variableRecord $rebuilt 'des_SF_active_on_destroy'
    $loadFlag = Read-PickupFlag $variableRecord $rebuilt 'des_SF_destroy_on_load'
    if ($activeFlag -le 0 -or $activeFlag -ne $loadFlag) { throw 'Pickup has mismatched activation/load flags' }
    $pickupFlags[$key] = $activeFlag
}
if (($brains | Sort-Object -Unique).Count -ne 3) { throw 'Pickup objects share behavior-instance keys' }
if (($variables | Sort-Object -Unique).Count -ne 3) { throw 'Pickup objects share variable-block keys' }
if (($models | Sort-Object -Unique).Count -ne 1) { throw 'Duplicating a pickup changed its behavior model' }
if (($pickupFlags.Values | Sort-Object -Unique).Count -ne 3) { throw 'Pickups share persistent story flags' }
if ($pickupFlags[$source.GaoKey] -ne 771) { throw 'Original pickup story flag changed' }

# Replay the observed native toggle/load-bit semantics for each possible collected subset.
# In particular, collecting the original and one copy must not clear either pickup's bit.
for ($subset = 0; $subset -lt 8; $subset++) {
    $savedBits = [Collections.Generic.HashSet[int]]::new()
    for ($pickupIndex = 0; $pickupIndex -lt $keys.Count; $pickupIndex++) {
        if (($subset -band (1 -shl $pickupIndex)) -eq 0) { continue }
        $flag = $pickupFlags[$keys[$pickupIndex]]
        if (-not $savedBits.Add($flag)) { $null = $savedBits.Remove($flag) }
    }
    for ($pickupIndex = 0; $pickupIndex -lt $keys.Count; $pickupIndex++) {
        $wasCollected = ($subset -band (1 -shl $pickupIndex)) -ne 0
        if ($savedBits.Contains($pickupFlags[$keys[$pickupIndex]]) -ne $wasCollected) {
            throw "Pickup reload state differs for subset $subset, object $pickupIndex"
        }
    }
}

$restoredProject = [JadeView.ModProject]::FromJson($project.ToJson())
$second = [JadeView.PcSceneWriter]::Rebuild($bytes, $document, $restoredProject, $null)
if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($second)) -ne
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rebuilt))) {
    throw 'Project save/load reassigned pickup flags or changed the exported scene'
}
$removedId = $restoredProject.SceneStructureChanges[0].ObjectId
$removedFlag = $restoredProject.PickupSaveFlags[$removedId]
$restoredProject.SceneStructureChanges.RemoveAt(0)
$null = [JadeView.PcSceneWriter]::Rebuild($bytes, $document, $restoredProject, $null)
if ($restoredProject.PickupSaveFlags[$removedId] -ne $removedFlag) { throw 'Deleted pickup released a save-owned flag' }
foreach ($assignment in $project.PickupSaveFlags.GetEnumerator()) {
    if ($restoredProject.PickupSaveFlags[$assignment.Key] -ne $assignment.Value) { throw 'Removing a copy reassigned persistence' }
}
$conflicting = [JadeView.ModProject]::FromJson($project.ToJson())
$conflicting.PickupSaveFlags[$conflicting.SceneStructureChanges[0].ObjectId] = 771
$rejected = $false
try {
    $null = [JadeView.PcSceneWriter]::Rebuild($bytes, $document, $conflicting, $null)
} catch {
    $rejected = $_.Exception.ToString().Contains('conflicts with the source archive')
}
if (-not $rejected) { throw 'Exporter accepted a conflicting pickup save flag' }
Write-Output 'PASS: OBJ_Gyro original and two copies have independent behavior and variable keys; behavior model retained'
Write-Output 'PASS: independent save flags; all eight collected subsets reload correctly under native toggle semantics'
Write-Output 'PASS: flag assignments survive project round-trip and deletion without reassignment'
Write-Output 'PASS: conflicting story-flag assignments are rejected'
Write-Output 'Serialization and native-rule simulation only; transitions and save/load in the game still require runtime verification.'
