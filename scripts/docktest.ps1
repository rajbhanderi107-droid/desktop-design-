param([string]$Out)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;
public struct PT8{public int X,Y;}
public struct R8{public int l,t,r,b;}
public class DT{
[DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")]static extern void mouse_event(uint f,int dx,int dy,uint d,UIntPtr e);public static bool SetCursorPos(int x,int y){mouse_event(0x8001,(int)(x*65535L/1919),(int)(y*65535L/1079),0,UIntPtr.Zero);return true;}
[DllImport("user32.dll")]public static extern bool GetCursorPos(out PT8 p);
[DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(PT8 p);
[DllImport("user32.dll")]public static extern IntPtr GetAncestor(IntPtr h,int f);
[DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
[DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string c,string n);
[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R8 r);}
'@
[DT]::SetProcessDPIAware() | Out-Null
$orig = New-Object PT8; [DT]::GetCursorPos([ref]$orig) | Out-Null
$tray = [DT]::FindWindow('Shell_TrayWnd', $null)

function Rect { $r = New-Object R8; [DT]::GetWindowRect($tray, [ref]$r) | Out-Null; "$($r.l),$($r.t),$($r.r),$($r.b)" }
function Capture($path) { $b = New-Object Drawing.Bitmap 1920, 90; $g = [Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(0, 990, 0, 0, $b.Size); $b.Save($path); $g.Dispose(); $b.Dispose() }

[DT]::SetCursorPos(960, 500) | Out-Null; Start-Sleep -Milliseconds 900
"hidden rect: " + (Rect)
Capture ($Out -replace '\.png$', '-hidden.png')

for ($y = 1000; $y -le 1079; $y += 4) { [DT]::SetCursorPos(1300, $y) | Out-Null; Start-Sleep -Milliseconds 15 }; for ($i = 0; $i -lt 6; $i++) { [DT]::SetCursorPos(1300 + $i, 1079) | Out-Null; Start-Sleep -Milliseconds 40 }
"shown rect after 350ms: " + (Rect)
Start-Sleep -Milliseconds 400
Capture $Out
foreach ($x in 980, 1300, 1850) {
    $p = New-Object PT8; $p.X = $x; $p.Y = 1050
    $root = [DT]::GetAncestor([DT]::WindowFromPoint($p), 2); $sb = New-Object Text.StringBuilder 128; [DT]::GetClassName($root, $sb, 128) | Out-Null
    "click at x=$x reaches: $sb"
}
[DT]::SetCursorPos($orig.X, $orig.Y) | Out-Null
