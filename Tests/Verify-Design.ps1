$projectRoot=Split-Path $PSScriptRoot -Parent
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$reader=[IO.File]::OpenRead((Join-Path $projectRoot 'MainWindow.xaml'))
try {$window=[Windows.Markup.XamlReader]::Load($reader)}finally{$reader.Dispose()}
try {
 $icon=New-Object Windows.Media.Imaging.BitmapImage
 $icon.BeginInit();$icon.UriSource=New-Object Uri((Join-Path $projectRoot 'Assets/App.ico'));$icon.CacheOption='OnLoad';$icon.EndInit()
 $window.Icon=$icon;$window.FindName('BrandIcon').Source=$icon
 $window.FindName('VersionList').Items.Add('Latest version')|Out-Null
 $window.FindName('VersionList').SelectedIndex=0
 $rows=@()
 foreach($name in @('MAX','Telegram Messenger','WhatsApp Messenger','YouTube')){$row=New-Object IpaDesktop.AppRow;$row.Name=$name;$row.ID=(6739530834L+$rows.Count).ToString();$rows+=$row}
 $window.FindName('Apps').ItemsSource=$rows
 $window.FindName('Apps').SelectedIndex=0
 $chosen=New-Object IpaDesktop.DownloadSelection
 foreach($row in $rows){$chosen.Add($row,[NullString]::Value)}
 $window.FindName('ChosenApps').ItemsSource=$chosen.Snapshot()
 $window.FindName('ChosenCount').Text='Selected: '+$chosen.Count
 $window.FindName('Download').Content='Download ('+$chosen.Count+')'
 $localRows=@()
 foreach($name in @('MAX.ipa','Telegram.ipa')) {
  $file=New-Object IpaDesktop.LocalRow;$file.Name=([IO.Path]::GetFileNameWithoutExtension($name)+'-6739530834-0123456789abcdef0123456789abcdef.ipa');$file.AppName=[IO.Path]::GetFileNameWithoutExtension($name);$file.Version='1.2.3';$file.Path='C:\Apps\'+$name;$file.AppleAccount='example@icloud.com';$file.Size='100 MB';$localRows+=$file
 }
 $window.FindName('Files').ItemsSource=$localRows
 $window.FindName('ChosenFiles').ItemsSource=$localRows
 $window.Show()
 [IpaDesktop.WindowTheme]::Apply($window)
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 # Check the actual text viewport, not only the outer search box height.
 $query=$window.FindName('Query')
 $query.Focus()|Out-Null
 $query.SelectedText='MAX Макс 6739530834'
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if($query.Text -ne 'MAX Макс 6739530834'){throw 'Search field did not retain entered text'}
 $hostView=$query.Template.FindName('PART_ContentHost',$query)
 if($hostView.ViewportHeight -lt $query.FontSize){throw 'Search text viewport is clipped by template padding'}
 $caret=$query.GetRectFromCharacterIndex(0)
 if($caret.IsEmpty -or $caret.Height -lt $query.FontSize -or $caret.Top -lt 0 -or $caret.Bottom -gt $query.ActualHeight){throw 'Search characters are outside the visible field'}
 if($query.Foreground.Color.ToString() -ne '#FFEDF1F8'){throw 'Search text lost its light foreground'}
 Write-Host ('PASS: entered Latin/Cyrillic search text is visible; viewport height '+$hostView.ViewportHeight)
 # Force each tab template to load and check minimum-window layout.
 $tabs=$window.FindName('Apps').Parent.Parent.Parent
 # Find the TabControl through the logical tree.
 function FindTabs($node) {
  if($node -is [Windows.Controls.TabControl]){return $node}
  foreach($child in [Windows.LogicalTreeHelper]::GetChildren($node)) {
   if($child -is [Windows.DependencyObject]){$result=FindTabs $child;if($result){return $result}}
  }
 }
 $tabs=FindTabs $window
 foreach($index in 0..4){$tabs.SelectedIndex=$index;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()}
 # Verify the same routed left-click binding used by both application grids.
 foreach($pair in @(@('Apps',0),@('Files',1))) {
  $tabs.SelectedIndex=$pair[1]
  $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
  $grid=$window.FindName($pair[0]);$row=$grid.ItemContainerGenerator.ContainerFromIndex(0)
  if(!$row){throw 'Missing visible data row'}
  $script:clickedItem=$null
  [IpaDesktop.RowClickSelection]::Attach($grid,[Action[object]]{param($item) $script:clickedItem=$item})
  foreach($eventId in @([Windows.UIElement]::PreviewMouseLeftButtonDownEvent,[Windows.UIElement]::PreviewMouseLeftButtonUpEvent)) {
   $mouse=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
   $mouse.RoutedEvent=$eventId;$mouse.Source=$row
   $grid.RaiseEvent($mouse)
  }
  if($script:clickedItem){throw 'Single click unexpectedly added an application'}
  $double=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Right)
  $double.RoutedEvent=[Windows.Controls.Control]::MouseDoubleClickEvent;$double.Source=$row;$grid.RaiseEvent($double)
  if($script:clickedItem){throw 'Right double click unexpectedly added an application'}
  $double=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
  $double.RoutedEvent=[Windows.Controls.Control]::MouseDoubleClickEvent;$double.Source=$row;$grid.RaiseEvent($double)
  if(![object]::ReferenceEquals($script:clickedItem,$grid.Items[0])){throw 'Double click did not add the correct data row'}
 }
 function FindButton($node) {
  if($node -is [Windows.Controls.Button] -and $node.Tag){return $node}
  for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++) {
   $result=FindButton ([Windows.Media.VisualTreeHelper]::GetChild($node,$i));if($result){return $result}
  }
 }
 foreach($pair in @(@('ChosenApps',0),@('ChosenFiles',1))) {
  $tabs.SelectedIndex=$pair[1]
  $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
  $list=$window.FindName($pair[0]);$button=FindButton $list
  if(!$button){throw 'No generated remove button'}
  $script:removedKey=$null
  [IpaDesktop.SelectionRemove]::Attach($list,[Action[string]]{param($key) $script:removedKey=$key})
  $button.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$button)))
  if($script:removedKey -ne [string]$button.Tag){throw 'Remove button did not deliver the correct ID/path'}
 }
 # Check the rendered file column and its tooltip, not only the model.
 $tabs.SelectedIndex=1
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $fileGrid=$window.FindName('Files');$fileCell=$fileGrid.Columns[0].GetCellContent($fileGrid.Items[0])
 if($fileCell.Text -ne 'MAX' -or !$fileCell.ToolTip.Contains($localRows[0].Name)){throw 'File grid did not render short name with full filename tooltip'}
 if($fileGrid.Columns[1].Header -ne 'Версия'){throw 'File grid version column is missing'}
 Write-Host 'PASS: rendered file grid short title, complete tooltip and version column'
 Write-Host 'PASS: remove button clicks through both actual WPF item templates'
 $journal=$window.FindName('Journal');$buffer=New-Object IpaDesktop.BufferedJournal
 [IpaDesktop.JournalView]::Attach($journal,$buffer)
 $tabs.SelectedIndex=0
 1..250|ForEach-Object{$journal.AppendText("old line $_"+[Environment]::NewLine)}
 $buffer.Add('latest hidden message')
 $tabs.SelectedIndex=3
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if(!$journal.Text.Contains('latest hidden message')){throw 'Journal tab did not flush pending messages'}
 $journal.ScrollToHome()
 $tabs.SelectedIndex=0;$tabs.SelectedIndex=3
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if($journal.VerticalOffset -lt ($journal.ExtentHeight-$journal.ViewportHeight-1)){throw 'Journal did not scroll to the latest line on entry'}
 $buffer.Add('latest live message');[IpaDesktop.JournalView]::Flush($journal,$buffer,$false)
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if(!$journal.Text.Contains('latest live message') -or $journal.VerticalOffset -lt ($journal.ExtentHeight-$journal.ViewportHeight-1)){throw 'Live journal did not follow new messages'}
 Write-Host 'PASS: journal latest entries on tab switch and live auto-scroll'
 $device=New-Object IpaDesktop.DeviceRow;$device.Name='Test iPhone';$device.ID='00008030-0000111ABC000DEF'
 $window.FindName('DeviceList').ItemsSource=[IpaDesktop.DeviceRow[]]@($device)
 $window.FindName('DeviceList').SelectedIndex=0
 if($window.FindName('DeviceList').SelectedItem.ID -ne $device.ID -or $window.FindName('DeviceList').DisplayMemberPath -ne 'DisplayLabel'){throw 'Named device selection lost its UDID'}
 Write-Host 'PASS: single-click exclusion and routed double-click selection on both grids'
 $tabs.SelectedIndex=0
 $window.Width=940;$window.Height=720;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 Write-Host ('Minimum layout: '+$window.FindName('Apps').ActualWidth+' x '+$window.FindName('Apps').ActualHeight)
 $minimumHeight=$window.FindName('Apps').ActualHeight
 $tabs.SelectedIndex=1;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if($window.FindName('Files').ActualHeight -lt 80){throw 'Files table collapsed at minimum size'}
 $picker=$window.FindName('FileAccount');$all=$window.FindName('SelectAllFiles')
 $point=$all.TranslatePoint((New-Object Windows.Point(0,0)),$window)
 if($picker.ActualWidth -lt 280 -or $picker.ActualHeight -lt 30 -or $all.ActualWidth -lt 100 -or $point.X+$all.ActualWidth -gt $window.ActualWidth){throw 'Account picker or select-all button clipped at minimum window size'}
 Write-Host 'PASS: account filter and bulk action remain visible at 940x720'

 $tabs.SelectedIndex=4;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if($window.FindName('StorageFiles').ActualHeight -lt 80){throw 'Storage table collapsed at minimum size'}
 $tabs.SelectedIndex=0;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $window.FindName('VersionList').IsDropDownOpen=$true;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $window.FindName('VersionList').IsDropDownOpen=$false
 $window.Width=1160;$window.Height=800;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $visual=$window.Content
 $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$visual.ActualWidth,[int]$visual.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($visual)
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=New-Object IO.MemoryStream
 $encoder.Save($stream)
 Write-Host ('IPA_PREVIEW:'+ [Convert]::ToBase64String($stream.ToArray()))
 $stream.Dispose()
 $tabs.SelectedIndex=4;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $window.FindName('StorageFiles').ItemsSource=$localRows
 $window.FindName('SetName').Text='Base set'
 $setA=New-Object IpaDesktop.AppSet;$setA.Name='Базовый набор'
 $setB=New-Object IpaDesktop.AppSet;$setB.Name='Общение'
 $presetApp=New-Object IpaDesktop.AppRow;$presetApp.Name='Telegram';$presetApp.ID='686449807'
 $setA.Apps=[IpaDesktop.AppRow[]]@($presetApp);$setB.Apps=[IpaDesktop.AppRow[]]@($presetApp)
 $window.FindName('AppSets').ItemsSource=[IpaDesktop.AppSet[]]@($setA,$setB)
 $window.FindName('AppSets').SelectedItems.Add($setA)|Out-Null;$window.FindName('AppSets').SelectedItems.Add($setB)|Out-Null
 $window.FindName('SetsCount').Text='Сохранено наборов: 2'
 $window.FindName('SetPreview').Text='Выбрано наборов: 2 · уникальных приложений: 1'

 $window.FindName('StorageSummary').Text='IPA: 2 | Storage: 200 MB | Older versions: 1'
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$visual.ActualWidth,[int]$visual.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($visual);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=New-Object IO.MemoryStream
 $encoder.Save($stream);Write-Host ('IPA_LIBRARY_PREVIEW:'+ [Convert]::ToBase64String($stream.ToArray()));$stream.Dispose()
 # Windows default disabled ListBox template can replace a transparent background with white.
 $setList=$window.FindName('AppSets')
 foreach($hasItems in @($false,$true)) {
  if($hasItems){$setList.ItemsSource=[IpaDesktop.AppSet[]]@($setA,$setB);$setList.SelectedItems.Add($setA)|Out-Null}
  else {$setList.ItemsSource=[IpaDesktop.AppSet[]]@()}
  foreach($enabled in @($true,$false,$true)) {
   $setList.IsEnabled=$enabled
   $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
   $surface=$setList.Template.FindName('SetListSurface',$setList)
   if(!$surface -or $surface.Background.Color.ToString() -ne '#FF101A29'){throw 'Set list lost dark surface when disabled or empty'}
   if($hasItems -and ($setList.SelectedItems.Count -ne 1 -or $setList.Items.Count -ne 2)){throw 'Disabling set list changed selection or items'}
  }
 }
 Write-Host 'PASS: dark set-list surface for empty/populated and enabled/disabled states; selection preserved'
 function EmitDialogPreview($dialog,$tag) {
  $dialog.UpdateLayout()
  $target=$dialog.Content
  $bmp=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$target.ActualWidth,[int]$target.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
  $bmp.Render($target);$png=New-Object Windows.Media.Imaging.PngBitmapEncoder
  $png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp));$bytes=New-Object IO.MemoryStream
  try{$png.Save($bytes);Write-Host ($tag+':'+[Convert]::ToBase64String($bytes.ToArray()))}finally{$bytes.Dispose()}
 }
 $ipa=[IpaDesktop.AppDialogs]::Ipa($window,$localRows[0]);$ipa.Show()
 try {EmitDialogPreview $ipa 'IPA_DETAILS_PREVIEW'}finally{$ipa.Close()}
 $editor=[IpaDesktop.AppSetEditor]::Create($window,'Базовый набор',[IpaDesktop.AppRow[]]@($presetApp),[Action[string,Collections.Generic.IEnumerable[IpaDesktop.AppRow]]]{param($name,$apps)})
 $editor.Show()
 try {EmitDialogPreview $editor 'IPA_EDITOR_PREVIEW'}finally{$editor.Close()}
 # Verify actual ScrollViewer scrollbars, page commands and drag routing in both orientations.
 function FindScrollbars($node){
  if($node -is [Windows.Controls.Primitives.ScrollBar]){Write-Output $node}
  for($n=0;$n -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$n++){FindScrollbars ([Windows.Media.VisualTreeHelper]::GetChild($node,$n))}
 }
 $scrollDialog=[IpaDesktop.AppDialogs]::Create($window,'Прокрутка',600,450)
 $viewer=New-Object Windows.Controls.ScrollViewer;$viewer.VerticalScrollBarVisibility='Auto';$viewer.HorizontalScrollBarVisibility='Auto'
 $surface=New-Object Windows.Controls.Border;$surface.Width=1500;$surface.Height=1500;$surface.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#151E2C')
 $label=New-Object Windows.Controls.TextBlock;$label.Text='Тёмная прокрутка · колесо, страницы и перетаскивание';$label.Margin='24';$surface.Child=$label;$viewer.Content=$surface;$scrollDialog.Content=$viewer
 $scrollDialog.Show()
 try{
  $scrollDialog.UpdateLayout()
  $bars=@(FindScrollbars $viewer|Where-Object IsVisible)
  if($bars.Count -ne 2){throw 'Both scrollbar orientations were not realized'}
  if($viewer.FindResource([Windows.SystemColors]::ControlBrushKey).Color.ToString() -ne '#FF101A29'){throw 'ScrollViewer corner lost dark background'}
  foreach($bar in $bars){
   $track=$bar.Template.FindName('PART_Track',$bar);$background=$bar.Template.FindName('ScrollSurface',$bar)
   if(!$track -or !$background -or $background.Background.Color.ToString() -ne '#FF101A29'){throw 'Native light scrollbar remained visible'}
   $bar.IsEnabled=$false;$scrollDialog.UpdateLayout()
   if($background.Opacity -ne 0.4){throw 'Disabled scrollbar state lost theme'}
   $bar.IsEnabled=$true
   if($bar.Orientation -eq 'Vertical'){
    if($bar.ActualWidth -ne 14 -or !$track.IsDirectionReversed){throw 'Vertical scrollbar geometry changed'}
    [Windows.Controls.Primitives.ScrollBar]::PageDownCommand.Execute($null,$bar)
   }else{
    if($bar.ActualHeight -ne 14 -or $track.IsDirectionReversed){throw 'Horizontal scrollbar geometry changed'}
    [Windows.Controls.Primitives.ScrollBar]::PageRightCommand.Execute($null,$bar)
   }
  }
  $scrollDialog.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$scrollDialog.UpdateLayout()
  if($viewer.VerticalOffset -le 0 -or $viewer.HorizontalOffset -le 0){throw 'Page commands do not scroll actual content'}
  $vertical=@($bars|Where-Object Orientation -eq 'Vertical')[0];$track=$vertical.Template.FindName('PART_Track',$vertical)
  $before=$viewer.VerticalOffset
  $track.Thumb.RaiseEvent((New-Object Windows.Controls.Primitives.DragStartedEventArgs(0,0)))
  $track.Thumb.RaiseEvent((New-Object Windows.Controls.Primitives.DragDeltaEventArgs(0,20)))
  $track.Thumb.RaiseEvent((New-Object Windows.Controls.Primitives.DragCompletedEventArgs(0,20,$false)))
  $scrollDialog.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$scrollDialog.UpdateLayout()
  if($viewer.VerticalOffset -le $before){throw 'Scrollbar thumb drag did not scroll content'}
  EmitDialogPreview $scrollDialog 'IPA_SCROLLBAR_PREVIEW'
 }finally{$scrollDialog.Close()}
 Write-Host 'PASS: dark vertical/horizontal scrollbars, disabled state, actual page commands and thumb drag'
 # Settings stay visible in their tab without opening an owned window.
 $window.FindName('SettingsTab').IsSelected=$true;$window.UpdateLayout()
 if(!$window.FindName('SettingsContent').IsVisible -or $window.FindName('SettingsContent').ActualHeight -lt 100){throw 'Inline settings are not visible'}
 EmitDialogPreview $window 'IPA_SETTINGS_PREVIEW'
 $tabs.SelectedIndex=0;$window.Width=940;$window.Height=720
 $window.FindName('OperationNotice').Visibility='Visible'
 $window.FindName('NoticeText').Text='Скачивание: есть ошибки · готово 2/3 · MAX: не удалось открыть IPA'
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if($window.FindName('Apps').ActualHeight -lt 80){throw 'Visible error banner collapsed search table at minimum size'}
 EmitDialogPreview $window 'IPA_NOTICE_PREVIEW'
 Write-Host 'PASS: IPA, set editor and inline settings layouts; persistent banner at minimum window size'
 if($minimumHeight -lt 80){throw 'Application table collapsed at minimum size'}
 Write-Host 'PASS: all tab templates, dark combo popup, minimum window layout, icon and DWM initialization'
}finally{$window.Close()}
