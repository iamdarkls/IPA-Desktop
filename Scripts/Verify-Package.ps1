param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-avalonia-package-'+[Guid]::NewGuid().ToString('N'))
function Assert($condition,$message){if(!$condition){throw $message}}
try {
 $stage=Join-Path $temp 'stage';$legacy=Join-Path $temp 'old install';$data=Join-Path $temp 'user data'
 [IO.Directory]::CreateDirectory($legacy)|Out-Null
 [IO.Directory]::CreateDirectory($data)|Out-Null
 [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $Archive),$stage)
 $oldFiles=@(Get-Content (Join-Path $projectRoot 'Tests/Fixtures/legacy-package-files.json') -Raw|ConvertFrom-Json)
 foreach($file in $oldFiles){
  $source=Join-Path $stage $file
  Assert ((Get-Item -LiteralPath $source).Length -gt 0 -and (Get-Item -LiteralPath $source).Length -le 32MB) "Legacy extraction limit: $file"
  $target=Join-Path $legacy $file;[IO.Directory]::CreateDirectory((Split-Path $target -Parent))|Out-Null
  [IO.File]::WriteAllText($target,'old version fixture')
 }
 [IO.File]::WriteAllText((Join-Path $legacy 'keep.ipa'),'keep')
 & (Join-Path $projectRoot 'Tests/Fixtures/Update-App-0.2.12.ps1') -Stage $stage -Target $legacy -NoRestart
 Assert ((Get-FileHash (Join-Path $legacy 'IPA-Desktop.exe')).Hash -eq (Get-FileHash (Join-Path $stage 'IPA-Desktop.exe')).Hash) '0.2.12 helper did not install the new apphost'
 Assert ([IO.File]::ReadAllText((Join-Path $legacy 'keep.ipa')) -eq 'keep') 'User IPA was modified'
 [IO.File]::WriteAllText((Join-Path $data 'state.json'),'{"Current":"legacy","DeviceCurrent":"devices","AutoUpdate":false,"SoundsEnabled":false,"CheckGuiUpdates":false,"AutoUpdateDeviceTools":false}')
 [IO.File]::WriteAllText((Join-Path $data 'AppSets.json'),'[{"Name":"Legacy set","Apps":[{"ID":"123","Name":"Fixture"}]}]')
 # Launch the actual trimmed self-contained executable with no shared .NET runtime.
 $savedRoot=$env:DOTNET_ROOT;$savedPath=$env:PATH
 try {
  $env:DOTNET_ROOT=Join-Path $temp 'no-shared-runtime';$env:PATH="$env:SystemRoot\System32;$env:SystemRoot"
  $process=Start-Process -FilePath (Join-Path $legacy 'IPA-Desktop.exe') -ArgumentList @('--smoke-test',('"'+$data+'"')) -WorkingDirectory $legacy -PassThru
  if(!$process.WaitForExit(45000)){Stop-Process -Id $process.Id -Force;throw 'Published Avalonia startup timed out'}
  $process.Refresh()
  if($process.ExitCode -ne 0){
   $errorFile=Join-Path $data 'smoke-error.txt'
   if(Test-Path $errorFile){Get-Content $errorFile|Write-Host}
   throw "Published executable failed with exit code $($process.ExitCode)"
  }
 }finally{$env:DOTNET_ROOT=$savedRoot;$env:PATH=$savedPath}
 Assert (Test-Path (Join-Path $data 'smoke-ok.txt')) 'Published runtime did not finish its scenario checks'
 Get-Content (Join-Path $data 'smoke-ok.txt')|Write-Host
 if($env:IPA_TEST_ARTIFACTS){New-Item -ItemType Directory $env:IPA_TEST_ARTIFACTS -Force|Out-Null;Copy-Item (Join-Path $data 'published-tab-*.png') $env:IPA_TEST_ARTIFACTS}
 # Exercise the new updater after arriving through the unmodified 0.2.12 helper.
 $nextStage=Join-Path $temp 'next-update/stage'
 [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $Archive),$nextStage)
 & (Join-Path $projectRoot 'Scripts/Update-App.ps1') -Stage $nextStage -Target $legacy -NoRestart
 $manifest=@(Get-Content (Join-Path $stage 'package-files.json') -Raw|ConvertFrom-Json)
 foreach($file in $manifest){Assert ((Get-FileHash (Join-Path $stage $file)).Hash -eq (Get-FileHash (Join-Path $legacy $file)).Hash) "Modern update mismatch: $file"}
 Assert ([IO.File]::ReadAllText((Join-Path $legacy 'keep.ipa')) -eq 'keep') 'Modern updater modified user data'
 Write-Host 'PASS: 0.2.12 package limits, exact legacy helper, self-contained startup without DOTNET_ROOT, preserved settings/sets/IPA, modern update manifest'
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
