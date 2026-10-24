param([Parameter(Mandatory=$true)][string]$Stage,[Parameter(Mandatory=$true)][string]$Target,[int]$ParentId=0,[switch]$NoRestart)
$ErrorActionPreference='Stop'
$files=@('IPA-Desktop.exe','IPA-Desktop.exe.config','MainWindow.xaml','Build.cmd','Start.cmd','README.md','LICENSE.txt','Docs/ARCHITECTURE.md','Assets/App.ico','Assets/App.svg','Scripts/Update-App.ps1','Scripts/Sync-Sources.ps1','Scripts/Resources.rsp','Generated/Core.cs','Generated/App.cs','Generated/IpaMetadata.cs','Generated/WindowTheme.cs','Generated/GuiUpdater.cs','Tests/Verify.ps1','Tests/Verify-IpaMetadata.ps1','Tests/Verify-Updater.ps1','Tests/Verify-Design.ps1','Tests/Verify-Services.ps1','Tests/Verify-AppleLogin.ps1', 'Tests/Verify-Engine.ps1','Tests/Verify-WorkflowFeatures.ps1','Tests/Verify-Layout.ps1','Tests/Fixtures/Legacy-Update-App.ps1')
$backup=Join-Path (Split-Path $Stage -Parent) 'backup'
$changed=New-Object 'System.Collections.Generic.List[string]'
$backedUp=New-Object 'System.Collections.Generic.HashSet[string]'
try {
 if($ParentId -gt 0) {
  $parent=Get-Process -Id $ParentId -ErrorAction SilentlyContinue
  if($parent -and !$parent.WaitForExit(60000)){throw 'Application did not close within 60 seconds.'}
 }
 $guard=New-Object System.Threading.Mutex($false,'Local\IPA-Desktop-0.1')
 try {$locked=$guard.WaitOne(10000)}catch [System.Threading.AbandonedMutexException]{$locked=$true}
 if(!$locked){throw 'Another IPA Desktop instance is running.'}
 foreach($file in $files) {
  if(!(Test-Path -LiteralPath (Join-Path $Stage $file) -PathType Leaf)){throw "Missing file: $file"}
  $destination=Join-Path $Target $file
  $check=$destination
  while($check -and $check -ne (Split-Path $Target -Parent)) {
   if(Test-Path -LiteralPath $check) {
    if((Get-Item -LiteralPath $check).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked destination: $file"}
   }
   $check=Split-Path $check -Parent
  }
 }
 New-Item -ItemType Directory -Path $backup | Out-Null
 foreach($file in $files) {
  $destination=Join-Path $Target $file
  if(Test-Path -LiteralPath $destination) {New-Item -ItemType Directory -Path (Split-Path (Join-Path $backup $file) -Parent) -Force|Out-Null;Copy-Item -LiteralPath $destination -Destination (Join-Path $backup $file);[void]$backedUp.Add($file)}
 }
 foreach($file in $files) {
  New-Item -ItemType Directory -Path (Split-Path (Join-Path $Target $file) -Parent) -Force|Out-Null
  $changed.Add($file)
  Copy-Item -LiteralPath (Join-Path $Stage $file) -Destination (Join-Path $Target $file) -Force
 }
 $guard.ReleaseMutex();$locked=$false
 if(!$NoRestart){Start-Process -FilePath (Join-Path $Target 'IPA-Desktop.exe') -WorkingDirectory $Target}
 # Keep the previous files for recovery; abandoned update directories expire after 7 days.
} catch {
 $failure=$_
 foreach($file in $changed) {
  try {
   if($backedUp.Contains($file)){Copy-Item -LiteralPath (Join-Path $backup $file) -Destination (Join-Path $Target $file) -Force}
   elseif(Test-Path -LiteralPath (Join-Path $Target $file)){Remove-Item -LiteralPath (Join-Path $Target $file) -Force}
  }catch{}
 }
 $failure | Out-String | Set-Content -LiteralPath (Join-Path (Split-Path $Stage -Parent) 'update-error.txt')
 if(!$NoRestart) {
  Add-Type -AssemblyName PresentationFramework
  [Windows.MessageBox]::Show('Update failed. The previous files were restored where possible. See update-error.txt in '+(Split-Path $Stage -Parent),'IPA Desktop')|Out-Null
 }
 throw $failure
} finally {
 if($locked){$guard.ReleaseMutex()}
 if($guard){$guard.Dispose()}
}
