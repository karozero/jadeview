param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceFile = (Resolve-Path (Join-Path $repoRoot $SourcePath)).Path
$irisRoot = (Resolve-Path (Join-Path $repoRoot 'external\irisEng')).Path
$sourcePrefix = $irisRoot.TrimEnd('\') + '\'
if (-not $sourceFile.StartsWith($sourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Compile-only audit accepts source files under external/irisEng only.'
}

$compilerEnvironment = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat'
$outputDirectory = Join-Path $repoRoot 'artifacts\native-script-bridge\dependency-audit'
$sourceName = [System.IO.Path]::GetFileNameWithoutExtension($sourceFile)
$objectFile = Join-Path $outputDirectory ($sourceName + '.obj')
$symbolsFile = Join-Path $outputDirectory ($sourceName + '-symbols.txt')
$relocationsFile = Join-Path $outputDirectory ($sourceName + '-relocations.txt')
$summaryFile = Join-Path $outputDirectory ($sourceName + '-imports.txt')

if (-not (Test-Path -LiteralPath $compilerEnvironment)) {
    throw "x86 MSVC environment was not found: $compilerEnvironment"
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Push-Location $repoRoot
try {
    $compileLine = @(
        'call', "`"$compilerEnvironment`"", '>nul', '&&',
        'cl', '/nologo', '/TC', '/W3', '/Gy', '/O1',
        '/Iexternal\jaded\src\dlls',
        '/Iexternal\jaded\src\dlls\BINK',
        '/Iexternal\jaded\src\libraries\XenonMeshProcessing\Sources',
        '/c', "`"$SourcePath`"", "/Fo`"$objectFile`""
    ) -join ' '
    & $env:ComSpec /d /c $compileLine
    if ($LASTEXITCODE -ne 0) {
        throw "Native x86 source compile failed with exit code $LASTEXITCODE."
    }

    $dumpLine = @('call', "`"$compilerEnvironment`"", '>nul', '&&', 'dumpbin', '/symbols',
        "`"$objectFile`"", '>', "`"$symbolsFile`"") -join ' '
    & $env:ComSpec /d /c $dumpLine
    if ($LASTEXITCODE -ne 0) {
        throw "COFF symbol extraction failed with exit code $LASTEXITCODE."
    }

    $relocationLine = @('call', "`"$compilerEnvironment`"", '>nul', '&&', 'dumpbin', '/relocations',
        "`"$objectFile`"", '>', "`"$relocationsFile`"") -join ' '
    & $env:ComSpec /d /c $relocationLine
    if ($LASTEXITCODE -ne 0) {
        throw "COFF relocation extraction failed with exit code $LASTEXITCODE."
    }

    $functionImports = Get-Content -LiteralPath $symbolsFile | ForEach-Object {
        if ($_ -match 'UNDEF\s+notype\s+\(\)\s+External\s+\|\s+(\S+)') {
            $Matches[1]
        }
    } | Sort-Object -Unique
    $dataSymbols = Get-Content -LiteralPath $symbolsFile | ForEach-Object {
        if ($_ -match '^[0-9A-Fa-f]+\s+([0-9A-Fa-f]{8})\s+UNDEF\s+notype\s+External\s+\|\s+(\S+)') {
            if ([Convert]::ToUInt64($Matches[1], 16) -gt 0) {
                $Matches[2]
            }
        }
    } | Sort-Object -Unique
    $relocatedSymbols = Get-Content -LiteralPath $relocationsFile | ForEach-Object {
        if ($_ -match '^\s+[0-9A-Fa-f]{8}\s+[A-Z0-9]+\s+[0-9A-Fa-f]{8}\s+[0-9A-Fa-f]+\s+(\S+)') {
            $Matches[1]
        }
    } | Sort-Object -Unique
    $relocatedSymbolSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relocatedSymbol in $relocatedSymbols) {
        [void]$relocatedSymbolSet.Add($relocatedSymbol)
    }
    $directFunctionImports = @($functionImports | Where-Object { $relocatedSymbolSet.Contains($_) })
    $directDataReferences = @($dataSymbols | Where-Object { $relocatedSymbolSet.Contains($_) })

    @(
        "Source: $($sourceFile.Substring($repoRoot.Length + 1))"
        'Mode: x86 compile and COFF relocation inspection; no link or execution'
        "Direct function imports referenced by relocations: $($directFunctionImports.Count)"
        ($directFunctionImports | ForEach-Object { "  $_" })
        "Direct undefined data references in relocations: $($directDataReferences.Count)"
        ($directDataReferences | ForEach-Object { "  $_" })
    ) | Set-Content -LiteralPath $summaryFile -Encoding UTF8

    Get-Content -LiteralPath $summaryFile
}
finally {
    Pop-Location
}
