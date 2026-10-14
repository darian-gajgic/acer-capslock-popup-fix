#Requires -RunAsAdministrator
<#
  Removes LockOsdBlocker completely: stops it, removes the autostart entry, deletes
  C:\Program Files\LockOsdBlocker and removes its certificate from the trusted root store.

  Run from an elevated PowerShell in this folder:
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
#>
$name     = 'LockOsdBlocker'
$certName = 'CN=LockOsdBlocker local signing'

try {
    [System.Threading.EventWaitHandle]::OpenExisting("Local\$name.quit").Set() | Out-Null
    Start-Sleep -Milliseconds 500
} catch { }
Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($key in 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run',
                 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run') {
    Remove-ItemProperty $key -Name $name -ErrorAction SilentlyContinue
}

$dir = Join-Path $env:ProgramFiles $name
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $dir) { Write-Warning "$dir is still in use; restart Windows and run this script again." }

Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -eq $certName | Remove-Item
# -DeleteKey is a provider parameter that Windows PowerShell 5.1 only binds with an explicit -Path
Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq $certName |
    ForEach-Object { Remove-Item -Path "Cert:\CurrentUser\My\$($_.Thumbprint)" -DeleteKey }

Write-Host 'LockOsdBlocker removed.'
