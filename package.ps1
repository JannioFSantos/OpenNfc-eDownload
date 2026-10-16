# Whitelist: não percorre diretórios de documentos, preferências ou certificados.
param([string]$Output = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$nfceExe = Join-Path $PSScriptRoot 'dist\openNfc-eDownload.exe'
if (!(Test-Path -LiteralPath $nfceExe)) { throw 'Compile e teste antes de empacotar.' }
$nfceVersion = ([Version][Diagnostics.FileVersionInfo]::GetVersionInfo($nfceExe).FileVersion).ToString(3)
$nfcePortable = @('dist/openNfc-eDownload.exe','dist/openNfc-eDownload.exe.config','README.md','MANUAL.md','LICENSE','docs/ARQUITETURA.md','docs/VALIDACAO.md')
$nfceSource = @('.gitignore','.gitattributes','README.md','MANUAL.md','LICENSE','build.ps1','test.ps1','package.ps1','app.manifest','openNfc-eDownload.exe.config','docs/ARQUITETURA.md','docs/VALIDACAO.md')
$nfceSource += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { 'src/' + $_.Name })
foreach ($nfceSpec in @(@{Name="openNfc-eDownload-$nfceVersion-portatil.zip";Files=$nfcePortable},@{Name="openNfc-eDownload-$nfceVersion-fontes.zip";Files=$nfceSource})) {
    $nfceDestination = Join-Path $Output $nfceSpec.Name
    $nfceTemporary = $nfceDestination + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $nfceBackup = $nfceDestination + '.' + [guid]::NewGuid().ToString('N') + '.bak'
    try {
        $nfceStream = [IO.FileStream]::new($nfceTemporary,[IO.FileMode]::CreateNew)
        try {
            $nfceArchive = [IO.Compression.ZipArchive]::new($nfceStream,[IO.Compression.ZipArchiveMode]::Create,$true)
            try {
                foreach ($nfceRelative in $nfceSpec.Files) {
                    $nfcePath = Join-Path $PSScriptRoot $nfceRelative
                    $nfceEntryName = if ($nfceRelative.StartsWith('dist/')) { $nfceRelative.Substring(5) } else { $nfceRelative }
                    $nfceEntry = $nfceArchive.CreateEntry($nfceEntryName,[IO.Compression.CompressionLevel]::Optimal)
                    $nfceEntryStream = $nfceEntry.Open()
                    try { $nfceBytes = [IO.File]::ReadAllBytes($nfcePath); $nfceEntryStream.Write($nfceBytes,0,$nfceBytes.Length) }
                    finally { $nfceEntryStream.Dispose() }
                }
            }
            finally { $nfceArchive.Dispose() }
        }
        finally { $nfceStream.Dispose() }
        if (Test-Path -LiteralPath $nfceDestination) { [IO.File]::Replace($nfceTemporary,$nfceDestination,$nfceBackup) } else { [IO.File]::Move($nfceTemporary,$nfceDestination) }
        Write-Output $nfceDestination
    }
    finally {
        if (Test-Path -LiteralPath $nfceTemporary) { Remove-Item -LiteralPath $nfceTemporary }
        if (Test-Path -LiteralPath $nfceBackup) { Remove-Item -LiteralPath $nfceBackup }
    }
}
$nfceChecksums = @($nfcePortable | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ })
$nfceChecksums += @(Get-ChildItem -LiteralPath $Output -Filter "openNfc-eDownload-$nfceVersion-*.zip" | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
[IO.File]::WriteAllLines((Join-Path $Output 'SHA256SUMS.txt'),$nfceChecksums,[Text.UTF8Encoding]::new($false))
