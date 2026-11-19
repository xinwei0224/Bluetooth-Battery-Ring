param(
  [string]$DeviceName = ''
)

# Read the battery Windows shows for a Bluetooth headset (DEVPKEY_Bluetooth_Battery),
# the real device model, AND the live connection state (via WinRT BluetoothDevice.
# ConnectionStatus). When the headset is disconnected/charging the PnP node and its
# last battery value still exist, so we report connected=false but keep the last
# battery value so the UI can show "disconnected, last known battery".
#
# Prints exactly one line of JSON on stdout.

$ErrorActionPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch {}

$battKey  = '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2'
$timeKey  = '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 7'
$cacheDir = Join-Path $env:LOCALAPPDATA 'ShokzLowBattery'
$cacheFile = Join-Path $cacheDir 'instance.txt'

function Clean-Model($fn) {
  if (-not $fn) { return $null }
  ($fn -replace '\s*(Hands-Free AG.*|Avrcp Transport.*|Stereo.*|Headset.*|Handsfree.*|Audio.*)$', '').Trim()
}

function Emit-Result($percent, $ageMs, $note, $model, $connected) {
  $dev = $model
  if (-not $dev) { if ($DeviceName) { $dev = $DeviceName } else { $dev = 'Bluetooth headset' } }
  $obj = [ordered]@{
    ok         = $true
    device     = $dev
    connected  = $connected
    percent    = $percent
    left       = $null
    right      = $null
    charging   = $false
    source     = $(if ($connected) { 'winbt' } elseif ($null -ne $percent) { 'cached' } else { 'none' })
    confidence = $(if ($connected) { 'exact' } elseif ($null -ne $percent) { 'lastknown' } else { 'unknown' })
    ageMs      = $ageMs
    note       = $note
  }
  Write-Output ($obj | ConvertTo-Json -Compress)
  exit 0
}

function Read-Battery($instanceId) {
  $out = @{ pct = $null; ageMs = [long]0 }
  if (-not $instanceId) { return $out }
  $props = Get-PnpDeviceProperty -InstanceId $instanceId -KeyName $battKey, $timeKey -ErrorAction SilentlyContinue
  if ($null -eq $props) { return $out }
  foreach ($p in @($props)) {
    if ($null -eq $p) { continue }
    if ($p.KeyName -eq $battKey) {
      if ($null -ne $p.Data -and -not ($p.Data -is [string])) {
        $v = [int]$p.Data
        if ($v -ge 0 -and $v -le 100) { $out.pct = $v }
      }
    }
    elseif ($p.KeyName -eq $timeKey) {
      if ($p.Data) {
        try {
          $dt = [datetime]$p.Data
          $ms = [long]([DateTime]::UtcNow - $dt.ToUniversalTime()).TotalMilliseconds
          if ($ms -lt 0) { $ms = 0 }
          $out.ageMs = $ms
        } catch { }
      }
    }
  }
  return $out
}

# Live connection state via WinRT. Returns $true/$false, or $null if undeterminable.
function Get-Connected([string]$instanceId) {
  try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
    $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
      $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    # MAC is embedded in the AG node instance id: ...&<12 hex>_C00000000
    $m = [regex]::Match([string]$instanceId, '&([0-9A-Fa-f]{12})_')
    if (-not $m.Success) { return $null }
    $addr = [Convert]::ToUInt64($m.Groups[1].Value, 16)
    [Windows.Devices.Bluetooth.BluetoothDevice, Windows.Devices.Bluetooth, ContentType = WindowsRuntime] | Out-Null
    $op = [Windows.Devices.Bluetooth.BluetoothDevice]::FromBluetoothAddressAsync([uint64]$addr)
    $at = $asTaskGeneric.MakeGenericMethod([Windows.Devices.Bluetooth.BluetoothDevice])
    $nt = $at.Invoke($null, @($op))
    $nt.Wait(-1) | Out-Null
    $bd = $nt.Result
    if ($null -eq $bd) { return $false }
    return ([string]$bd.ConnectionStatus -eq 'Connected')
  } catch { return $null }
}

# 1) Try cached instance id.
$instance = $null
$model = $null
if (Test-Path -LiteralPath $cacheFile) {
  $c = Get-Content -LiteralPath $cacheFile -Raw -ErrorAction SilentlyContinue
  if ($c) { $instance = $c.Trim() }
}
$res = Read-Battery $instance
if ($instance) {
  $d = Get-PnpDevice -InstanceId $instance -ErrorAction SilentlyContinue
  if ($d) { $model = Clean-Model $d.FriendlyName }
}

# 2) Cache miss: locate the Hands-Free AG node (class System), read battery + model.
if ($null -eq $instance -or $null -eq $res.pct) {
  $filter = '*Hands-Free AG*'
  if ($DeviceName) { $filter = $DeviceName + '*' }
  $cands = Get-PnpDevice -Class System -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -like $filter }
  foreach ($cand in $cands) {
    $try = Read-Battery $cand.InstanceId
    if ($null -ne $try.pct) {
      $instance = $cand.InstanceId
      $res = $try
      $model = Clean-Model $cand.FriendlyName
      break
    }
  }
  if ($instance) {
    if (-not (Test-Path -LiteralPath $cacheDir)) { New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null }
    Set-Content -LiteralPath $cacheFile -Value $instance -Encoding ASCII -ErrorAction SilentlyContinue
  }
}

# 3) Live connection state.
$connected = $false
$c = Get-Connected $instance
if ($null -ne $c) { $connected = [bool]$c }
elseif ($null -ne $res.pct) { $connected = $true }  # fallback to legacy behaviour if WinRT unavailable

if ($null -eq $res.pct -and $null -eq $instance) {
  Emit-Result $null 0 'no battery reported by Windows (headset not paired)' $model $false
}

$note = if ($connected) { 'Windows Bluetooth battery (same value as Settings)' } else { 'disconnected - showing last known battery' }
Emit-Result $res.pct $res.ageMs $note $model $connected
