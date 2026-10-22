param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$callPattern = [regex]'\b(?<api>AI_ExecCallbackAll|AI_ExecCallback)\s*\((?<arguments>[^;]*?)\)\s*;'
$sites = [System.Collections.Generic.List[object]]::new()

foreach ($file in Get-ChildItem -LiteralPath $resolvedSource -Filter '*.c' -File -Recurse) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in $callPattern.Matches($text)) {
        $arguments = $match.Groups['arguments'].Value
        $argumentParts = $arguments.Split(',')
        if ($argumentParts.Length -lt 2 -or $argumentParts[1].Trim() -notmatch '^-?\d+$') { continue }

        $lineNumber = 1 + [regex]::Matches($text.Substring(0, $match.Index), "`n").Count
        $relativePath = [System.IO.Path]::GetRelativePath($resolvedSource, $file.FullName).Replace('\', '/')
        $sites.Add([ordered]@{
            CallbackId = [int]$argumentParts[1].Trim()
            Api = $match.Groups['api'].Value
            Source = $relativePath
            Line = $lineNumber
            Call = "$($match.Groups['api'].Value)($($arguments.Trim()))"
        })
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$json = ConvertTo-Json -InputObject @($sites) -Depth 5
[System.IO.File]::WriteAllText($resolvedOutput, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote $($sites.Count) source callback dispatch sites to $resolvedOutput"
