#Requires -RunAsAdministrator
<#
  Installs LockOsdBlocker, which hides the Acer Quick Access Caps Lock / Num Lock / Scroll Lock pop-up.

  What it does:
    1. Compiles src\LockOsdBlocker.cs with the C# compiler that ships with Windows (.NET Framework 4).
    2. Creates a self-signed code-signing certificate that exists only on this PC, trusts it
       machine-wide, signs the helper with it and then DELETES the private key, so the
       certificate can never sign anything else. (Windows only lets a signed program in
       Program Files use UIAccess, which is needed to touch the Acer pop-up window.)
    3. Copies the helper to C:\Program Files\LockOsdBlocker and starts it at every login.

  Run from an elevated PowerShell in this folder:
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
$ErrorActionPreference = 'Stop'

$name      = 'LockOsdBlocker'
$certName  = 'CN=LockOsdBlocker local signing'
$installTo = Join-Path $env:ProgramFiles $name
$exe       = Join-Path $installTo "$name.exe"
$src       = Join-Path $PSScriptRoot 'src'
$csc       = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runKey    = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'

if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc (.NET Framework 4 is part of Windows 10/11)." }

# 1. Build
$build = Join-Path $env:TEMP "$name-build"
New-Item -ItemType Directory -Force $build | Out-Null
& $csc -nologo -target:winexe -optimize "-win32manifest:$src\$name.manifest" "-out:$build\$name.exe" "$src\$name.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'Built helper.'

# Stop a running copy (it exits when this event is signalled) so the file can be replaced
try {
    $quit = [System.Threading.EventWaitHandle]::OpenExisting("Local\$name.quit")
    $quit.Set() | Out-Null
    Start-Sleep -Milliseconds 500
} catch { }

# 2. Install files
New-Item -ItemType Directory -Force $installTo | Out-Null
Copy-Item "$build\$name.exe", "$src\$name.cs", "$src\$name.manifest" $installTo -Force
Remove-Item $build -Recurse -Force

# 3. Sign with a fresh certificate, then delete its private key
Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -eq $certName | Remove-Item
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certName `
    -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(20)
try {
    $cer = Join-Path $env:TEMP "$name.cer"
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Remove-Item $cer
    $sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256
    if ($sig.Status -ne 'Valid') { throw "Signing failed: $($sig.Status) $($sig.StatusMessage)" }
} finally {
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey
}
Write-Host 'Signed helper; private key deleted.'

# 4. Start at login for all users, and start it now
New-ItemProperty $runKey -Name $name -Value "`"$exe`"" -PropertyType String -Force | Out-Null
Start-Process $exe
Write-Host "Installed to $installTo and started. Press Caps Lock to check."
