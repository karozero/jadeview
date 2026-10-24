param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestFullPath = (Resolve-Path (Join-Path $repoRoot $ManifestPath)).Path
$manifestDirectory = [System.IO.Path]::GetDirectoryName($manifestFullPath)
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\instance-initialization-probe'
Import-Module (Join-Path $PSScriptRoot 'ExactSourceExtraction.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'OfcRegistrySource.psm1') -Force

function Resolve-RoleResource {
    param([string] $Role, [string] $Kind)
    $links = @($fixture.Links | Where-Object { $_.Role -eq $Role })
    if ($links.Count -ne 1 -or $links[0].Status -ne 'Resolved') { throw "Expected one resolved link for role '$Role'." }
    $entries = @($fixture.Resources | Where-Object { $_.File -eq $links[0].File })
    if ($entries.Count -ne 1 -or $entries[0].Status -ne 'Resolved' -or $entries[0].Kind -ne $Kind) {
        throw "Role '$Role' must resolve to exactly one resource of kind '$Kind'."
    }
    $entry = $entries[0]
    if ([string]$entry.Key -ne [string]$links[0].Key) { throw "Role '$Role' link/resource keys disagree." }
    $path = [System.IO.Path]::GetFullPath((Join-Path $manifestDirectory $entry.File))
    $prefix = $manifestDirectory.TrimEnd('\') + '\'
    if (-not $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Role '$Role' path escaped its manifest directory." }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ne [int]$entry.Size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) {
        throw "Role '$Role' resource failed manifest size/SHA-256 verification."
    }
    $key = [Convert]::ToUInt32([string]$links[0].Key, 16)
    if ($key -eq 0 -or $key -eq [uint32]::MaxValue -or [uint32]$entry.Offset -eq 0) { throw "Role '$Role' has empty/sentinel key or zero resource offset." }
    [pscustomobject]@{ Role = $Role; Key = $key; Offset = [uint32]$entry.Offset; Path = $path; Entry = $entry; Bytes = $bytes; Link = $links[0] }
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
$fixture = $manifest.Fixture
$selected = @(
    (Resolve-RoleResource -Role 'owner' -Kind 'l'),
    (Resolve-RoleResource -Role 'instance' -Kind 'q'),
    (Resolve-RoleResource -Role 'instance-variables' -Kind 't'),
    (Resolve-RoleResource -Role 'model' -Kind 's'),
    (Resolve-RoleResource -Role 'model-entry:.ova' -Kind 't'),
    (Resolve-RoleResource -Role 'model-entry:.ofc' -Kind 'r')
)
$sourceResources = @($selected | Where-Object { $_.Role -ne 'owner' })
if ($sourceResources.Count -ne 5 -or (@($sourceResources.Key | Select-Object -Unique).Count -ne 5) -or
    (@($sourceResources.Offset | Select-Object -Unique).Count -ne 5)) { throw 'Expected five uniquely keyed and offset source resources.' }
if ($selected[2].Entry.OvaSummary.Status -ne 'DecodedStorage' -or $selected[4].Entry.OvaSummary.Status -ne 'DecodedStorage') {
    throw 'Both OVA resources need decoded manifest summaries.'
}
if ($selected[2].Entry.OvaSummary.DescriptorCount -ne 0 -or $selected[2].Entry.OvaSummary.StorageLength -ne 0) {
    throw 'This native proof requires an empty instance OVA overlay.'
}

$aimBytes = $selected[3].Bytes
if ($aimBytes.Length -eq 0 -or ($aimBytes.Length % 8) -ne 0) { throw 'AIM bytes are not a nonempty 8-byte-entry table.' }
$modelOvaLink = @($fixture.Links | Where-Object { $_.Role -eq 'model-entry:.ova' })
$functionLink = @($fixture.Links | Where-Object { $_.Role -eq 'model-entry:.ofc' })
if ($modelOvaLink.Count -ne 1 -or $functionLink.Count -ne 1 -or
    [int]$modelOvaLink[0].EntryIndex -ne 0 -or [int]$functionLink[0].EntryIndex -ne 1) {
    throw 'AIM OVA/OFC selected links do not match the manifest entry order.'
}
$functionKey = [Convert]::ToUInt32([string]$functionLink[0].Key, 16)
$registryDocument = Read-ExactSourceFile -Path (Join-Path $repoRoot 'external\irisEng\ai\ai_compiledscripts.c')
$registryHeader = Read-ExactSourceFile -Path (Join-Path $repoRoot 'external\irisEng\ai\ai_compiledscripts.h')
$registryContract = Get-OfcRegistryContract $registryDocument $registryHeader 'external/irisEng/ai/ai_compiledscripts.c' 'external/irisEng/ai/ai_compiledscripts.h'
$registryRow = Get-OfcRegistryRow $registryDocument $functionKey 'external/irisEng/ai/ai_compiledscripts.c'
$symbol = $registryRow.Symbol

$callbackDefinitions = [System.Collections.Generic.List[object]]::new()
foreach ($sourceFile in Get-ChildItem (Join-Path $repoRoot 'external\irisEng\ai') -Filter '*.c' -Recurse) {
    $document = Read-ExactSourceFile -Path $sourceFile.FullName
    if (-not [regex]::IsMatch($document.Text, '(?m)^[\t ]*[A-Za-z_][A-Za-z0-9_\t \*]*\b' + [regex]::Escape($symbol) + '\s*\(')) { continue }
    $relativeSource = $sourceFile.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $callbackDefinitions.Add((Get-FunctionBody -SourceText $document.Text -SourceBytes $document.Bytes -SourceEncoding $document.Encoding -FunctionName $symbol -SourcePath $relativeSource))
}
if ($callbackDefinitions.Count -ne 1) { throw "Registry callback '$symbol' must have exactly one source definition." }
$callback = $callbackDefinitions[0]

$functionGroups = @(
    [pscustomobject]@{ Path = 'external/irisEng/ai_common.c'; Names = @(
        'AI_ul_CallbackLoadInstance', 'AI_ul_CallbackLoadModel', 'AI_ul_CallbackLoadVars', 'AI_ul_CallbackLoadFunction',
        'AI_ResolveAllFctRef', 'AI_ResolveVarRef', 'AI_ResolveModelRef', 'AI_ResolveCarDecHiHiEditor',
        'AI_FreeVarDes', 'AI_InstanceAfterLoading', 'AI_ResolveInstanceRef', 'AI_ReinitInstance',
        'AI_SpecialWParam', 'AI_ul_RealResolveOneRef', 'AI_b_IsARef', 'AI_ul_ResolveOneRef'
    ) },
    [pscustomobject]@{ Path = 'external/irisEng/loading.c'; Names = @(
        'LOA_ReadLong', 'LOA_ReadShort', 'LOA_ReadChar', 'LOA_ReadFloat', 'LOA_ReadCharArray',
        'LOA_ReadString', 'LOA_ReadVector', 'LOA_MakeFileRef', 'LOA_ul_GetCurrentKey'
    ) }
)
$functions = [System.Collections.Generic.List[object]]::new()
foreach ($group in $functionGroups) {
    $path = Join-Path $repoRoot ($group.Path.Replace('/', '\'))
    $document = Read-ExactSourceFile -Path $path
    foreach ($name in $group.Names) {
        $functions.Add((Get-FunctionBody -SourceText $document.Text -SourceBytes $document.Bytes -SourceEncoding $document.Encoding -FunctionName $name -SourcePath $group.Path))
    }
}
$functions.Add($callback)

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$generatedPath = Join-Path $outputDirectory 'generated_source.c'
$stream = [System.IO.MemoryStream]::new()
$ascii = [System.Text.Encoding]::ASCII
$prototypes = @'
void AI_ResolveAllFctRef(tAIModel*);
void AI_ResolveVarRef(void*, byte*, AIVarNameElem*, AIVarElem*, unsigned int, int);
void AI_ResolveModelRef(tAIModel*);
void AI_ResolveCarDecHiHiEditor(tAI*);
void AI_FreeVarDes(tAIVars*);
void AI_InstanceAfterLoading(tAI*);
BOOL AI_SpecialWParam(char);
unsigned int AI_ul_RealResolveOneRef(int, unsigned int);
BOOL AI_b_IsARef(int);
int AI_ul_ResolveOneRef(int, int);
tAIModel* AI_ul_CallbackLoadModel(uint32_t);
tAIVars* AI_ul_CallbackLoadVars(uint32_t);
tAIFunction* AI_ul_CallbackLoadFunction(uint32_t);

'@
$prototypeBytes = $ascii.GetBytes($prototypes)
$stream.Write($prototypeBytes, 0, $prototypeBytes.Length)
foreach ($function in $functions) {
    $comment = "/* exact source span $($function.Source):$($function.StartLine)-$($function.EndLine), body SHA-256 $($function.BodySha256) */`r`n"
    $commentBytes = $ascii.GetBytes($comment)
    $stream.Write($commentBytes, 0, $commentBytes.Length)
    $stream.Write($function.Bytes, 0, $function.Bytes.Length)
    $separator = $ascii.GetBytes("`r`n`r`n")
    $stream.Write($separator, 0, $separator.Length)
}
$generatedBytes = $stream.ToArray()
$stream.Dispose()
[System.IO.File]::WriteAllBytes($generatedPath, $generatedBytes)

$registryPath = Join-Path $outputDirectory 'generated_registry_row.c'
$registryText = @"
/* One source registry row selected by this fixture; callback identity only. */
#include "../../../external/irisEng/ai/ai_compiledscripts.h"
extern int $symbol();
tStruct_funclist AI2C_gat_fctdefs[$($registryContract.Extent)] = { { (int)0x$($functionKey.ToString('X8')), $symbol } };
unsigned int AI2C_i_SizeofArray(void) { return 1; }
"@
[System.IO.File]::WriteAllText($registryPath, $registryText, [System.Text.Encoding]::ASCII)

$sourceRecords = @($functionGroups | ForEach-Object {
    $sourcePath = Join-Path $repoRoot ($_.Path.Replace('/', '\'))
    [pscustomobject]@{ Path = $_.Path; Sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash; Functions = $_.Names }
})
$callbackPath = Join-Path $repoRoot ($callback.Source.Replace('/', '\'))
$sliceManifest = [pscustomobject]@{
    SchemaVersion = 1
    Purpose = 'Bounded source-executed Iris instance initialization on manifest-selected BF resources; callback body linked but never invoked.'
    Fixture = [pscustomobject]@{ Container = $manifest.Container; OwnerKey = $fixture.OwnerKey; SelectedResources = @($selected | ForEach-Object {
        [pscustomobject]@{ Role = $_.Role; Key = ('{0:X8}' -f $_.Key); Offset = $_.Offset; File = $_.Entry.File; Kind = $_.Entry.Kind; Size = $_.Bytes.Length; Sha256 = $_.Entry.Sha256 }
    }) }
    Registry = [pscustomobject]@{ Source = 'external/irisEng/ai/ai_compiledscripts.c'; SourceSha256 = $registryDocument.Sha256; RowLine = $registryRow.Span.StartLine; Key = ('{0:X8}' -f $functionKey); Symbol = $symbol; RowSha256 = $registryRow.Span.Sha256; ArrayExtent = $registryContract.Extent }
    Callback = [pscustomobject]@{ Source = $callback.Source; SourceSha256 = (Get-FileHash -LiteralPath $callbackPath -Algorithm SHA256).Hash; StartLine = $callback.StartLine; EndLine = $callback.EndLine; BodySha256 = $callback.BodySha256; Invocation = 'not executed' }
    ExtractedFunctions = @($functions | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Source = $_.Source; StartLine = $_.StartLine; EndLine = $_.EndLine; ByteOffset = $_.ByteOffset; ByteCount = $_.ByteCount; BodySha256 = $_.BodySha256 } })
    GeneratedSourceSha256 = Get-FileHash -LiteralPath $generatedPath -Algorithm SHA256 | Select-Object -ExpandProperty Hash
    GeneratedRegistrySha256 = Get-FileHash -LiteralPath $registryPath -Algorithm SHA256 | Select-Object -ExpandProperty Hash
}
$manifestOut = Join-Path $outputDirectory 'source_manifest.json'
$sliceManifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestOut -Encoding UTF8
Write-Output "Generated exact-source initialization slice: $generatedPath"
Write-Output "Generated fixture-selected callback registry: $registryPath"
Write-Output "Provenance: $manifestOut"
