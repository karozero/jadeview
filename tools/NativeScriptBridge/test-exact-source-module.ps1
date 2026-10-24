$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactRoot = Join-Path $repoRoot 'artifacts\native-script-bridge'

function Get-ArtifactInventory {
    @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -File | ForEach-Object {
        '{0}|{1}|{2}' -f $_.FullName, $_.Length, $_.LastWriteTimeUtc.Ticks
    } | Sort-Object)
}

$beforeImport = Get-ArtifactInventory
Import-Module (Join-Path $PSScriptRoot 'ExactSourceExtraction.psm1') -Force
$afterImport = Get-ArtifactInventory
if (Compare-Object $beforeImport $afterImport) { throw 'Importing the source module wrote artifacts.' }

$directory = Join-Path $artifactRoot 'source-module-regression'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$accent = [char]0xE9
$cases = @(
    @{ Encoding = [System.Text.UTF8Encoding]::new($false, $true); Name = 'UTF-8'; Bom = $false },
    @{ Encoding = [System.Text.Encoding]::GetEncoding(1252); Name = 'Windows-1252'; Bom = $false },
    @{ Encoding = [System.Text.UTF8Encoding]::new($true, $true); Name = 'UTF-8'; Bom = $true }
)
$caseIndex = 0
foreach ($case in $cases) {
    foreach ($ending in @('LF', 'CRLF', 'Mixed')) {
        $newline = if ($ending -eq 'LF') { "`n" } else { "`r`n" }
        $body = "void EncodingFixture(void) { const char *word = `"caf${accent}`"; }"
        $text = "/* pr${accent}fix */" + $newline + $body + $newline
        if ($ending -eq 'Mixed') { $text += "/* LF suffix */`n" }
        $bytes = [byte[]]($case.Encoding.GetPreamble() + $case.Encoding.GetBytes($text))
        $path = Join-Path $directory ("source-{0}.c" -f $caseIndex++)
        [IO.File]::WriteAllBytes($path, $bytes)
        $source = Read-ExactSourceFile -Path $path
        if ($source.EncodingName -ne $case.Name -or $source.HasBom -ne $case.Bom -or $source.LineEnding -ne $ending) {
            throw "Source encoding metadata changed: $path"
        }
        if ($source.Sha256 -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) {
            throw "Source hash changed: $path"
        }
        $function = Get-FunctionBody -SourceText $source.Text -SourceBytes $source.Bytes `
            -SourceEncoding $source.Encoding -FunctionName 'EncodingFixture' -SourcePath $path
        if ($function.BodySha256 -ne (Get-BytesSha256 -Bytes $case.Encoding.GetBytes($body))) {
            throw "Exact body bytes changed: $path"
        }
    }
}
Write-Output "PASS: source module import is inert; $caseIndex encoding/BOM/line-ending fixtures preserve source and body hashes."
