@echo off
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set MD=%WINDIR%\System32\WinMetadata
"%FW%\csc.exe" /nologo /target:winexe /optimize /out:"%~dp0TodoPanel.exe" ^
  /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Drawing.dll" ^
  /r:"%FW%\System.Runtime.dll" /r:"%MD%\Windows.UI.winmd" /r:"%MD%\Windows.Foundation.winmd" /r:"%MD%\Windows.ApplicationModel.winmd" ^
  /recurse:"%~dp0src\*.cs"
