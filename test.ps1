#Requires -Version 7.0
param([switch]$UiSmoke)
$ErrorActionPreference = 'Stop'
$nfceTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('openNfce-tests-' + [guid]::NewGuid().ToString('N'))
$nfceOutput = Join-Path $nfceTestRoot 'build'
$nfcePfx = Join-Path $nfceTestRoot 'sintetico.pfx'
$nfceReport = Join-Path $nfceTestRoot 'testes.txt'
New-Item -ItemType Directory -Path $nfceTestRoot | Out-Null
$nfceRsa = $null
$nfceCert = $null
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Output $nfceOutput
    $nfceRsa = [System.Security.Cryptography.RSA]::Create(2048)
    $nfceRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=openNfceDownload TESTE', $nfceRsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $nfceSan = [byte[]](@(0x30,27,0xa0,25,6,5,0x60,0x4c,1,3,3,0xa0,16,0x0c,14) + [Text.Encoding]::ASCII.GetBytes('11222333000181'))
    $nfceRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509Extension]::new('2.5.29.17', $nfceSan, $false))
    $nfceCert = $nfceRequest.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddDays(2))
    [IO.File]::WriteAllBytes($nfcePfx, $nfceCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, 'somente-teste'))
    $nfceArgs = @('--self-test', '--test-cert', ('"' + $nfcePfx + '"'), '--report', ('"' + $nfceReport + '"'))
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'reference\sp-xsd')) { $nfceArgs += @('--schemas', ('"' + (Join-Path $PSScriptRoot 'reference') + '"')) }
    if ($UiSmoke) { $nfceArgs += '--ui-smoke' }
    $nfceProcess = Start-Process -FilePath (Join-Path $nfceOutput 'openNfc-eDownload.exe') -ArgumentList $nfceArgs -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath $nfceReport
    if ($nfceProcess.ExitCode -ne 0) { throw 'A suíte offline falhou.' }
    Write-Output ('Build testado: ' + $nfceOutput)
    Write-Output ('Relatório: ' + $nfceReport)
}
finally {
    if ($nfceCert) { $nfceCert.Dispose() }
    if ($nfceRsa) { $nfceRsa.Dispose() }
    if (Test-Path -LiteralPath $nfcePfx) { Remove-Item -LiteralPath $nfcePfx }
}
