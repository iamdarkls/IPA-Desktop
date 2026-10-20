param([switch]$Check)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$source=Join-Path $projectRoot 'Source'
if(!(Test-Path -LiteralPath $source)){throw 'Source directory is required. Use a repository checkout for development.'}
$groups=[ordered]@{
 'Core.cs'=@('Domain','Infrastructure','Services')
 'App.cs'=@('Presentation')
 'IpaMetadata.cs'=@('Infrastructure/Storage/IpaMetadata.cs')
 'WindowTheme.cs'=@('Presentation/Shared/WindowTheme.cs')
 'GuiUpdater.cs'=@('Services/Updates/GuiUpdater.cs')
}
$special=@('IpaMetadata.cs','WindowTheme.cs','GuiUpdater.cs')
foreach($target in $groups.Keys) {
 $files=@()
 foreach($path in $groups[$target]) {
  $entry=Join-Path $source $path
  if(Test-Path -LiteralPath $entry -PathType Container){$files+=Get-ChildItem -LiteralPath $entry -Filter '*.cs' -File -Recurse}
  else{$files+=Get-Item -LiteralPath $entry}
 }
 if($special -notcontains $target){$files=@($files|Where-Object{$special -notcontains $_.Name})}
 $files=@($files|Sort-Object FullName -Unique)
 $imports=New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
 $bodies=New-Object 'System.Collections.Generic.List[string]'
 foreach($file in $files) {
  $text=[IO.File]::ReadAllText($file.FullName).Replace("`r`n","`n")
  foreach($match in [regex]::Matches($text,'(?m)^using [^;]+;')){[void]$imports.Add($match.Value)}
  $body=[regex]::Replace($text,'(?m)^using [^;]+;\n?','').Trim()
  $bodies.Add('// '+$file.FullName.Substring($source.Length+1).Replace('\','/')+"`n"+$body)
 }
 $generated="// Generated from Source/. Edit the source classes, then run Sync-Sources.ps1.`n"+[string]::Join("`n",$imports)+"`n`n"+[string]::Join("`n`n",$bodies)+"`n"
 $destination=Join-Path (Join-Path $projectRoot 'Generated') $target
 if($Check) {
  if([IO.File]::ReadAllText($destination).Replace("`r`n","`n") -cne $generated){throw "Outdated generated file: $target. Run Sync-Sources.ps1."}
 }else{[IO.File]::WriteAllText($destination,$generated,(New-Object Text.UTF8Encoding($false)))}
}
Write-Host 'PASS: canonical sources match portable compilation snapshots'
