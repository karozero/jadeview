param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\instance-initialization-probe'
$compilerEnvironment = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat'
if (-not (Test-Path -LiteralPath $compilerEnvironment)) { throw "x86 MSVC environment was not found: $compilerEnvironment" }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'extract-instance-initialization-slice.ps1') -ManifestPath $ManifestPath | Out-Host
    $sourceManifestPath = Join-Path $outputDirectory 'source_manifest.json'
    $sourceManifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
    $resources = @{}
    foreach ($resource in $sourceManifest.Fixture.SelectedResources) { $resources[[string]$resource.Role] = $resource }
    foreach ($role in @('owner', 'instance', 'model', 'instance-variables', 'model-entry:.ova', 'model-entry:.ofc')) {
        if (-not $resources.ContainsKey($role)) { throw "Generated source manifest is missing role '$role'." }
    }
    $paths = @(
        $resources['instance'].File,
        $resources['model'].File,
        $resources['instance-variables'].File,
        $resources['model-entry:.ova'].File,
        $resources['model-entry:.ofc'].File
    ) | ForEach-Object { Join-Path (Split-Path -Parent (Resolve-Path (Join-Path $repoRoot $ManifestPath))) $_ }
    $numbers = @(
        ('0x' + [string]$resources['owner'].Key),
        ('0x' + [string]$resources['instance'].Key), ('0x{0:X8}' -f [uint32]$resources['instance'].Offset),
        ('0x' + [string]$resources['model'].Key), ('0x{0:X8}' -f [uint32]$resources['model'].Offset),
        ('0x' + [string]$resources['instance-variables'].Key), ('0x{0:X8}' -f [uint32]$resources['instance-variables'].Offset),
        ('0x' + [string]$resources['model-entry:.ova'].Key), ('0x{0:X8}' -f [uint32]$resources['model-entry:.ova'].Offset),
        ('0x' + [string]$resources['model-entry:.ofc'].Key), ('0x{0:X8}' -f [uint32]$resources['model-entry:.ofc'].Offset)
    )

    $exeRelative = 'artifacts\native-script-bridge\instance-initialization-probe\instance_initialization_probe.exe'
    $compileLine = @(
        'call', "`"$compilerEnvironment`"", '>nul', '&&',
        'cl', '/nologo', '/TC', '/W3', '/Gy', '/O1', '/MD',
        '/Iexternal\jaded\src\dlls',
        '/Iexternal\jaded\src\dlls\BINK',
        '/Iexternal\jaded\src\libraries\XenonMeshProcessing\Sources',
        '/Foartifacts\native-script-bridge\instance-initialization-probe\',
        "/Fe:$exeRelative",
        'tools\NativeScriptBridge\instance_initialization_probe.c',
        'tools\NativeScriptBridge\instance_initialization_probe_host.c',
        'tools\NativeScriptBridge\instance_initialization_probe_preflight.c',
        'tools\NativeScriptBridge\instance_initialization_probe_adapters.c',
        'artifacts\native-script-bridge\instance-initialization-probe\generated_registry_row.c',
        'external\irisEng\ai\ai_types.c',
        '/link', '/OPT:REF', '/INCREMENTAL:NO'
    ) -join ' '
    & $env:ComSpec /d /c $compileLine
    if ($LASTEXITCODE -ne 0) { throw "Instance initialization native probe failed to compile/link: $LASTEXITCODE" }

    $runLog = Join-Path $outputDirectory ('run-{0}-{1}.log' -f $sourceManifest.Fixture.Container, $sourceManifest.Fixture.OwnerKey)
    $runArguments = @($numbers) + @($paths)
    $outputLines = & (Join-Path $repoRoot $exeRelative) @runArguments 2>&1
    $exitCode = $LASTEXITCODE
    $outputLines | Set-Content -LiteralPath $runLog -Encoding UTF8
    Write-Output "NATIVE_EXIT_CODE=$exitCode"
    Write-Output "NATIVE_LOG=$runLog"
    $outputLines | ForEach-Object { Write-Output $_ }
    if ($exitCode -ne 0) { throw "Native probe exited ${exitCode}: $($outputLines -join [Environment]::NewLine)" }
}
finally { Pop-Location }
