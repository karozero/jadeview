param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$resolvedSourcePath = (Resolve-Path -LiteralPath $SourcePath).Path
$sourceText = [System.IO.File]::ReadAllText($resolvedSourcePath)
$pattern = '\{\s*0x([0-9a-fA-F]{1,8})\s*,\s*([A-Za-z_][A-Za-z0-9_]*)'
$functionNames = [ordered]@{}
foreach ($match in [regex]::Matches($sourceText, $pattern)) {
    $functionNames[$match.Groups[1].Value.ToUpperInvariant()] = $match.Groups[2].Value
}
if ($functionNames.Count -eq 0) {
    throw "No Iris callback names found in $resolvedSourcePath"
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$json = ConvertTo-Json -InputObject $functionNames -Depth 4
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($resolvedOutputPath, $json, $utf8WithoutBom)
Write-Output "Wrote $($functionNames.Count) Iris callback names to $resolvedOutputPath"
