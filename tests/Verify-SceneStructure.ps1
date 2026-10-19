param(
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [string]$AssemblyPath = "$PSScriptRoot/../bin/VerifyStructural/JadeView.dll",
    [string]$SceneId = 'FF007685',
    [switch]$BuildArchive,
    [switch]$AllNoOp
)

$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$archive = [JadeView.Archive]::new($ArchivePath)
if ($AllNoOp) {
    $checked = 0
    foreach ($candidate in $archive.Entries | Where-Object {$_.Id.StartsWith('FF0')}) {
        $raw = $archive.Read($candidate, [int]::MaxValue)
        $scene = [JadeView.SceneDocument]::new()
        $scene.ContainerName = $candidate.Name
        $scene.BaseBfPath = $archive.PathName
        $out = [JadeView.PcSceneWriter]::Rebuild($raw, $scene, [JadeView.ModProject]::new(), $null)
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw)) -ne
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($out))) { throw "No-op changed $($candidate.Id)" }
        $checked++
    }
    Write-Output "PASS: $checked FF0 containers rebuilt byte-identically"
    return
}
$entry = $archive.Entries | Where-Object Id -eq $SceneId
if ($null -eq $entry) { throw "Scene $SceneId not found" }
$bytes = $archive.Read($entry, [int]::MaxValue)
$doc = [JadeView.SceneLoader]::Load($bytes, $entry.Name, $archive.PathName, '')
$emptyProject = [JadeView.ModProject]::new()
$unchanged = [JadeView.PcSceneWriter]::Rebuild($bytes, $doc, $emptyProject, $null)
if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -ne
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($unchanged))) { throw 'No-op rebuild changed bytes' }
Write-Output 'PASS: no-op FF0 rebuild is byte-identical'

$resolver = [JadeView.ResourceResolver]::new()
$null = $resolver.LoadFixContainer($archive)
$null = $resolver.LoadSceneContainer($archive, $entry)
$world = $doc.Worlds | Where-Object {$_.Name.Length -gt 0 -and [char]::IsDigit($_.Name[0])} |
    Sort-Object {$_.Objects.Count} -Descending | Select-Object -First 1
if ($null -eq $world) { throw 'No level world found' }
$source = $world.Objects | Where-Object {
    $null -eq $_.Actor -and $_.Capabilities.CanTranslate -and $resolver.Geometries.ContainsKey($_.GeometryKey)
} | Select-Object -First 1
if ($null -eq $source) { throw 'No editable source object with resolved geometry found' }
$project = [JadeView.ModProject]::new()
$project.ContainerName = $doc.ContainerName
$project.RecordSceneObjectState($source, $doc.ContainerName, $world.Key, $false)
$copy = [JadeView.SceneStructureChange]::Added($source, $doc.ContainerName, $world.Key)
$copy.ObjectId = 'editor-structural-test-copy'
$copy.GaoKey = [Convert]::ToUInt32('FEFFFFFE', 16)
$copy.Name += ' Copy'
$copy.SourceObjectId = $source.Id
$copy.Transform.Tx += 2
$copy.Transform.Sx *= 1.25
$copy.Transform.Sy *= 1.25
$copy.Transform.Sz *= 1.25
$project.SceneStructureChanges.Add($copy)
$plainCopy = [JadeView.SceneStructureChange]::Added($source, $doc.ContainerName, $world.Key)
$plainCopy.ObjectId = 'editor-structural-test-plain-copy'
$plainCopy.GaoKey = [Convert]::ToUInt32('FEFFFFFC', 16)
$plainCopy.Name += ' Plain Copy'
$plainCopy.SourceObjectId = $source.Id
$plainCopy.Transform.Tx += 6
$project.SceneStructureChanges.Add($plainCopy)

$moved = $world.Objects | Where-Object {
    $_.Id -ne $source.Id -and $null -eq $_.Actor -and $_.Capabilities.CanTranslate
} | Select-Object -First 1
if ($null -eq $moved) { throw 'No second static object for combined transform test' }
$oldTransform = $moved.Transform.Clone()
$newTransform = $oldTransform.Clone()
$newTransform.Translation = [Numerics.Vector3]::Add($newTransform.Translation, [Numerics.Vector3]::new(3,0,0))
$null = $project.RecordChange($moved, $doc.ContainerName, $world.Key, $oldTransform, $newTransform, $true)

