param([string]$Out)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)] public struct ABD{public int cbSize;public IntPtr hWnd;public uint cb;public uint edge;public int l,t,r,b;public IntPtr lParam;}
public class PT{[DllImport("shell32.dll")]public static extern IntPtr SHAppBarMessage(uint m,ref ABD d);[DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string c,string n);[DllImport("user32.dll")]public static extern bool SetProcessDPIAware();}
'@
[PT]::SetProcessDPIAware() | Out-Null
$d = New-Object ABD; $d.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($d); $d.hWnd = [PT]::FindWindow('Shell_TrayWnd', $null)
$sheet = New-Object Drawing.Bitmap 1920, 400
$sg = [Drawing.Graphics]::FromImage($sheet)
# first reveal lets Taskbar Glass learn the icon positions; then hide and reveal again to watch the pop
$d.lParam = [IntPtr]2; [PT]::SHAppBarMessage(10, [ref]$d) | Out-Null; Start-Sleep -Milliseconds 2600
$d.lParam = [IntPtr]1; [PT]::SHAppBarMessage(10, [ref]$d) | Out-Null; Start-Sleep -Milliseconds 1200
$d.lParam = [IntPtr]2; [PT]::SHAppBarMessage(10, [ref]$d) | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
$i = 0
foreach ($ms in 70, 160, 260, 600) {
    while ($sw.ElapsedMilliseconds -lt $ms) { Start-Sleep -Milliseconds 5 }
    $b = New-Object Drawing.Bitmap 1920, 80; $g = [Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(0, 1000, 0, 0, $b.Size)
    $sg.DrawImage($b, 0, $i * 100); $g.Dispose(); $b.Dispose(); $i++
}
$sheet.Save($Out)
"captured"
# Taskbar Glass turns auto-hide back on by itself within ~3 s
