$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$here\tray.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$here\tray.cs"
Write-Host "built $here\tray.exe"
