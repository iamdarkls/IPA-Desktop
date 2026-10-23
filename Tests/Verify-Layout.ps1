$projectRoot=Split-Path $PSScriptRoot -Parent
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-layout-'+[Guid]::NewGuid().ToString('N'))
try {
 $zip=Join-Path $projectRoot ('IPA-Desktop-'+[IpaDesktop.GuiUpdater]::CurrentVersion+'-Windows.zip')
 if(!(Test-Path -LiteralPath $zip)){throw 'Package must be created before layout verification'}
 $stage=Join-Path $temp 'stage'
 $target=Join-Path $temp 'legacy install'
 New-Item -ItemType Directory -Path $stage,$target -Force|Out-Null
 [IO.Compression.ZipFile]::ExtractToDirectory($zip,$stage)
 # Old 0.1.6-0.1.12 updaters read root paths only.
 $legacy=@('IPA-Desktop.exe','IPA-Desktop.exe.config','MainWindow.xaml','App.ico','Update-App.ps1','Core.cs','App.cs','IpaMetadata.cs','WindowTheme.cs','GuiUpdater.cs','Build.cmd','Start.cmd','README.md','LICENSE.txt','Verify.ps1','Verify-IpaMetadata.ps1','Verify-Updater.ps1')
 foreach($file in $legacy) {
  if(!(Test-Path -LiteralPath (Join-Path $stage $file))){throw "Missing legacy update entry: $file"}
  [IO.File]::WriteAllText((Join-Path $target $file),'old fixture')
 }
 [IO.File]::WriteAllText((Join-Path $target 'keep.ipa'),'keep')
 & (Join-Path $PSScriptRoot 'Fixtures/Legacy-Update-App.ps1') -Stage $stage -Target $target -NoRestart
 [IpaDesktop.InstallationLayout]::Ensure($target)
 foreach($file in [IpaDesktop.GuiUpdater]::PackageFiles) {
  if(!(Test-Path -LiteralPath (Join-Path $target $file))){throw "Legacy update did not restore organized file: $file"}
 }
 foreach($file in @('App.ico','Update-App.ps1','Core.cs','App.cs','IpaMetadata.cs','WindowTheme.cs','GuiUpdater.cs','Verify.ps1','Verify-IpaMetadata.ps1','Verify-Updater.ps1')) {
  if(Test-Path -LiteralPath (Join-Path $target $file)){throw "Migration copy left in root: $file"}
 }
 if([IO.File]::ReadAllText((Join-Path $target 'keep.ipa')) -ne 'keep'){throw 'User IPA modified by layout migration'}
 [IpaDesktop.InstallationLayout]::Ensure($target)
 # A user-modified root file must survive migration cleanup.
 [IO.File]::WriteAllText((Join-Path $target 'Verify.ps1'),'custom user test')
 [IpaDesktop.InstallationLayout]::Ensure($target)
 if([IO.File]::ReadAllText((Join-Path $target 'Verify.ps1')) -ne 'custom user test'){throw 'User-modified root file removed'}
 Write-Host 'PASS: legacy updater migration; organized resources; repeat startup; preserved user files'
 $clean=Join-Path $temp 'clean extraction'
 [IO.Compression.ZipFile]::ExtractToDirectory($zip,$clean)
 [IpaDesktop.InstallationLayout]::Ensure($clean)
 $extract=Join-Path $temp 'organized update/stage'
 [IpaDesktop.GuiUpdater]::Extract($zip,$extract)
 & (Join-Path $projectRoot 'Scripts/Update-App.ps1') -Stage $extract -Target $clean -NoRestart
 foreach($file in [IpaDesktop.GuiUpdater]::PackageFiles) {
  $expected=[IO.File]::ReadAllBytes((Join-Path $extract $file))
  $actual=[IO.File]::ReadAllBytes((Join-Path $clean $file))
  if([Convert]::ToBase64String($expected) -ne [Convert]::ToBase64String($actual)){throw "Organized update changed file: $file"}
 }
 # Build the migrated portable folder without repository Source or old root copies.
 Push-Location $target
 try {& cmd.exe /d /c 'Build.cmd < nul';if($LASTEXITCODE -ne 0){throw 'Migrated portable compilation failed'}}finally{Pop-Location}
 Write-Host 'PASS: organized extraction/update and migrated portable compilation'
 # A checkout build must regenerate edited canonical sources without a manual sync.
 $checkout=Join-Path $temp 'checkout build'
 [IO.Compression.ZipFile]::ExtractToDirectory($zip,$checkout)
 Copy-Item -LiteralPath (Join-Path $projectRoot 'Source') -Destination $checkout -Recurse
 $canonical=Join-Path $checkout 'Source/Domain/Updates/AppInfo.cs'
 [IO.File]::AppendAllText($canonical,"`r`n// automatic-build-sync-fixture`r`n")
 Push-Location $checkout
 try {
  & cmd.exe /d /c 'Build.cmd < nul'
  if($LASTEXITCODE -ne 0){throw 'Checkout build did not regenerate canonical sources'}
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File Scripts/Sync-Sources.ps1 -Check
  if($LASTEXITCODE -ne 0){throw 'Checkout build left stale generated snapshots'}
 }finally{Pop-Location}
 if(!([IO.File]::ReadAllText((Join-Path $checkout 'Generated/Core.cs'))).Contains('automatic-build-sync-fixture')){throw 'Build compiled stale generated sources'}
 Write-Host 'PASS: checkout compilation synchronizes edited canonical sources automatically'
}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