# Add an asset from FIX, independently of an existing level GAO.
$geometry = $resolver.Geometries.Values | Where-Object {
    $_.Positions.Length -gt 0 -and $_.Skin.Length -eq 0 -and $null -eq $_.Particles -and
    $resolver.ResourceItems[$_.Key].Id.Scope -eq 'FIX' -and $_.Key -ne $source.GeometryKey -and
    $_.MaterialKey -notin @(0,[uint32]::MaxValue)
} | Select-Object -First 1
if ($null -eq $geometry) { throw 'No standalone FIX asset found for addition test' }
$added = [JadeView.SceneStructureChange]::new()
$added.ContainerName = $doc.ContainerName
$added.WorldKey = $world.Key
$added.ObjectId = 'editor-structural-test-add'
$added.GaoKey = [Convert]::ToUInt32('FEFFFFFD', 16)
$added.Name = 'Added FIX geometry'
$added.GeometryKey = $geometry.Key
$added.MaterialKey = $geometry.MaterialKey
$added.DrawFlags = [uint32]0x01000008
$added.Transform.Tx = $source.Transform.Translation.X + 4
$added.Transform.Ty = $source.Transform.Translation.Y
$added.Transform.Tz = $source.Transform.Translation.Z
$project.SceneStructureChanges.Add($added)

