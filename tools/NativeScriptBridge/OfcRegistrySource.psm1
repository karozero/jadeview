Import-Module (Join-Path $PSScriptRoot 'ExactSourceExtraction.psm1')

function Get-CCodeMask {
    param([string] $Text)
    $masked = $Text.ToCharArray()
    $state = 'code'
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $current = $Text[$index]
        $next = if ($index + 1 -lt $Text.Length) { $Text[$index + 1] } else { [char]0 }
        if ($state -eq 'code') {
            if ($current -eq '/' -and $next -eq '/') { $state = 'line'; $masked[$index] = ' '; $masked[++$index] = ' '; continue }
            if ($current -eq '/' -and $next -eq '*') { $state = 'block'; $masked[$index] = ' '; $masked[++$index] = ' '; continue }
            if ($current -eq '"') { $state = 'string'; $masked[$index] = ' '; continue }
            if ($current -eq "'") { $state = 'char'; $masked[$index] = ' '; continue }
            continue
        }
        if ($current -ne "`r" -and $current -ne "`n") { $masked[$index] = ' ' }
        if ($state -eq 'line') {
            if ($current -eq "`n") { $state = 'code' }
            continue
        }
        if ($state -eq 'block') {
            if ($current -eq '*' -and $next -eq '/') { $masked[++$index] = ' '; $state = 'code' }
            continue
        }
        if ($current -eq '\') { if ($index + 1 -lt $Text.Length) { $masked[++$index] = ' ' }; continue }
        if (($state -eq 'string' -and $current -eq '"') -or ($state -eq 'char' -and $current -eq "'")) { $state = 'code' }
    }
    return -join $masked
}

function Get-RegistrySpan {
    param($Document, $Match, [string] $Source)
    $offset = $Document.Encoding.GetByteCount($Document.Text.Substring(0, $Match.Index))
    $text = $Document.Text.Substring($Match.Index, $Match.Length)
    $count = $Document.Encoding.GetByteCount($text)
    $bytes = [byte[]]::new($count)
    [Array]::Copy($Document.Bytes, $offset, $bytes, 0, $count)
    [pscustomobject]@{
        Source = $Source
        SourceSha256 = $Document.Sha256
        SourceEncoding = $Document.EncodingName
        StartLine = ([regex]::Matches($Document.Text.Substring(0, $Match.Index), "`n")).Count + 1
        ByteOffset = $offset
        ByteCount = $count
        Text = $text
        Sha256 = Get-BytesSha256 -Bytes $bytes
    }
}

function Get-OfcRegistryContract {
    param($Definition, $Declaration, [string] $DefinitionSource, [string] $DeclarationSource)
    $prefix = '(?m)^[\t ]*'
    $array = 'tStruct_funclist\s+AI2C_gat_fctdefs\s*\[\s*(?<extent>[1-9][0-9]*)\s*\]'
    $definitions = [regex]::Matches((Get-CCodeMask $Definition.Text), $prefix + $array + '\s*=\s*\{')
    $declarations = [regex]::Matches((Get-CCodeMask $Declaration.Text), $prefix + 'extern\s+' + $array + '\s*;')
    if ($definitions.Count -ne 1 -or $declarations.Count -ne 1) {
        throw 'Registry contract requires exactly one active definition and one active header declaration.'
    }
    $definitionExtent = 0
    $declarationExtent = 0
    if (-not [int]::TryParse($definitions[0].Groups['extent'].Value, [ref]$definitionExtent) -or
        -not [int]::TryParse($declarations[0].Groups['extent'].Value, [ref]$declarationExtent) -or
        $definitionExtent -ne $declarationExtent) {
        throw 'Registry definition/header extents differ or exceed the supported integer range.'
    }
    [pscustomobject]@{
        Extent = $definitionExtent
        Definition = Get-RegistrySpan $Definition $definitions[0] $DefinitionSource
        Declaration = Get-RegistrySpan $Declaration $declarations[0] $DeclarationSource
    }
}

function Get-OfcRegistryRow {
    param($Document, [uint32] $Key, [string] $Source)
    $keyHex = $Key.ToString('X8')
    $pattern = '(?m)^[\t ]*\{[\t ]*0x' + $keyHex + '[\t ]*,[\t ]*(?<symbol>[A-Za-z_][A-Za-z0-9_]*)[\t ]*\}[\t ]*,[\t ]*\r?$'
    $matches = [regex]::Matches((Get-CCodeMask $Document.Text), $pattern)
    if ($matches.Count -ne 1) {
        throw "The selected key $keyHex does not have exactly one source registry row; refusing an uncompiled fallback."
    }
    [pscustomobject]@{
        Symbol = $matches[0].Groups['symbol'].Value
        Span = Get-RegistrySpan $Document $matches[0] $Source
    }
}

Export-ModuleMember -Function Get-OfcRegistryContract, Get-OfcRegistryRow
