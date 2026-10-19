param(
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [string]$AssemblyPath = "$PSScriptRoot/../bin/VerifyStructural/JadeView.dll"
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$archive = [JadeView.Archive]::new($ArchivePath)
$entry = $archive.Entries | Where-Object Id -eq 'FF0057FD'
$bytes = $archive.Read($entry, [int]::MaxValue)
$doc = [JadeView.SceneLoader]::Load($bytes, $entry.Name, $archive.PathName, '')
$world = $doc.Worlds | Where-Object {$_.Objects.Name -contains 'DEC_murG_00_01.gao'} | Select-Object -First 1
$wall = $world.Objects | Where-Object Name -eq 'DEC_murG_00_01.gao' | Select-Object -First 1
if ($null -eq $wall) { throw 'Use a source BF in which DEC_murG_00_01.gao has not already been deleted' }
$project = [JadeView.ModProject]::new()
$project.RecordSceneObjectState($wall, $doc.ContainerName, $world.Key, $false)
$rebuilt = [JadeView.PcSceneWriter]::Rebuild($bytes, $doc, $project, $null)
$loader = [JadeView.SceneLoader].Assembly.GetType('JadeView.PcSceneLoader')
$read = $loader.GetMethod('Read', [Reflection.BindingFlags]'Static,Public,NonPublic')
$parsed = $read.Invoke($null, @($rebuilt, $archive.PathName))
$records = $parsed.GetType().GetField('Records').GetValue($parsed)
$worlds = $parsed.GetType().GetField('Worlds').GetValue($parsed)
$after = $worlds | Where-Object Key -eq $world.Key
if ($after.WorldMembers.Key -contains $wall.GaoKey) { throw 'Deleted barrier remains a world member' }
if ($records | Where-Object {$_.Kind -eq 'l' -and $_.Key -eq $wall.GaoKey}) {
    throw 'Cinematic or prefab references still reload the deleted barrier'
}
foreach ($record in $records) {
    foreach ($offset in $record.GaoReferenceOffsets) {
        if ([BitConverter]::ToUInt32($rebuilt, $offset) -eq $wall.GaoKey) {
            throw ('Deleted barrier remains referenced by {0:X8}' -f $record.Key)
        }
    }
}
# The crash dump shows this barrier's unowned g resource surviving an unload.
foreach ($key in @([Convert]::ToUInt32('8F001948',16), [Convert]::ToUInt32('8F001949',16))) {
    if ($records.Key -contains $key) { throw 'Deleted barrier animation remains in the load stream' }
}
foreach ($name in @('SCT_1.gao', 'SCT_2.gao')) {
    $controller = $after.WorldMembers | Where-Object Name -eq $name
    if ($null -eq $controller -or $controller.BrainFieldOffset -ge 0 -or
        ($controller.Flags -band 0x20) -ne 0 -or ($controller.References | Where-Object Kind -eq 'q')) {
        throw "Sector controller $name has an inconsistent behavior extension"
    }
}
# Gameplay and portal geometry must remain present; no global byte replacement.
$originalParsed = $read.Invoke($null, @($bytes, $archive.PathName))
$originalWorld = $originalParsed.GetType().GetField('Worlds').GetValue($originalParsed) | Where-Object Key -eq $world.Key
$affectedAnimations = @($originalParsed.Records | Where-Object {
    $_.Kind -eq 'i' -and @($_.AnimationChannels | Where-Object Item1 -eq $wall.GaoKey).Count -gt 0
})
if ($affectedAnimations.Count -eq 0) { throw 'Regression fixture has no cinematic references to the barrier' }
foreach ($beforeAnimation in $affectedAnimations) {
    $afterAnimation = $records | Where-Object { $_.Kind -eq 'i' -and $_.Key -eq $beforeAnimation.Key } | Select-Object -First 1
    if ($null -eq $afterAnimation) { throw 'A surviving cinematic resource was removed' }
    $keptChannels = @($beforeAnimation.AnimationChannels | Where-Object Item1 -ne $wall.GaoKey)
    if ($afterAnimation.AnimationChannels.Count -ne $keptChannels.Count) { throw 'Cinematic channel count differs' }
    for ($channelIndex = 0; $channelIndex -lt $keptChannels.Count; $channelIndex++) {
        $beforeRange = $keptChannels[$channelIndex].Item2
        $afterRange = $afterAnimation.AnimationChannels[$channelIndex].Item2
        if ([BitConverter]::ToString($bytes, $beforeRange.Offset, $beforeRange.Size) -ne
            [BitConverter]::ToString($rebuilt, $afterRange.Offset, $afterRange.Size)) {
            throw 'Deleting the barrier changed an unrelated cinematic channel'
        }
    }
}
foreach ($name in @('SCT_1.gao', 'SCT_2.gao')) {
    $beforeController = $originalWorld.WorldMembers | Where-Object Name -eq $name
    $afterController = $after.WorldMembers | Where-Object Name -eq $name
    if ($afterController.Size -ne $beforeController.Size - 4 -or
        $afterController.Flags -ne ($beforeController.Flags -band (-bnot 0x20)) -or
        $afterController.Prefab -ne $beforeController.Prefab) { throw 'Controller layout or prefab changed unexpectedly' }
}
# Re-emit the modified container again, not merely re-read it. No edits must
# produce identical bytes after the extension's size and dependency change.
$reloadedDoc = [JadeView.SceneLoader]::Load($rebuilt, $entry.Name, $archive.PathName, '')
$secondPass = [JadeView.PcSceneWriter]::Rebuild($rebuilt, $reloadedDoc, [JadeView.ModProject]::new(), $null)
if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rebuilt)) -ne
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($secondPass))) { throw 'Rebuilding the modified scene changed bytes' }
foreach ($name in @('OBJ_Gyro.gao', 'SCT_Porte_1_2.gao')) {
    $beforeRecord = $originalWorld.WorldMembers | Where-Object Name -eq $name
    $afterRecord = $after.WorldMembers | Where-Object Name -eq $name
    if ($null -eq $beforeRecord -or $null -eq $afterRecord -or
        [BitConverter]::ToString($bytes, $beforeRecord.Offset, $beforeRecord.Size) -ne
        [BitConverter]::ToString($rebuilt, $afterRecord.Offset, $afterRecord.Size)) { throw "$name was changed" }
}
Write-Output 'PASS: barrier and orphan animation absent; typed references cleared; controllers, pickup and portal verified'
Write-Output 'In-game rendering and script lifecycle require runtime verification.'
