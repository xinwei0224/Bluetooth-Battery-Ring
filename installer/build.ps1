$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# Stage the payload (extension + native host + tray) and zip it.
$stage = Join-Path $root 'build\payload'
$zip = Join-Path $root 'build\payload.zip'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null
Copy-Item (Join-Path $root 'extension') (Join-Path $stage 'extension') -Recurse -Force
New-Item -ItemType Directory (Join-Path $stage 'native-host'), (Join-Path $stage 'tray') -Force | Out-Null
Copy-Item (Join-Path $root 'native-host\host.exe'), (Join-Path $root 'native-host\status.ps1') (Join-Path $stage 'native-host') -Force
Copy-Item (Join-Path $root 'tray\tray.exe'), (Join-Path $root 'tray\status.ps1') (Join-Path $stage 'tray') -Force
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)

& $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$here\BluetoothBatterySetup.exe" `
    "/resource:$zip,ShokzPayload.zip" `
    /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    (Join-Path $here 'installer.cs')
Write-Host "built $here\BluetoothBatterySetup.exe"