$tempProject = Join-Path ([IO.Path]::GetTempPath()) ("jade-structure-" + [Guid]::NewGuid().ToString('N') + '.bgemod')
$tempArchive = Join-Path ([IO.Path]::GetTempPath()) ("jade-structure-" + [Guid]::NewGuid().ToString('N') + '.bf')
try {
    $project.Save($tempProject)
    $loaded = [JadeView.ModProject]::Load($tempProject)
    $snapshot = $loaded.CreateSnapshot()
    ($loaded.SceneStructureChanges | Where-Object ObjectId -eq $copy.ObjectId).Transform.Tx += 100
    $plan = $snapshot.BuildWritePlan($doc, $bytes, $resolver)
    $rebuilt = [JadeView.SceneWriter]::CreatePatchedContainer($bytes, $plan)
    $roundtrip = [JadeView.SceneLoader]::Load($rebuilt, $entry.Name, $archive.PathName, '')
    $worldAfter = $roundtrip.Worlds | Where-Object Key -eq $world.Key
    for ($worldIndex = 0; $worldIndex -lt $doc.Worlds.Count; $worldIndex++) {
        $oldRange = $doc.Worlds[$worldIndex].RecordRange
        $newRange = $roundtrip.Worlds[$worldIndex].RecordRange
        if ($oldRange.Size -ne $newRange.Size -or
            [BitConverter]::ToString($bytes, $oldRange.Offset, $oldRange.Size) -ne
            [BitConverter]::ToString($rebuilt, $newRange.Offset, $newRange.Size)) { throw 'Rebuild changed a WOW record' }
    }
    if ($worldAfter.Objects.GaoKey -contains $source.GaoKey) { throw 'Deleted object is still present' }
    $copyAfter = $worldAfter.Objects | Where-Object GaoKey -eq $copy.GaoKey
    $plainAfter = $worldAfter.Objects | Where-Object GaoKey -eq $plainCopy.GaoKey
    $addAfter = $worldAfter.Objects | Where-Object GaoKey -eq $added.GaoKey
    if ($null -eq $copyAfter -or [Math]::Abs($copyAfter.Transform.Translation.X - $copy.Transform.Tx) -gt 1e-5) { throw 'Copy transform did not reload' }
    if ([Math]::Abs($copyAfter.Transform.Scale.X - $copy.Transform.Sx) -gt 1e-5) { throw 'Copy scale did not reload' }
    if ($null -eq $addAfter -or $addAfter.GeometryKey -ne $geometry.Key) { throw 'FIX geometry addition did not reload' }
    if ($null -eq $plainAfter -or $plainAfter.Flags -ne $source.Flags) { throw 'Unscaled copy flags did not reload' }
    if ($copyAfter.GeometryKey -eq $source.GeometryKey -or $plainAfter.GeometryKey -eq $source.GeometryKey -or
        $copyAfter.GeometryKey -eq $plainAfter.GeometryKey) { throw 'Duplicates share a GRO key' }
    $boundsSize = if (($source.Flags -band 0x80000) -ne 0) {48} else {24}
    $oldBounds = $bytes[($source.RecordRange.Offset+94)..($source.RecordRange.Offset+93+$boundsSize)]
    $newBounds = $rebuilt[($plainAfter.RecordRange.Offset+94)..($plainAfter.RecordRange.Offset+93+$boundsSize)]
    if ([BitConverter]::ToString($oldBounds) -ne [BitConverter]::ToString($newBounds)) { throw 'Unscaled copy changed source bounds' }
    $movedAfter = $worldAfter.Objects | Where-Object GaoKey -eq $moved.GaoKey
    if ($null -eq $movedAfter -or [Math]::Abs($movedAfter.Transform.Translation.X - $newTransform.Translation.X) -gt 1e-5) { throw 'Combined existing-object transform did not reload' }
    $roundtripCheck = [JadeView.SceneWriter]::VerifyRoundtrip($bytes, $rebuilt, $plan)
    if (-not $roundtripCheck.IsSuccess) { throw 'Structural roundtrip plan verification failed' }
    if ($worldAfter.Objects.Count -ne $world.Objects.Count + 2) { throw 'Unexpected object count' }
    # A BF built by this editor already contains FE keys. Deleting its objects
    # must not make their source identities available for a subsequent addition.
    $null = $worldAfter.Objects.Remove($copyAfter)
    $null = $worldAfter.Objects.Remove($plainAfter)
    $allocated = $roundtrip.AllocateObjectKey()
    if ($allocated -in @($copy.GaoKey, $plainCopy.GaoKey, $added.GaoKey)) { throw 'Deleted source GAO key was reused' }
    $nextAllocated = $roundtrip.AllocateObjectKey()
    if ($allocated -eq $nextAllocated) { throw 'An allocated GAO key was reused before insertion' }
    Write-Output 'PASS: removed source keys and allocated keys remain reserved'
    $tamperedSource = $bytes.Clone()
    $tamperedSource[$tamperedSource.Length-1] = $tamperedSource[$tamperedSource.Length-1] -bxor 1
    if ($plan.PreflightCheck($tamperedSource).IsSuccess) { throw 'Stale source bytes were accepted' }
    $conflicting = $snapshot.CreateSnapshot()
    ($conflicting.SceneStructureChanges | Where-Object ObjectId -eq $copy.ObjectId).GaoKey = $source.GaoKey
    $rejected = $false
    try { $null = $conflicting.BuildWritePlan($doc, $bytes, $resolver) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Conflicting added GAO key was accepted' }
    Write-Output "PASS: project save/load, delete, duplicate, FIX asset addition and transform replay ($($bytes.Length) -> $($rebuilt.Length) bytes)"
    if ($BuildArchive) {
        [JadeView.SceneWriter]::BuildAndSaveArchive($archive, $tempArchive, $entry.Index, $rebuilt, $null)
        $builtArchive = [JadeView.Archive]::new($tempArchive)
        $builtEntry = $builtArchive.Entries | Where-Object Id -eq $SceneId
        $stored = $builtArchive.Read($builtEntry, [int]::MaxValue)
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rebuilt)) -ne
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stored))) { throw 'BF replacement bytes changed' }
        if ($builtArchive.Entries.Count -ne $archive.Entries.Count) { throw 'BF entry inventory changed' }
        Write-Output 'PASS: full BF export and replacement readback'
    }
}
finally {
    if (Test-Path -LiteralPath $tempProject) { Remove-Item -LiteralPath $tempProject }
    if (Test-Path -LiteralPath $tempArchive) { Remove-Item -LiteralPath $tempArchive }
}
