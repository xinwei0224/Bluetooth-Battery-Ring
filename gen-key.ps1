# Generate an RSA key and the Chrome extension ID that the manifest "key" pins.
# Builds the DER SubjectPublicKeyInfo by hand (works on PowerShell 5.1 / .NET Framework).
# WARNING: running this creates a NEW random key -> a NEW extension ID. If you use it,
# you must also update extension/manifest.json ("key") and the ExtId in installer/installer.cs
# and setup.ps1. Normally you do NOT need to run this.
$ErrorActionPreference = 'Stop'

function Write-DerLen([System.IO.Stream]$s, [int]$len) {
  if ($len -lt 0x80) { $s.WriteByte([byte]$len); return }
  $tmp = $len
  $bytes = New-Object System.Collections.Generic.List[byte]
  while ($tmp -gt 0) { $bytes.Insert(0, [byte]($tmp -band 0xFF)); $tmp = $tmp -shr 8 }
  $s.WriteByte([byte](0x80 -bor $bytes.Count))
  foreach ($b in $bytes) { $s.WriteByte($b) }
}

function Write-Tlv([System.IO.Stream]$s, [byte]$tag, [byte[]]$val) {
  $s.WriteByte($tag)
  Write-DerLen $s $val.Length
  $s.Write($val, 0, $val.Length)
}

function To-IntBytes([byte[]]$b) {
  $i = 0
  while ($i -lt $b.Length - 1 -and $b[$i] -eq 0) { $i++ }
  $r = [byte[]]$b[$i..($b.Length - 1)]
  if (($r[0] -band 0x80) -ne 0) { $r = [byte[]](,[byte]0 + $r) }
  return $r
}

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$p = $rsa.ExportParameters($false)
$mod = To-IntBytes $p.Modulus
$exp = To-IntBytes $p.Exponent

$inner = New-Object System.IO.MemoryStream
Write-Tlv $inner 0x02 $mod
Write-Tlv $inner 0x02 $exp
$innerBytes = $inner.ToArray()

$rsaPub = New-Object System.IO.MemoryStream
Write-Tlv $rsaPub 0x30 $innerBytes
$rsaPubBytes = $rsaPub.ToArray()

$oidBytes = [byte[]](0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01)
$algId = New-Object System.IO.MemoryStream
Write-Tlv $algId 0x06 $oidBytes
$algId.WriteByte(0x05)
$algId.WriteByte(0x00)
$algIdBytes = $algId.ToArray()

$algSeq = New-Object System.IO.MemoryStream
Write-Tlv $algSeq 0x30 $algIdBytes
$algSeqBytes = $algSeq.ToArray()

$bitVal = [byte[]](,[byte]0x00 + $rsaPubBytes)
$bitStr = New-Object System.IO.MemoryStream
Write-Tlv $bitStr 0x03 $bitVal
$bitStrBytes = $bitStr.ToArray()

$spki = New-Object System.IO.MemoryStream
Write-Tlv $spki 0x30 ([byte[]]($algSeqBytes + $bitStrBytes))
$spkiBytes = $spki.ToArray()

$keyB64 = [Convert]::ToBase64String($spkiBytes)
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = $sha.ComputeHash($spkiBytes)
$id = ''
for ($i = 0; $i -lt 16; $i++) {
  $id += [char](97 + (($hash[$i] -shr 4) -band 0xF))
  $id += [char](97 + ($hash[$i] -band 0xF))
}

$outDir = $PSScriptRoot
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
@{ key = $keyB64; id = $id } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outDir 'key.json') -Encoding UTF8

Write-Output ("EXT_ID=" + $id)
Write-Output ("KEY_LEN=" + $keyB64.Length)
Write-Output ("SPKI_BYTES=" + $spkiBytes.Length)
