function Get-BytesSha256 {
    param([byte[]] $Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($Bytes)
        return [BitConverter]::ToString($hashBytes).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-FunctionBody {
    param([string] $SourceText, [byte[]] $SourceBytes, [System.Text.Encoding] $SourceEncoding, [string] $FunctionName, [string] $SourcePath)

    $signaturePattern = '(?m)^[\t ]*[A-Za-z_][A-Za-z0-9_\t \*]*\b' + [regex]::Escape($FunctionName) + '\s*\('
    $signatureMatches = [regex]::Matches($SourceText, $signaturePattern)
    $definitions = [System.Collections.Generic.List[object]]::new()
    foreach ($signatureMatch in $signatureMatches) {
        $openParenthesis = $SourceText.IndexOf('(', $signatureMatch.Index)
        $parenthesisDepth = 0
        $closeParenthesis = -1
        for ($index = $openParenthesis; $index -lt $SourceText.Length; $index++) {
            if ($SourceText[$index] -eq '(') { $parenthesisDepth++ }
            if ($SourceText[$index] -eq ')') {
                $parenthesisDepth--
                if ($parenthesisDepth -eq 0) {
                    $closeParenthesis = $index
                    break
                }
            }
        }
        if ($closeParenthesis -lt 0) { continue }

        $openBrace = -1
        for ($index = $closeParenthesis + 1; $index -lt $SourceText.Length; $index++) {
            if ([char]::IsWhiteSpace($SourceText[$index])) { continue }
            if ($SourceText[$index] -eq '/' -and $index + 1 -lt $SourceText.Length -and $SourceText[$index + 1] -eq '/') {
                $newline = $SourceText.IndexOf("`n", $index + 2)
                if ($newline -lt 0) { break }
                $index = $newline
                continue
            }
            if ($SourceText[$index] -eq '/' -and $index + 1 -lt $SourceText.Length -and $SourceText[$index + 1] -eq '*') {
                $commentEnd = $SourceText.IndexOf('*/', $index + 2, [System.StringComparison]::Ordinal)
                if ($commentEnd -lt 0) { break }
                $index = $commentEnd + 1
                continue
            }
            if ($SourceText[$index] -eq '{') { $openBrace = $index }
            break
        }
        if ($openBrace -lt 0) { continue }

        $braceDepth = 0
        $lexicalState = 'code'
        for ($index = $openBrace; $index -lt $SourceText.Length; $index++) {
            $current = $SourceText[$index]
            $next = if ($index + 1 -lt $SourceText.Length) { $SourceText[$index + 1] } else { [char]0 }

            if ($lexicalState -eq 'line-comment') {
                if ($current -eq "`n") { $lexicalState = 'code' }
                continue
            }
            if ($lexicalState -eq 'block-comment') {
                if ($current -eq '*' -and $next -eq '/') { $lexicalState = 'code'; $index++ }
                continue
            }
            if ($lexicalState -eq 'string' -or $lexicalState -eq 'character') {
                if ($current -eq '\') { $index++; continue }
                if ($lexicalState -eq 'string' -and $current -eq '"') { $lexicalState = 'code'; continue }
                if ($lexicalState -eq 'character' -and $current -eq "'") { $lexicalState = 'code'; continue }
                continue
            }

            if ($current -eq '/' -and $next -eq '/') { $lexicalState = 'line-comment'; $index++; continue }
            if ($current -eq '/' -and $next -eq '*') { $lexicalState = 'block-comment'; $index++; continue }
            if ($current -eq '"') { $lexicalState = 'string'; continue }
            if ($current -eq "'") { $lexicalState = 'character'; continue }
            if ($current -eq '{') { $braceDepth++ }
            if ($current -eq '}') {
                $braceDepth--
                if ($braceDepth -eq 0) {
                    $endExclusive = $index + 1
                    $bodyText = $SourceText.Substring($signatureMatch.Index, $endExclusive - $signatureMatch.Index)
                    $startLine = ([regex]::Matches($SourceText.Substring(0, $signatureMatch.Index), "`n")).Count + 1
                    $endLine = $startLine + ([regex]::Matches($bodyText, "`n")).Count
                    $bodyByteOffset = $SourceEncoding.GetByteCount($SourceText.Substring(0, $signatureMatch.Index))
                    $bodyByteCount = $SourceEncoding.GetByteCount($bodyText)
                    $bodyBytes = [byte[]]::new($bodyByteCount)
                    [Array]::Copy($SourceBytes, $bodyByteOffset, $bodyBytes, 0, $bodyByteCount)
                    $definitions.Add([pscustomobject]@{
                        Name = $FunctionName
                        Source = $SourcePath
                        StartLine = $startLine
                        EndLine = $endLine
                        ByteOffset = $bodyByteOffset
                        ByteCount = $bodyByteCount
                        Text = $bodyText
                        Bytes = $bodyBytes
                        BodySha256 = Get-BytesSha256 -Bytes $bodyBytes
                    })
                    break
                }
            }
        }
    }

    if ($definitions.Count -ne 1) {
        throw "Expected exactly one function definition '$FunctionName' in $SourcePath, found $($definitions.Count)."
    }
    return $definitions[0]
}

function Read-ExactSourceFile {
    param([string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    $encodingName = 'UTF-8'
    try { $text = $encoding.GetString($bytes) }
    catch {
        $encoding = [System.Text.Encoding]::GetEncoding(1252)
        $text = $encoding.GetString($bytes)
        $encodingName = 'Windows-1252'
    }
    $lineEnding = 'LF'
    if ($text.Contains("`r`n")) {
        $lineEnding = 'CRLF'
        if ($text.Replace("`r`n", '').Contains("`n")) { $lineEnding = 'Mixed' }
    }
    [pscustomobject]@{
        Bytes = $bytes
        Text = $text
        Encoding = $encoding
        EncodingName = $encodingName
        Sha256 = Get-BytesSha256 -Bytes $bytes
        HasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        LineEnding = $lineEnding
    }
}

Export-ModuleMember -Function Get-BytesSha256, Get-FunctionBody, Read-ExactSourceFile
