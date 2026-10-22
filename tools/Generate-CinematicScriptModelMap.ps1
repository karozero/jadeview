param(
    [Parameter(Mandatory = $true)]
    [string]$ArchiveIndexPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$indexFile = (Resolve-Path -LiteralPath $ArchiveIndexPath).Path
$index = Get-Content -LiteralPath $indexFile -Raw | ConvertFrom-Json
$models = @($index.ScriptModels)
if ($models.Count -eq 0) {
    throw "The archive index contains no ScriptModels: $indexFile"
}

$catalog = [ordered]@{}
foreach ($model in ($models | Sort-Object Key)) {
    if ([string]::IsNullOrWhiteSpace($model.Key)) {
        throw 'The archive index contains a script model without a key.'
    }

    $callbacks = [string[]]@($model.Functions | ForEach-Object { $_.Name })
    $catalog[$model.Key.ToUpperInvariant()] = $callbacks
}

$json = ConvertTo-Json -InputObject $catalog -Depth 8
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($resolvedOutputPath, $json, $utf8WithoutBom)

Write-Output "Wrote $($catalog.Count) script-model callback catalogs to $resolvedOutputPath"
