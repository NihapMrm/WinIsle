@echo off
rem Optional first argument: where to write the exe (for example a temp copy to run --check while the app runs).
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set MD=%WINDIR%\System32\WinMetadata
set OUT=%~1
if "%OUT%"=="" set OUT=%~dp0WinIsle.exe
"%FW%\csc.exe" /nologo /target:winexe /optimize /out:"%OUT%" ^
  /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Drawing.dll" /r:"%FW%\System.Management.dll" ^
  /r:"%FW%\System.Runtime.dll" /r:"%MD%\Windows.UI.winmd" /r:"%MD%\Windows.Foundation.winmd" /r:"%MD%\Windows.ApplicationModel.winmd" /r:"%MD%\Windows.Media.winmd" /r:"%MD%\Windows.Storage.winmd" /r:"%MD%\Windows.Graphics.winmd" /r:"%MD%\Windows.Devices.winmd" ^
  /recurse:"%~dp0src\*.cs"
