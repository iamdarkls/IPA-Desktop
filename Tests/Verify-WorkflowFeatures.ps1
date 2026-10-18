$projectRoot=Split-Path $PSScriptRoot -Parent
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-features-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
function Assert($condition,$message){if(!$condition){throw $message}}
function NewIpa($folder,$name,$bundle,$version,$account,$binaryInfo=$null){
 $path=Join-Path $folder $name
 $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
 try{
  $metadata='<?xml version="1.0"?><plist version="1.0"><dict><key>appleId</key><string>'+ $account +'</string><key>itemId</key><integer>1234567890</integer><key>itemName</key><string>Example</string><key>softwareVersionBundleId</key><string>'+ $bundle +'</string><key>bundleVersion</key><string>0.1</string></dict></plist>'
  $info='<?xml version="1.0"?><plist version="1.0"><dict><key>CFBundleIdentifier</key><string>'+ $bundle +'</string><key>CFBundleShortVersionString</key><string>'+ $version +'</string><key>CFBundleVersion</key><string>10</string></dict></plist>'
  foreach($pair in @(@('iTunesMetadata.plist',[Text.Encoding]::UTF8.GetBytes($metadata)),@('Payload/Example.app/Info.plist',$(if($binaryInfo){[Convert]::FromBase64String($binaryInfo)}else{[Text.Encoding]::UTF8.GetBytes($info)})))){
   $entry=$zip.CreateEntry($pair[0]);$stream=$entry.Open()
   try{$bytes=[byte[]]$pair[1];$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
  }
 }finally{$zip.Dispose()}
 return $path
}
try{
 $engine=New-Object IpaDesktop.Engine($temp)
 # Offline catalog fixture: no network or Apple account needed.
 $catalogRoot=Join-Path $temp 'catalog-cache'
 $catalogStore=New-Object IpaDesktop.SettingsStore($catalogRoot)
 $catalogLocator=New-Object IpaDesktop.ToolLocator($catalogStore)
 $catalog=New-Object IpaDesktop.AppCatalog($catalogStore,$catalogLocator)
 $catalog.GetType().GetField('catalogChecked',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($catalog,$true)
 $catalogDir=Join-Path $catalogRoot 'Files';[IO.Directory]::CreateDirectory($catalogDir)|Out-Null
 $catalogPath=Join-Path $catalogDir 'AppsList.txt';$customPath=Join-Path $catalogDir 'AppsListCustom.txt'
 $catalogText=(1..2000|ForEach-Object{'Application '+$_+': '+(100000+$_)}) -join [Environment]::NewLine
 [IO.File]::WriteAllText($catalogPath,$catalogText)
 [IO.File]::WriteAllText($customPath,'Custom app: 100001')
 $timer=[Diagnostics.Stopwatch]::StartNew()
 $matches=$catalog.SearchAsync('app',[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 $cold=$timer.Elapsed.TotalMilliseconds
 Assert ($matches.Count -eq 2000 -and $matches[0].Name -eq 'Custom app') 'Cached catalog lost custom priority or ID deduplication'
 $matches[0].Name='mutated UI result'
 # Exclusive handles make any repeated content read fail; metadata checks remain possible.
 $lockedCatalog=[IO.File]::Open($catalogPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
 $lockedCustom=[IO.File]::Open($customPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
 try {
  $timer.Restart()
  $matches=$catalog.SearchAsync('app',[Threading.CancellationToken]::None).GetAwaiter().GetResult()
  $warm=$timer.Elapsed.TotalMilliseconds
  Assert ($matches.Count -eq 2000 -and $matches[0].Name -eq 'Custom app') 'Repeated search reread locked catalog or cache was mutated by UI'
 }finally{$lockedCatalog.Dispose();$lockedCustom.Dispose()}
 [IO.File]::WriteAllText($customPath,'Renamed custom fixture: 100001')
 $matches=$catalog.SearchAsync('Renamed',[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 Assert ($matches.Count -eq 1 -and $matches[0].ID -eq '100001') 'Edited custom catalog did not invalidate cache'
 [IO.File]::Delete($customPath)
 $matches=$catalog.SearchAsync('Application 1',[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 Assert (@($matches|Where-Object ID -eq '100001').Count -eq 1) 'Deleting custom catalog did not restore base catalog entry'
 $cancelCatalog=New-Object Threading.CancellationTokenSource;$cancelCatalog.Cancel();$rejected=$false
 try {try{$catalog.SearchAsync('app',$cancelCatalog.Token).GetAwaiter().GetResult()|Out-Null}catch{$rejected=$true}}
 finally {$cancelCatalog.Dispose()}
 Assert $rejected 'Catalog ignored cancellation'
 Write-Host ('PASS: catalog cache with locked source files, custom edits/deletion, result isolation and cancellation; 2000 rows cold={0:N2}ms warm={1:N2}ms' -f $cold,$warm)

 $old=NewIpa $engine.Downloads 'old.ipa' 'com.example.app' '1.9' 'first@example.com'
 $latest=NewIpa $engine.Downloads 'latest.ipa' 'com.example.app' '1.10' 'first@example.com'
 $other=NewIpa $engine.Downloads 'other-account.ipa' 'com.example.app' '1.0' 'second@example.com'
 $unknown=NewIpa $engine.Downloads 'unknown.ipa' 'com.example.app' '0.5' ''
 $binary=NewIpa $engine.Downloads 'binary.ipa' 'com.example.binary' '0.1' 'first@example.com' 'YnBsaXN0MDDTAQIDBAUGXxASQ0ZCdW5kbGVJZGVudGlmaWVyXxAaQ0ZCdW5kbGVTaG9ydFZlcnNpb25TdHJpbmdfEA9DRkJ1bmRsZVZlcnNpb25fEBJjb20uZXhhbXBsZS5iaW5hcnlTMi4wUjEwCA8kQVNobAAAAAAAAAEBAAAAAAAAAAcAAAAAAAAAAAAAAAAAAABv'
 $rows=$engine.LocalFiles.ListAsync().GetAwaiter().GetResult()
 $choices=[IpaDesktop.LocalFileAccounts]::Choices($rows)
 Assert ($choices.Count -eq 4 -and $null -eq $choices[0].Key) 'Account filter did not expose all/known/unknown options'
 Assert (@($rows|Where-Object {[IpaDesktop.LocalFileAccounts]::Matches($_,'FIRST@example.com')}).Count -eq 3) 'Account filter case handling failed'
 Assert (@($rows|Where-Object {[IpaDesktop.LocalFileAccounts]::Matches($_,'')}).Count -eq 1) 'Unknown account was assigned to a known account'
 foreach($status in @($null,'','Не указан','Нет доступа','Не удалось прочитать','Метаданные слишком велики')) {
  $missing=New-Object IpaDesktop.LocalRow;$missing.AppleAccount=$status
  Assert ([IpaDesktop.LocalFileAccounts]::Matches($missing,'')) 'Missing/unreadable metadata was treated as a real account'
 }
 Write-Host 'PASS: metadata-based account choices with unknown/error statuses and case-insensitive matching'

 Assert ($rows.Count -eq 5) 'Storage lost IPA files'
 Assert (($rows|Where-Object Path -eq $old).IsOld) 'Numeric old-version detection failed'
 Assert (!($rows|Where-Object Path -eq $latest).IsOld) 'Newest version was selected for deletion'
 Assert (!($rows|Where-Object Path -eq $other).IsOld) 'Another Apple account was grouped with old versions'
 Assert (!($rows|Where-Object Path -eq $unknown).IsOld) 'Unknown account was selected for cleanup'
 Assert (($rows|Where-Object Path -eq $binary).Version -eq '2.0') 'Binary Info.plist version was not read'
 Assert (($rows|Where-Object Path -eq $latest).Version -eq '1.10') 'Info.plist did not override metadata fallback'
 Assert (($rows|Measure-Object Bytes -Sum).Sum -gt 0) 'Storage size is missing'
 $engine.AppSets.Save('Base',[IpaDesktop.LocalRow[]]@($rows|Where-Object { $_.Path -eq $old -or $_.Path -eq $latest }))
 $sets=$engine.AppSets.Load();Assert ($sets.Count -eq 1 -and $sets[0].Apps.Count -eq 1 -and $sets[0].Apps[0].ID -eq '1234567890') 'Set did not deduplicate App Store IDs'
 $saved=[IO.File]::ReadAllText((Join-Path $temp 'AppSets.json'))
 Assert (!$saved.Contains('old.ipa') -and !$saved.Contains('first@example.com') -and !$saved.Contains('1.10')) 'Set retained IPA paths, accounts or versions'
 [IO.File]::Delete($old);[IO.File]::Delete($latest)
 $resolved=$engine.AppSets.ResolveApps($sets[0]);Assert ($resolved.Count -eq 1 -and $resolved[0].ID -eq '1234567890') 'ID set depends on locally downloaded IPA'
 # Legacy sets migrate by metadata, never by guessing a filename.
 $legacy=New-Object IpaDesktop.AppSet;$legacy.Name='Legacy';$legacy.Files=@('other-account.ipa')
 $resolved=$engine.AppSets.ResolveApps($legacy);Assert ($resolved[0].ID -eq '1234567890') 'Legacy metadata ID was not read'
 $engine.AppSets.SaveApps($legacy.Name,[IpaDesktop.AppRow[]]$resolved)
 $legacy.Files=@('absent.ipa');$rejected=$false
 try{$engine.AppSets.ResolveApps($legacy)|Out-Null}catch{$rejected=$true}
 Assert $rejected 'Missing legacy IPA was silently substituted'
 $bad=New-Object IpaDesktop.AppSet;$bad.Name='Bad';$bad.Files=@('..\outside.ipa');$rejected=$false
 try{$engine.AppSets.ResolveApps($bad)|Out-Null}catch{$rejected=$true}
 Assert $rejected 'Set path traversal was accepted'
 $badApp=New-Object IpaDesktop.AppRow;$badApp.ID='../123';$badApp.Name='Invalid';$rejected=$false
 try{$engine.AppSets.SaveApps('Bad',[IpaDesktop.AppRow[]]@($badApp))}catch{$rejected=$true}
 Assert $rejected 'Invalid App Store ID accepted'
 $outside=New-Object IpaDesktop.LocalRow;$outside.Path=Join-Path $temp 'outside.ipa';[IO.File]::WriteAllText($outside.Path,'fixture')
 $rejected=$false;try{$engine.AppSets.Save('Outside',[IpaDesktop.LocalRow[]]@($outside))}catch{$rejected=$true}
 Assert $rejected 'Set accepted an IPA outside the downloads folder'
 $noID=Join-Path $engine.Downloads 'no-id.ipa';[IO.File]::WriteAllText($noID,'fixture')
 $legacy.Files=@('no-id.ipa');$rejected=$false
 try{$engine.AppSets.ResolveApps($legacy)|Out-Null}catch{$rejected=$true}
 Assert $rejected 'Legacy filename was treated as an ID without metadata'
 $integer=Join-Path $engine.Downloads 'integer.ipa';$zip=[IO.Compression.ZipFile]::Open($integer,[IO.Compression.ZipArchiveMode]::Create)
 try{$stream=$zip.CreateEntry('iTunesMetadata.plist').Open();try{$bytes=[Convert]::FromBase64String('YnBsaXN0MDDSAQIDBFdhcHBsZUlkVml0ZW1JZF8QEWZpcnN0QGV4YW1wbGUuY29tEwAAAAGRtRRSCA0VHDAAAAAAAAABAQAAAAAAAAAFAAAAAAAAAAAAAAAAAAAAOQ==');$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}}finally{$zip.Dispose()}
 Assert ([IpaDesktop.IpaMetadata]::ReadDetails($integer).AppStoreID -eq '6739530834') 'Binary integer itemId was lost or truncated to 32 bits'
 $engine.AppSets.Delete('Base');Assert ($engine.AppSets.Load().Count -eq 1 -and [IO.File]::Exists($other)) 'Deleting a set removed IPA files'
 # Short labels must not alter stored file names, paths or account/version metadata.
 $label=New-Object IpaDesktop.LocalRow
 $label.Name='Макс (Max)-6739530834-0123456789abcdef0123456789abcdef.ipa'
 $label.Path=Join-Path $engine.Downloads $label.Name
 Assert ($label.DisplayName -eq 'Макс (Max)') 'Generated filename suffix was shown as application title'
 $label.AppName='MAX';$label.Version='1.2.3';$label.AppleAccount='first@example.com'
 Assert ($label.DisplayName -eq 'MAX' -and $label.FileDetails.Contains($label.Name) -and $label.FileDetails.Contains($label.Path) -and $label.FileDetails.Contains('1.2.3') -and $label.FileDetails.Contains('first@example.com')) 'Short label or full IPA tooltip lost metadata'
 $selection=New-Object IpaDesktop.InstallationSelection;$selection.Add($label)
 Assert ($selection.Snapshot()[0].DisplayName -eq 'MAX' -and $selection.Snapshot()[0].Path -eq $label.Path) 'Install queue lost short label or real path'
 $label.AppName=$null;$label.Name='My-App-123.ipa'
 Assert ($label.DisplayName -eq 'My-App-123') 'Ordinary imported filename was shortened incorrectly'
 Assert (($rows|Where-Object Path -eq $other).DisplayName -eq 'Example') 'IPA metadata title was not used'
 Write-Host 'PASS: short metadata titles, exact filename fallback, full tooltips and unchanged installation paths'
 Write-Host 'PASS: account-independent ID sets, metadata IDs, legacy conversion, missing files, path bounds, XML/binary integers and account-isolated storage'
 # Batch legacy conversion is all-or-nothing, even when the second set is invalid.
 $legacy.Files=@('other-account.ipa')
 $invalid=New-Object IpaDesktop.AppSet;$invalid.Name='Missing legacy';$invalid.Files=@('missing.ipa')
 $before=[IO.File]::ReadAllText((Join-Path $temp 'AppSets.json'));$rejected=$false
 try{$engine.AppSets.ResolveMany([IpaDesktop.AppSet[]]@($legacy,$invalid))|Out-Null}catch{$rejected=$true}
 Assert ($rejected -and [IO.File]::ReadAllText((Join-Path $temp 'AppSets.json')) -eq $before) 'Failed batch migration changed saved sets'
 $locked=Join-Path $temp 'atomic.json';[IO.File]::WriteAllText($locked,'original')
 $handle=[IO.File]::Open($locked,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$rejected=$false
 try{try{[IpaDesktop.AtomicFile]::WriteUtf8($locked,'replacement')}catch{$rejected=$true}}finally{$handle.Dispose()}
 Assert ($rejected -and [IO.File]::ReadAllText($locked) -eq 'original' -and @(Get-ChildItem $temp -Filter 'atomic.json.*.tmp').Count -eq 0) 'Atomic save failure damaged original or retained temp file'
 # Windows PowerShell compiles test helpers against a DLL copy of the application.
 $testAssembly=Join-Path $temp 'IPA-Desktop-test.dll'
 Copy-Item -LiteralPath (Join-Path $projectRoot 'IPA-Desktop.exe') -Destination $testAssembly -Force
 Add-Type -ReferencedAssemblies @($testAssembly,'System.dll','System.Core.dll') -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IpaDesktop;
public static class BatchContractCheck {
 public static void Verify() {
  int signals=0;
  var sound=new CompletionSound(delegate { signals++; });
  sound.Notify(false,1,0);sound.Notify(true,0,0);sound.Notify(true,1,1);
  if(signals!=0)throw new Exception("Disabled, empty or failed queue played success sound");
  sound.Notify(true,3,0);
  if(signals!=1)throw new Exception("Successful queue did not play exactly one sound");
  new CompletionSound(delegate { throw new InvalidOperationException("no sound device"); }).Notify(true,1,0);
  int started=0, completed=0, failed=0;
  var result=BatchRunner.Run(new List<int>{1,2,3}, (item,ct)=>{if(item==2)throw new InvalidOperationException("fixture");return Task.FromResult(0);},
   (item,index)=>{started++;}, item=>{completed++;}, (item,error)=>{failed++;}, null, CancellationToken.None).GetAwaiter().GetResult();
  if(started!=3 || completed!=2 || failed!=1 || result.Succeeded!=2 || result.Failed.Count!=1 || result.Failed[0]!=2)throw new Exception("Batch did not continue after item failure");
  using(var cancel=new CancellationTokenSource()){
   started=0;failed=0;bool canceled=false;
   try{BatchRunner.Run(new List<int>{1,2,3}, (item,ct)=>{cancel.Cancel();return Task.FromResult(0);},
    (item,index)=>{started++;}, item=>{}, (item,error)=>{failed++;}, null, cancel.Token).GetAwaiter().GetResult();}
   catch(OperationCanceledException){canceled=true;}
   if(!canceled || started!=1 || failed!=0)throw new Exception("Cancellation started pending items or counted failures");
  }
 }
}
'@
 [BatchContractCheck]::Verify()
 Write-Host 'PASS: completion sound policy and unavailable audio device'
 Write-Host 'PASS: atomic write failure, all-or-nothing set migration, batch error continuation and cancellation'
 # Reports preserve full failures and distinguish success, failure and cancellation.
 $operation=New-Object IpaDesktop.OperationReport;$operation.Kind='Установка';$operation.Total=3
 $operation.Add('MAX',$null)
 $operation.Add('Telegram',(New-Object Exception('ERROR: ApplicationVerificationFailed')))
 Assert ($operation.Succeeded -eq 1 -and $operation.Failed -eq 1 -and $operation.Details.Contains('ApplicationVerificationFailed') -and $operation.Items[1].ShortError.Contains('проверку')) 'Operation report lost full or short failure'
 $operation.Canceled=$true
 Assert ($operation.Summary.Contains('отменено') -and $operation.Details.Contains('1')) 'Canceled operation report lost remaining items'
 $setOne=New-Object IpaDesktop.AppRow;$setOne.ID='900';$setOne.Name='Update fixture'
 $engine.AppSets.SaveApps('Rename source',[IpaDesktop.AppRow[]]@($setOne))
 $engine.AppSets.SaveApps('Existing target',[IpaDesktop.AppRow[]]@($setOne))
 $before=[IO.File]::ReadAllText((Join-Path $temp 'AppSets.json'));$rejected=$false
 try{$engine.AppSets.Update('Rename source','Existing target',[IpaDesktop.AppRow[]]@($setOne))}catch{$rejected=$true}
 Assert ($rejected -and [IO.File]::ReadAllText((Join-Path $temp 'AppSets.json')) -eq $before) 'Set rename collision damaged config'
 $engine.AppSets.Update('Rename source','New name',[IpaDesktop.AppRow[]]@($setOne))
 $sets=$engine.AppSets.Load()
 Assert (@($sets|Where-Object Name -eq 'New name').Count -eq 1 -and @($sets|Where-Object Name -eq 'Rename source').Count -eq 0) 'Atomic set rename did not replace original'
 Write-Host 'PASS: report success/failure/cancel and atomic set rename collision handling'
 $progress=New-Object IpaDesktop.QueueProgress
 $progress.Begin('Скачивание',2,3,'Example')
 Assert ($progress.Feed('PROGRESS:50')) 'Machine-readable progress was ignored'
 Assert ([Math]::Abs($progress.Value-50) -lt 0.01) 'Queue percentage is incorrect'
 Assert ($progress.Text.Contains('2/3') -and $progress.Text.Contains('Example')) 'Progress lost queue/app identity'
 Assert (!$progress.Feed('PROGRESS:101')) 'Invalid percentage accepted'
 Assert ($progress.Text.Contains('недоступны')) 'Unreported speed/bytes were invented'
 Assert ($progress.Feed('Downloading: 50% |bar| (10 MB/20 MB, 2.0 MB/s)')) 'Reported transfer metrics were ignored'
 Assert ($progress.Text.Contains('2.0 MB/s')) 'Reported transfer speed was lost'
 $progress.Begin('Установка',1,2,'Example')
 Assert ($progress.Feed('Install: InstallingApplication (30%)')) 'Installer percentage was ignored'
 $progress.Complete(1,1);Assert ($progress.Value -eq 100 -and $progress.Text.Contains('ошибок: 1')) 'Queue result summary is incorrect'
 $progress.Begin('Скачивание',1,2,'Canceled');$progress.Feed('PROGRESS:50')|Out-Null;$progress.Stop('Отменено')
 Assert ($progress.Text.Contains('Отменено') -and !$progress.Feed('PROGRESS:80')) 'Canceled progress continued updating'
 $progress.Reset();Assert (!$progress.Active) 'New operation retained old progress'
 Write-Host 'PASS: actual progress formats, honest missing metrics, global queue progress and result summary'
 $raw=@'
App 6739530834 version 1.10
first@example.com
C:\Users\PrivateUser\Apps\MAX.ipa
Authorization: Bearer header-secret
Cookie: cookie-secret
{"password":"json-pass","passwordToken":"json-token","authCode":"123456","dsid":9876543210}
--password "argument pass" --auth-code 654321 --keychain-passphrase 'phrase-secret'
refresh_token=refresh-secret
<key>passwordToken</key><string>plist-secret</string>
00008030-0000111ABC000DEF
Private iPhone
'@
 $report=[IpaDesktop.DiagnosticReport]::Create($raw,'1.1.8','device-tools-v1.0.0',[string[]]@('Private iPhone'))
 foreach($secret in @('first@example.com','PrivateUser','header-secret','cookie-secret','json-pass','json-token','123456','9876543210','argument pass','654321','phrase-secret','refresh-secret','plist-secret','00008030-0000111ABC000DEF','Private iPhone')){Assert (!$report.Contains($secret)) ('Diagnostic report leaked '+$secret)}
 Assert ($report.Contains('6739530834') -and $report.Contains('version 1.10') -and $report.Contains('1.1.8')) 'Diagnostic redaction discarded useful application/version data'
 Write-Host 'PASS: diagnostic emails, profiles, headers, JSON secrets, CLI credentials, plist tokens and device identifiers are scrubbed'
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
