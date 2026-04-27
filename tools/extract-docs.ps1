# Extract plain text from the ShopFlow .docx source documents into docs/source/.
# Re-runnable on any Windows machine with PowerShell 5+ (no extra deps).
# .docx is a ZIP containing word/document.xml; we read XML and strip tags.

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$Dest = Join-Path $Root "docs\source"
New-Item -ItemType Directory -Force -Path $Dest | Out-Null

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Extract-DocxText {
    param([string]$DocxPath, [string]$OutPath)

    $zip = [System.IO.Compression.ZipFile]::OpenRead($DocxPath)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "word/document.xml" }
        if (-not $entry) { throw "$DocxPath does not contain word/document.xml" }

        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try { $xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }

    # Convert paragraph / break / tab tags to whitespace, then strip everything else.
    $xml = [regex]::Replace($xml, '</w:p>', "`n")
    $xml = [regex]::Replace($xml, '<w:br[^/]*/>', "`n")
    $xml = [regex]::Replace($xml, '<w:tab[^/]*/>', "`t")
    $xml = [regex]::Replace($xml, '<[^>]+>', '')
    $xml = [System.Net.WebUtility]::HtmlDecode($xml)
    $xml = [regex]::Replace($xml, "`n`n+", "`n`n")

    [System.IO.File]::WriteAllText($OutPath, $xml, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  wrote $OutPath ($($xml.Length) chars)"
}

$docxFiles = Get-ChildItem -Path $Root -Filter "*.docx" -File
if ($docxFiles.Count -eq 0) {
    Write-Error "No .docx files found at $Root\"
    exit 1
}

foreach ($docx in $docxFiles) {
    $txtName = [System.IO.Path]::GetFileNameWithoutExtension($docx.Name) + ".txt"
    $out = Join-Path $Dest $txtName
    Write-Host "Extracting $($docx.Name) ..."
    Extract-DocxText -DocxPath $docx.FullName -OutPath $out
}

Write-Host "Done. Extracted text under $Dest\"
