param(
  [ValidateSet("set", "invoke", "keys", "list", "toggle")] [string]$Action,
  [string]$Name,
  [string]$Value,
  [string]$Window = "GrayScope 2"
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$proc = Get-Process GrayScope2 | Where-Object { $_.MainWindowTitle -eq $Window } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
  (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Window)))
if (-not $win) { "window not found"; exit 1 }

function Find($n, $type = $null) {
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $n)
  if ($type) { $cond = New-Object System.Windows.Automation.AndCondition($cond, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type))) }
  $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if (-not $el) { throw "element '$n' not found" }
  return $el
}

switch ($Action) {
  "set" {
    $el = Find $Name ([System.Windows.Automation.ControlType]::Edit)
    $vp = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $vp.SetValue($Value); "set $Name"
  }
  "invoke" {
    $el = Find $Name
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "invoked $Name"
  }
  "toggle" {
    $el = Find $Name
    $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); "toggled $Name"
  }
  "keys" {
    $win.SetFocus(); Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($Value); "sent $Value"
  }
  "list" {
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { $n = $e.Current.Name; if ($n) { "{0} | {1}" -f $e.Current.ControlType.ProgrammaticName, $n } }
  }
}
