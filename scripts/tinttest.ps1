param([int]$State = 4, [string]$Color = '3423588560', [int]$Seconds = 6)
Add-Type @'
using System;using System.Runtime.InteropServices;
public class AC{[StructLayout(LayoutKind.Sequential)]public struct P{public int S,F;public uint C;public int A;}[StructLayout(LayoutKind.Sequential)]public struct D{public int At;public IntPtr Da;public int Sz;}
[DllImport("user32.dll")]static extern int SetWindowCompositionAttribute(IntPtr h,ref D d);[DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string c,string n);
public static void Set(int s,uint c){var p=new P{S=s,F=2,C=c};int z=Marshal.SizeOf(p);IntPtr q=Marshal.AllocHGlobal(z);Marshal.StructureToPtr(p,q,false);var d=new D{At=19,Da=q,Sz=z};SetWindowCompositionAttribute(FindWindow("Shell_TrayWnd",null),ref d);Marshal.FreeHGlobal(q);}}
'@
$c = [uint32]::Parse($Color)
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) { [AC]::Set($State, $c); Start-Sleep -Milliseconds 50 }
