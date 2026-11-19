$ErrorActionPreference = 'Stop'
$build = $PSScriptRoot
$extDir = Join-Path $build 'extension'
$nhDir = Join-Path $build 'native-host'
$trayExe = Join-Path $build 'tray\tray.exe'
$nm = Join-Path $nhDir 'com.shokz.battery.json'

Write-Output '=== Bluetooth Battery setup ==='

# 1) key + id
$raw = (Get-Content -LiteralPath (Join-Path $build 'key.json') -Raw) -replace [char]0xFEFF, ''
$kj = $raw | ConvertFrom-Json
$id = $kj.id
Write-Output ('Extension ID: ' + $id)

# 2) ensure extension manifest has the pinned key
$mp = Join-Path $extDir 'manifest.json'
$mt = [IO.File]::ReadAllText($mp, [Text.Encoding]::UTF8)
if ($mt.Contains('__KEY__')) {
  $mt = $mt.Replace('__KEY__', $kj.key)
  [IO.File]::WriteAllText($mp, $mt, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output 'Injected key into extension/manifest.json'
}

# 3) native messaging host manifest
$hostPath = Join-Path $nhDir 'host.exe'
$obj = [ordered]@{
  name            = 'com.shokz.battery'
  description     = 'Shokz battery reader (Windows PnP DEVPKEY_Bluetooth_Battery)'
  path            = $hostPath
  type            = 'stdio'
  allowed_origins = @("chrome-extension://$id/")
}
$obj | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $nm -Encoding UTF8
Write-Output ('Wrote native host manifest: ' + $nm)

# 4) register host (Chrome + Edge)
foreach ($root in @(
    'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.shokz.battery',
    'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.shokz.battery')) {
  New-Item -Path $root -Force | Out-Null
  Set-Item -Path $root -Value $nm
  Write-Output ('Registered: ' + $root)
}

# 5) tray auto-start (HKCU Run) + start it now
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $run -Name 'ShokzBatteryTray' -Value ('"' + $trayExe + '"')
Write-Output 'Tray set to auto-start.'
if (-not (Get-Process -Name tray -ErrorAction SilentlyContinue)) {
  Start-Process -FilePath $trayExe
  Write-Output 'Tray started.'
} else {
  Write-Output 'Tray already running.'
}

# 6) open the Chrome extensions page
try { Start-Process 'chrome.exe' 'chrome://extensions/' } catch { }

Write-Output ''
Write-Output '=== DONE ==='
Write-Output 'Next (one time): on chrome://extensions, turn on Developer mode,'
Write-Output ('click "Load unpacked" and choose: ' + $extDir)
