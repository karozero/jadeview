param(
    [string]$IrisFunctionsPath = (Join-Path $PSScriptRoot "../external/irisEng/ai/ai_functions.c"),
    [string]$JadedDefinitionsPath = (Join-Path $PSScriptRoot "../external/jaded/src/libraries/AIinterp/Sources/Functions/AIdeffct.h"),
    [string]$CatalogPath = (Join-Path $PSScriptRoot "../src/Features/Cinematics/Data/engine-function-selectors.json"),
    [string]$OutputPath,
    [string]$EvidenceOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Add-SelectorName {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Map,
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)][string]$Name
    )

    $normalizedSelector = $Selector.ToUpperInvariant()
    if (-not $Map.ContainsKey($normalizedSelector)) {
        $Map[$normalizedSelector] = [System.Collections.Generic.List[string]]::new()
    }
    if (-not $Map[$normalizedSelector].Contains($Name)) {
        $Map[$normalizedSelector].Add($Name)
    }
}

function Normalize-FunctionName {
    param([string]$Name)
    return ($Name -replace '^AI_EvalFunc_', '' -replace '_', '').ToLowerInvariant()
}

foreach ($inputPath in @($IrisFunctionsPath, $JadedDefinitionsPath, $CatalogPath)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required selector source was not found: $inputPath"
    }
}

$irisNames = [System.Collections.Generic.SortedDictionary[string, System.Collections.Generic.List[string]]]::new([StringComparer]::Ordinal)
foreach ($line in Get-Content -LiteralPath $IrisFunctionsPath) {
    if ($line -match '^\s*\{\s*0x(?<selector>[0-9A-Fa-f]+)\s*,\s*[^,]+,\s*AI_EvalFunc_(?<name>[A-Za-z0-9_]+)\s*\}') {
        Add-SelectorName -Map $irisNames -Selector $Matches.selector -Name $Matches.name
    }
}

$jadedNames = [System.Collections.Generic.SortedDictionary[string, System.Collections.Generic.List[string]]]::new([StringComparer]::Ordinal)
foreach ($line in Get-Content -LiteralPath $JadedDefinitionsPath) {
    if ($line -match 'DEFINE_FUNCTION\(\s*(?<selector>\d+)\s*,\s*[^,]*,\s*"(?<name>[^"]+)"') {
        $selector = ([uint32]$Matches.selector).ToString("X4", [CultureInfo]::InvariantCulture)
        Add-SelectorName -Map $jadedNames -Selector $selector -Name $Matches.name
    }
}

$catalogNames = [System.Collections.Generic.SortedDictionary[string, System.Collections.Generic.List[string]]]::new([StringComparer]::Ordinal)
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json -AsHashtable
foreach ($entry in $catalog.GetEnumerator()) {
    Add-SelectorName -Map $catalogNames -Selector $entry.Key -Name $entry.Value
}

$selectorIds = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($sourceMap in @($irisNames, $jadedNames, $catalogNames)) {
    foreach ($selector in $sourceMap.Keys) { [void]$selectorIds.Add($selector) }
}

$rows = foreach ($selector in $selectorIds) {
    $iris = @()
    if ($irisNames.ContainsKey($selector)) { $iris = @($irisNames[$selector].ToArray()) }
    $jaded = @()
    if ($jadedNames.ContainsKey($selector)) { $jaded = @($jadedNames[$selector].ToArray()) }
    $catalogEntry = @()
    if ($catalogNames.ContainsKey($selector)) { $catalogEntry = @($catalogNames[$selector].ToArray()) }
    $allSourceNames = @($iris + $jaded | Select-Object -Unique)
    $normalizedSourceNames = @($allSourceNames | ForEach-Object { Normalize-FunctionName $_ } | Select-Object -Unique)

    $status = if ($iris.Count -gt 0 -and $jaded.Count -gt 0) {
        if ($normalizedSourceNames.Count -eq 1) { "both-same-name" } else { "both-name-divergence" }
    } elseif ($iris.Count -gt 0) { "iris-only" }
    elseif ($jaded.Count -gt 0) { "jaded-only" }
    else { "catalog-only" }

    [pscustomobject]@{
        Selector = $selector
        CatalogName = $catalogEntry -join " | "
        IrisNames = $iris
        JadedNames = $jaded
        Status = $status
        CatalogMatchesIris = $catalogEntry.Count -gt 0 -and @($catalogEntry | ForEach-Object { Normalize-FunctionName $_ } | Where-Object { $normalizedIris = @($iris | ForEach-Object { Normalize-FunctionName $_ }); $_ -in $normalizedIris }).Count -gt 0
        SourceNamesNormalized = $normalizedSourceNames
    }
}

$summary = [pscustomobject]@{
    IrisSource = [IO.Path]::GetFullPath($IrisFunctionsPath)
    JadedSource = [IO.Path]::GetFullPath($JadedDefinitionsPath)
    JadeViewCatalog = [IO.Path]::GetFullPath($CatalogPath)
    Counts = [pscustomobject]@{
        IrisSelectors = $irisNames.Count
        JadedSelectors = $jadedNames.Count
        JadeViewSelectors = $catalogNames.Count
        BothSameName = @($rows | Where-Object Status -eq "both-same-name").Count
        BothNameDivergence = @($rows | Where-Object Status -eq "both-name-divergence").Count
        IrisOnly = @($rows | Where-Object Status -eq "iris-only").Count
        JadedOnly = @($rows | Where-Object Status -eq "jaded-only").Count
        CatalogSelectorsMissingFromIris = @($rows | Where-Object { $_.CatalogName -and $_.IrisNames.Count -eq 0 }).Count
        CatalogNameDiffersFromIris = @($rows | Where-Object { $_.CatalogName -and $_.IrisNames.Count -gt 0 -and -not $_.CatalogMatchesIris }).Count
    }
    Applicability = "Iris is a pre-release/source model and Jaded is a 2006 Rayman 4 prototype fork; agreement is corroboration, not proof of 2003 retail-PC behavior. Selector IDs are distinct from OFC callback resource keys."
    Selectors = @($rows)
}

if ($EvidenceOutputPath) {
    $evidence = [ordered]@{}
    foreach ($row in $rows) {
        $catalogName = $row.CatalogName -replace '^AI_EvalFunc_', ''
        $evidence[$row.Selector] = [ordered]@{
            CatalogName = $catalogName
            IrisNames = @($row.IrisNames)
            JadedNames = @($row.JadedNames)
            Status = $row.Status
        }
    }
    $fullEvidencePath = [IO.Path]::GetFullPath($EvidenceOutputPath)
    $evidenceDirectory = Split-Path -Parent $fullEvidencePath
    if ($evidenceDirectory) { [IO.Directory]::CreateDirectory($evidenceDirectory) | Out-Null }
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fullEvidencePath -Encoding utf8
    Write-Output "WROTE $fullEvidencePath"
}

if ($OutputPath) {
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $parentDirectory = Split-Path -Parent $fullOutputPath
    if ($parentDirectory) { [IO.Directory]::CreateDirectory($parentDirectory) | Out-Null }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullOutputPath -Encoding utf8
    Write-Output "WROTE $fullOutputPath"
}

$summary.Counts | Format-List
Write-Output "High-value conflicts:"
$rows | Where-Object { $_.Status -eq "both-name-divergence" -and $_.Selector -in @("1F7D", "1FFB") } |
    Select-Object Selector, CatalogName, IrisNames, JadedNames, Status | Format-List
