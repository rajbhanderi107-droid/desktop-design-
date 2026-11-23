param([string]$Out, [int]$Y = 1000, [int]$H = 80)
Add-Type -AssemblyName System.Drawing
Add-Type 'using System.Runtime.InteropServices;public class DpiCap{[DllImport("user32.dll")]public static extern bool SetProcessDPIAware();}'
[DpiCap]::SetProcessDPIAware() | Out-Null
$b = New-Object Drawing.Bitmap 1920, $H
$g = [Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen(0, $Y, 0, 0, $b.Size)
$b.Save($Out)
"saved"
