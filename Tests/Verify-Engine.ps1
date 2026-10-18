param([switch]$Live)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-engine-'+[Guid]::NewGuid().ToString('N'))
$ct=[Threading.CancellationToken]::None
try {
 $oldBins=Join-Path $temp 'Versions/legacy/MainApp/windows_amd64'
 [IO.Directory]::CreateDirectory($oldBins)|Out-Null
 foreach($name in @('ipatool-old.exe','idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe','libimobiledevice-1.0.dll','libplist-2.0.dll','dependency.dll','Launcher.ps1')){[IO.File]::WriteAllText((Join-Path $oldBins $name),'fixture')}
 $support=Join-Path $temp 'Versions/devices/windows_amd64'
 [IO.Directory]::CreateDirectory($support)|Out-Null
 foreach($name in @('idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe','libimobiledevice-1.0.dll','libplist-2.0.dll','dependency.dll')){Copy-Item (Join-Path $oldBins $name) $support}
 [IO.Directory]::CreateDirectory((Join-Path $temp 'Versions/devices/LICENSES'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $temp 'Versions/devices/sources.json'),'{}')
 [IpaDesktop.EngineUpdater]::CheckDevicePackage((Join-Path $temp 'Versions/devices'))
 if(Test-Path (Join-Path $support 'ipatool-old.exe')){throw 'Device import retained bundled ipatool'}
 if(Test-Path (Join-Path $support 'Launcher.ps1')){throw 'Device import retained scripts'}
 if(!(Test-Path (Join-Path $support 'dependency.dll'))){throw 'Device import lost DLL dependencies'}
 if(![IpaDesktop.EngineUpdater]::HasDeviceTools((Join-Path $temp 'Versions/devices'))){throw 'Device import is incomplete'}
 $engine=New-Object IpaDesktop.Engine($temp)
 $engine.Settings.Current='direct';$engine.Settings.Source='Sorvigolova/ipatool';$engine.Settings.Version='1.1.8'
 $engine.Settings.DeviceSource='iamdarkls/IPA-Desktop';$engine.Settings.DeviceCurrent='devices';$engine.Settings.DeviceVersion='4.2.2'
 $engine.Settings.Previous='legacy';$engine.Settings.PreviousVersion='4.2.2'
 $engine.Settings.AutoUpdate=$false;$engine.Settings.CheckGuiUpdates=$false
 $direct=Join-Path $temp 'Versions/direct/windows_amd64/nested'
 [IO.Directory]::CreateDirectory($direct)|Out-Null
 [IO.File]::WriteAllText((Join-Path $direct 'ipatool.exe'),'fixture')
 if($engine.Binary('ipatool*.exe') -ne (Join-Path $direct 'ipatool.exe')){throw 'ipatool did not use direct release'}
 if($engine.Binary('ideviceinstaller.exe') -ne (Join-Path $support 'ideviceinstaller.exe')){throw 'Device tool used engine folder'}
 $engine.Save();$engine.Rollback()
 if($engine.Settings.Source -or $engine.Settings.DeviceCurrent -ne 'devices'){throw 'Legacy rollback lost source or device tools'}
 if($engine.Binary('ipatool*.exe') -ne (Join-Path $oldBins 'ipatool-old.exe')){throw 'Legacy rollback did not restore bundled engine'}
 $engine.Rollback()
 $restored=New-Object IpaDesktop.Engine($temp)
 if($restored.Settings.Source -ne 'Sorvigolova/ipatool' -or $restored.Settings.DeviceCurrent -ne 'devices' -or $restored.Settings.AutoUpdate -or $restored.Settings.CheckGuiUpdates){throw 'Rollback/serialization lost independent state'}
 $engine.Settings.PreviousDeviceCurrent='legacy';$engine.Settings.PreviousDeviceVersion='old-support'
 $engine.Settings.AutoUpdateDeviceTools=$true;$engine.Save()
 $engineCurrent=$engine.Settings.Current
 $engine.RollbackDeviceTools()
 if($engine.Settings.DeviceCurrent -ne 'legacy' -or $engine.Settings.AutoUpdateDeviceTools -or $engine.Settings.Current -ne $engineCurrent){throw 'Device rollback affected ipatool or did not disable its own updates'}
 $engine.RollbackDeviceTools()
 if($engine.Settings.DeviceCurrent -ne 'devices' -or $engine.Settings.Current -ne $engineCurrent){throw 'Second device rollback lost independent history'}
 $engine.Rollback();$engine.Rollback()
 if($engine.Settings.AutoUpdateDeviceTools -or $engine.Settings.PreviousDeviceCurrent -ne 'legacy'){throw 'ipatool rollback lost device update settings/history'}
 $roundTrip=New-Object IpaDesktop.Engine($temp)
 if($roundTrip.Settings.AutoUpdateDeviceTools -or $roundTrip.Settings.PreviousDeviceCurrent -ne 'legacy'){throw 'Device update preferences were not persisted'}
 $oldStateRoot=Join-Path $temp 'old-state'
 [IO.Directory]::CreateDirectory($oldStateRoot)|Out-Null
 [IO.File]::WriteAllText((Join-Path $oldStateRoot 'state.json'),'{}')
 $oldStateEngine=New-Object IpaDesktop.Engine($oldStateRoot)
 if(!$oldStateEngine.Settings.AutoUpdateDeviceTools){throw 'Old settings did not default to device update checks'}
 Write-Host 'PASS: independent device rollback, repeated rollback, preserved update flags/history and legacy defaults'
 $asset=New-Object IpaDesktop.Asset
 $asset.name='windows_amd64_ipatool-cpp.zip';$asset.size=1
 $asset.browser_download_url='https://github.com/Sorvigolova/ipatool/releases/download/fixture/windows_amd64_ipatool-cpp.zip'
 $release=New-Object IpaDesktop.Release;$release.assets=@($asset)
 [IpaDesktop.EngineUpdater]::SelectAsset($release)|Out-Null
 $release.assets=@($asset,$asset);$rejected=$false
 try{[IpaDesktop.EngineUpdater]::SelectAsset($release)|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Ambiguous Windows archive accepted'}
 $release.assets=@($asset);$asset.browser_download_url='https://github.com/kda2495/IPA_Downloader/releases/download/fixture/test.zip';$rejected=$false
 try{[IpaDesktop.EngineUpdater]::SelectAsset($release)|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Bundled source accepted as direct engine'}
 $release.tag_name='device-tools-v1.0.0'
 $asset.name='IPA-DeviceTools-1.0.0-Windows-x64.zip';$asset.digest='sha256:'+('a'*64)
 $asset.browser_download_url='https://github.com/iamdarkls/IPA-Desktop/releases/download/device-tools-v1.0.0/'+$asset.name
 [IpaDesktop.EngineUpdater]::SelectDeviceAsset($release)|Out-Null
 $asset.browser_download_url='https://github.com/kda2495/IPA_Downloader/releases/download/device-tools-v1.0.0/'+$asset.name
 $rejected=$false;try{[IpaDesktop.EngineUpdater]::SelectDeviceAsset($release)|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Device update accepted IPA_Downloader binary source'}
 $asset.browser_download_url='https://github.com/iamdarkls/IPA-Desktop/releases/download/device-tools-v1.0.0/'+$asset.name
 $asset.digest=$null;$rejected=$false;try{[IpaDesktop.EngineUpdater]::SelectDeviceAsset($release)|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Device update accepted missing GitHub digest'}
 Write-Host 'PASS: own device package, independent tool lookup, legacy/direct rollback, persisted settings, release selection'
 if($Live){
  $liveRoot=Join-Path $temp 'live'
  $liveEngine=New-Object IpaDesktop.Engine($liveRoot)
  $latest=$liveEngine.Check($ct).GetAwaiter().GetResult()
  $liveEngine.Install($latest,$ct).GetAwaiter().GetResult()
  if($liveEngine.Settings.Source -ne 'Sorvigolova/ipatool' -or !$liveEngine.Settings.DeviceCurrent){throw 'Fresh live install did not commit independent sources'}
  $liveEngine.Binary('ipatool*.exe')|Out-Null
  foreach($pattern in @('idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe')){$liveEngine.Binary($pattern)|Out-Null}
  if(@(Get-ChildItem (Join-Path $liveRoot ('Versions/'+$liveEngine.Settings.DeviceCurrent)) -Filter 'ipatool*.exe' -Recurse).Count){throw 'Fresh install retained bundled engine'}
  # Same-version legacy migration must still select direct upstream and reuse device tools.
  $prior=$liveEngine.Settings.Current
  $devices=$liveEngine.Settings.DeviceCurrent
  $liveEngine.Settings.Source=$null;$liveEngine.Save()
  $liveEngine.Install($latest,$ct).GetAwaiter().GetResult()
  if($liveEngine.Settings.Current -eq $prior -or $liveEngine.Settings.Previous -ne $prior -or $liveEngine.Settings.DeviceCurrent -ne $devices){throw 'Live migration did not preserve device tools/previous engine'}
  $liveEngine.Rollback()
  if($liveEngine.Settings.Current -ne $prior -or $liveEngine.Settings.DeviceCurrent -ne $devices){throw 'Live rollback failed'}
  $deviceRelease=$liveEngine.CheckDeviceTools($ct).GetAwaiter().GetResult()
  if(!$liveEngine.DeviceToolsCurrent($deviceRelease)){throw 'Current support release was not recognized'}
  $liveEngine.Settings.DeviceSource=$null
  if($liveEngine.DeviceToolsCurrent($deviceRelease)){throw 'Same-version legacy source prevented migration'}
  $before=$liveEngine.Settings.Current
  $liveEngine.Settings.AutoUpdate=$false;$liveEngine.Settings.CheckGuiUpdates=$false;$liveEngine.Settings.AutoUpdateDeviceTools=$false
  $liveEngine.InstallDeviceTools($deviceRelease,$ct).GetAwaiter().GetResult()
  if($liveEngine.Settings.Current -ne $before -or $liveEngine.Settings.DeviceCurrent -eq $devices -or $liveEngine.Settings.PreviousDeviceCurrent -ne $devices -or $liveEngine.Settings.AutoUpdate -or $liveEngine.Settings.AutoUpdateDeviceTools -or $liveEngine.Settings.CheckGuiUpdates){throw 'Device update replaced ipatool, lost rollback or preferences'}
  if(!$liveEngine.DeviceToolsCurrent($deviceRelease)){throw 'Updated device release was not recognized'}
  $liveEngine.RollbackDeviceTools()
  if($liveEngine.Settings.DeviceCurrent -ne $devices -or $liveEngine.Settings.Current -ne $before){throw 'Live device rollback changed ipatool'}
  $saved=[IO.File]::ReadAllText((Join-Path $liveRoot 'state.json'))
  $versionsBefore=@(Get-ChildItem (Join-Path $liveRoot 'Versions') -Directory).Count
  $lock=[IO.File]::Open((Join-Path $liveRoot 'state.json'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  $failed=$false
  try{try{$liveEngine.InstallDeviceTools($deviceRelease,$ct).GetAwaiter().GetResult()}catch{$failed=$true}}finally{$lock.Dispose()}
  if(!$failed -or [IO.File]::ReadAllText((Join-Path $liveRoot 'state.json')) -ne $saved -or $liveEngine.Settings.DeviceCurrent -ne $devices -or @(Get-ChildItem (Join-Path $liveRoot 'Versions') -Directory).Count -ne $versionsBefore){throw 'Failed settings save changed active support or retained uncommitted stage'}
  $cancel=New-Object Threading.CancellationTokenSource;$cancel.Cancel();$failed=$false
  try{try{$liveEngine.InstallDeviceTools($deviceRelease,$cancel.Token).GetAwaiter().GetResult()}catch{$failed=$true}}finally{$cancel.Dispose()}
  if(!$failed -or $liveEngine.Settings.DeviceCurrent -ne $devices){throw 'Canceled device update changed active support'}
  # Проверяем оба обновления на настоящих Windows-архивах, без входа в Apple Account.
  foreach($tag in @('1.1.9','1.2.0')){
   $info=Invoke-RestMethod -Uri ('https://api.github.com/repos/Sorvigolova/ipatool/releases/tags/'+$tag) -Headers @{'User-Agent'='IPA-Desktop-Compatibility'}
   $pinned=New-Object IpaDesktop.Release;$pinned.tag_name=$info.tag_name
   $pinned.assets=@($info.assets|ForEach-Object{
    $item=New-Object IpaDesktop.Asset
    $item.name=$_.name;$item.size=$_.size;$item.digest=$_.digest;$item.browser_download_url=$_.browser_download_url
    $item
   })
   $priorEngine=$liveEngine.Settings.Current;$priorTools=$liveEngine.Settings.DeviceCurrent
   $liveEngine.Install($pinned,$ct).GetAwaiter().GetResult()
   if($liveEngine.Settings.Version -ne $tag -or $liveEngine.Settings.Source -ne 'Sorvigolova/ipatool' -or $liveEngine.Settings.DeviceCurrent -ne $priorTools){throw "ipatool $tag install affected tools or selected wrong release"}
   # Reach the password prompt and type one fixture character without Enter: no Apple password/code is submitted and no login request starts.
   $loginStop=New-Object Threading.CancellationTokenSource;$loginStop.CancelAfter(10000)
   $loginProcess=$null;$reachedPassword=$false
   try {
    $factory=New-Object IpaDesktop.ConPtyLoginFactory
    $loginProcess=$factory.Start($liveEngine.Binary('ipatool*.exe'),$liveEngine.Downloads)
    $protocol=New-Object IpaDesktop.AppleLoginProtocol
    while(!$reachedPassword) {
     $chunk=$loginProcess.ReadAsync($loginStop.Token).GetAwaiter().GetResult()
     if($null -eq $chunk){throw 'Actual ipatool exited before interactive password prompt'}
     $prompt=$protocol.Append($chunk)
     if($prompt -eq [IpaDesktop.AppleLoginPrompt]::Identifier){
      $identifier=New-Object Security.SecureString
      try {foreach($character in 'fixture@example.com'.ToCharArray()){$identifier.AppendChar($character)};$identifier.MakeReadOnly();$loginProcess.SendAsync($identifier,$loginStop.Token).GetAwaiter().GetResult()}finally{$identifier.Dispose()}
     }
     elseif($prompt -eq [IpaDesktop.AppleLoginPrompt]::Password){$reachedPassword=$true}
    }
    # Cause actual _getch echo/repaint without Enter: Apple authentication is never called.
    $testCharacter=New-Object Security.SecureString
    try {$testCharacter.AppendChar('x');$testCharacter.MakeReadOnly();$loginProcess.SendCharactersAsync($testCharacter,$loginStop.Token).GetAwaiter().GetResult()}finally{$testCharacter.Dispose()}
    $echoSeen=$false
    while(!$echoSeen) {
     $chunk=$loginProcess.ReadAsync($loginStop.Token).GetAwaiter().GetResult()
     if($null -eq $chunk){throw 'Actual ipatool exited during password-character echo'}
     if($protocol.Append($chunk) -ne [IpaDesktop.AppleLoginPrompt]::None){throw 'Actual ipatool repaint requested credentials twice'}
     if($chunk.Contains('*')){$echoSeen=$true}
    }
   }finally{if($loginProcess){$loginProcess.Dispose()};$loginStop.Dispose()}
   if(!$reachedPassword){throw 'Actual ipatool does not support hidden interactive input'}
   Write-Host ('PASS: actual ipatool '+$tag+' accepts ConPTY TTY and password-character echo/redraw without duplicate requests; stopped before Enter/network login')
   $liveEngine.Rollback()
   if($liveEngine.Settings.Current -ne $priorEngine -or $liveEngine.Settings.DeviceCurrent -ne $priorTools){throw "ipatool $tag rollback lost independent state"}
   Write-Host ('PASS: actual Windows ipatool '+$tag+' archive, SHA-256, CLI command contract, install and rollback')
  }
  $probe=New-Object IpaDesktop.ProcessRunner($liveRoot)
  foreach($name in @('idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe')){
   $help=$probe.Run($liveEngine.Binary($name),[string[]]@('--help'),$ct,$false,$false,$null).GetAwaiter().GetResult()
   if($help.Code -ne 0 -or !$help.Output.Contains('--network')){throw "Network transport unsupported by actual $name"}
  }
  Write-Host 'PASS: actual Windows device tools advertise network transport in all three CLI commands'
  Write-Host 'PASS: actual device CLI versions; independent device update/rollback; current release; save-failure recovery; cancellation'
  Write-Host ('PASS: actual upstream Windows ZIP '+$latest.tag_name+'; CLI startup/commands; fresh device import; migration; rollback')
 }
} finally {if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
