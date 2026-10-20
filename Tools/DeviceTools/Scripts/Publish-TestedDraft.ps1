# Recover only a draft produced by an already tested build of identical native inputs.
$ErrorActionPreference='Stop'
$version=(Get-Content Tools/DeviceTools/VERSION -Raw).Trim()
$tag="device-tools-v$version"
$repo=$env:GH_REPO
$releases=gh api "repos/$repo/releases?per_page=100" | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Could not read releases'}
$matches=@($releases|Where-Object tag_name -eq $tag)
if($matches.Count -eq 0){Write-Host 'No draft to recover';return}
if($matches.Count -ne 1){throw 'Ambiguous native release'}
$release=$matches[0]
if(!$release.draft){Write-Host 'Native release is already public';return}
$target=$release.target_commitish
if($target -notmatch '^[0-9a-f]{40}$'){throw 'Draft target is not an exact commit'}
$names=@("IPA-DeviceTools-$version-Windows-x64.zip","IPA-DeviceTools-$version-Sources.zip")
foreach($name in $names){
 $asset=@($release.assets|Where-Object name -eq $name)
 if($asset.Count -ne 1 -or $asset[0].size -le 0 -or $asset[0].digest -notmatch '^sha256:[0-9a-f]{64}$'){throw 'Draft is incomplete; refuse recovery'}
}
$runs=gh api "repos/$repo/actions/workflows/device-tools.yml/runs?head_sha=$target&per_page=30" | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Could not verify tested build'}
$tested=$false
foreach($run in @($runs.workflow_runs|Where-Object { $_.head_sha -eq $target -and $_.head_branch -eq 'main' })){
 $jobs=gh api "repos/$repo/actions/runs/$($run.id)/jobs?filter=latest&per_page=100" | ConvertFrom-Json
 if($LASTEXITCODE -ne 0){throw 'Could not verify native build steps'}
 foreach($job in @($jobs.jobs|Where-Object name -eq 'build')){
  $compiled=@($job.steps|Where-Object { $_.name -eq 'Build pinned sources and package Windows binaries' -and $_.conclusion -eq 'success' })
  $checked=@($job.steps|Where-Object { $_.name -eq 'Verify standalone Windows command line tools' -and $_.conclusion -eq 'success' })
  if($compiled.Count -eq 1 -and $checked.Count -eq 1){$tested=$true}
 }
}
if(!$tested){throw 'No successful compile and native validation for draft target'}
git fetch --no-tags --depth=1 origin $target
if($LASTEXITCODE -ne 0){throw 'Could not read tested native source commit'}
# Compare the complete native tool tree; new modules cannot escape draft validation.
$built=git rev-parse "${target}:Tools/DeviceTools"
if($LASTEXITCODE -ne 0){throw 'Missing tested native inputs'}
$current=git rev-parse 'HEAD:Tools/DeviceTools'
if($LASTEXITCODE -ne 0 -or $built -ne $current){throw 'Native inputs changed since tested build; rebuild instead of recovering draft'}
$cache=Join-Path $PWD 'native-draft-recovery'
New-Item -ItemType Directory -Path $cache|Out-Null
foreach($name in $names){
 $asset=@($release.assets|Where-Object name -eq $name)[0]
 $path=Join-Path $cache $name
 Invoke-WebRequest -Uri $asset.url -Headers @{Authorization="Bearer $env:GH_TOKEN";Accept='application/octet-stream'} -OutFile $path
 if((Get-Item $path).Length -ne $asset.size -or ('sha256:'+(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()) -ne $asset.digest){throw 'Draft asset size or SHA-256 mismatch'}
}
$package=Join-Path $cache 'package'
Expand-Archive (Join-Path $cache "IPA-DeviceTools-$version-Windows-x64.zip") $package
$manifest=Get-Content (Join-Path $package 'sha256.json') -Raw | ConvertFrom-Json
$prefix=[IO.Path]::GetFullPath($package)+[IO.Path]::DirectorySeparatorChar
foreach($entry in $manifest.PSObject.Properties){
 $file=[IO.Path]::GetFullPath((Join-Path $package $entry.Name))
 if(!$file.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $entry.Value){throw 'Recovered package manifest mismatch'}
}
$pins=Get-Content Tools/DeviceTools/pins.json -Raw | ConvertFrom-Json
$builtPins=Get-Content (Join-Path $package 'sources.json') -Raw | ConvertFrom-Json
foreach($entry in $pins.PSObject.Properties){if($builtPins.PSObject.Properties[$entry.Name].Value -ne $entry.Value){throw 'Recovered package source revision mismatch'}}
$savedPath=$env:PATH
try{
 $env:PATH="$env:SystemRoot\System32;$env:SystemRoot"
 foreach($name in @('idevice_id','ideviceinfo','ideviceinstaller')){
  $exe=Join-Path $package "windows_amd64/$name.exe"
  $reported=& $exe --version 2>&1
  if($LASTEXITCODE -ne 0 -or !$reported){throw 'Recovered native tool does not run independently'}
  $help=& $exe --help 2>&1
  if($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch '--network'){throw 'Recovered native network support missing'}
  Write-Host "$name : $reported"
 }
}finally{$env:PATH=$savedPath}
# Drafts without a published tag cannot reliably be retrieved by /releases/tags/TAG.
gh api --method PATCH "repos/$repo/releases/$($release.id)" -F draft=false -f make_latest=false | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not publish validated draft by ID'}
"published=true" >> $env:GITHUB_OUTPUT
Write-Host "PASS: recovered tested native draft by release ID; verified both asset hashes, native inputs, manifest and standalone network CLI. Published $tag."
