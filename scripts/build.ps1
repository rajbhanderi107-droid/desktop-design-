# Builds both apps from this folder and installs them where Windows starts them from.
#   powershell -ExecutionPolicy Bypass -File scripts\build.ps1            build + restart both
#   powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Only TaskbarGlass
param([ValidateSet('All', 'TaskbarGlass', 'DeskWidgets')][string]$Only = 'All')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
$icon = "$repo\apps\TaskbarGlass\glass.ico"

function Build($name, [string[]]$refs) {
    $dest = "$env:LOCALAPPDATA\$name"
    New-Item -ItemType Directory -Force $dest | Out-Null
    $out = "$dest\$name.new.exe"
    & $csc /nologo /target:winexe /optimize "/out:$out" @refs "/win32icon:$icon" "$repo\apps\$name\$name.cs"
    if ($LASTEXITCODE -ne 0) { throw "$name failed to compile" }
    Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 1
    Move-Item $out "$dest\$name.exe" -Force
    Copy-Item $icon "$dest\glass.ico" -Force
    Start-Process "$dest\$name.exe"
    "$name built and restarted"
}

$common = '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll'
if ($Only -in 'All', 'TaskbarGlass') {
    Build 'TaskbarGlass' ($common + '/r:Microsoft.CSharp.dll', "/r:$wpf\UIAutomationClient.dll", "/r:$wpf\UIAutomationTypes.dll", "/r:$wpf\WindowsBase.dll")
}
if ($Only -in 'All', 'DeskWidgets') {
    Build 'DeskWidgets' ($common + '/r:System.Web.Extensions.dll')
}
