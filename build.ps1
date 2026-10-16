param([string]$Output = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$nfceCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$nfceSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $nfceCompiler /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /langversion:5 "/out:$Output\openNfc-eDownload.exe" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Security.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll $nfceSources
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'openNfc-eDownload.exe.config') -Destination $Output
foreach ($nfceDoc in @('README.md','MANUAL.md')) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $nfceDoc)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $nfceDoc) -Destination $Output }
}
Write-Output "Aplicativo: $Output\openNfc-eDownload.exe"
