param([string]$Prefix, [string]$Out, [string]$Invoke)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class D { [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v); }
"@
[D]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
$appPid = (Get-Process GrayScope2).Id
$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appPid))) |
  Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name.StartsWith($Prefix) }
$w = $wins | Select-Object -First 1
if (-not $w) { "no window $Prefix"; exit 1 }
if ($Out) {
  $h = [IntPtr]$w.Current.NativeWindowHandle; $r = New-Object D+RECT; [D]::GetWindowRect($h, [ref]$r) | Out-Null
  $b = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($b)
  $d = $g.GetHdc(); [D]::PrintWindow($h, $d, 2) | Out-Null; $g.ReleaseHdc($d); $b.Save($Out); "saved $Out"
}
if ($Invoke) {
  $el = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Invoke)))
  $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "invoked $Invoke"
}
