param([int]$X, [int]$Y, [string]$Title = "GrayScope 2", [switch]$Double, [switch]$Right)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class M {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
}
"@
[M]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
$h = (Get-Process GrayScope2 | Where-Object { $_.MainWindowTitle -eq $Title } | Select-Object -First 1).MainWindowHandle
if (-not $h) { "window not found"; exit 1 }
[M]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
$r = New-Object M+RECT
[M]::GetWindowRect($h, [ref]$r) | Out-Null
[M]::SetCursorPos($r.L + $X, $r.T + $Y) | Out-Null
$down = if ($Right) { 0x0008 } else { 0x0002 }
$up = if ($Right) { 0x0010 } else { 0x0004 }
$n = if ($Double) { 2 } else { 1 }
for ($i = 0; $i -lt $n; $i++) {
  [M]::mouse_event($down, 0, 0, 0, [IntPtr]::Zero); [M]::mouse_event($up, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60
}
"clicked $X,$Y"
