@echo off
setlocal
cd /d "%~dp0"
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
"%FX%\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /win32manifest:app.manifest /out:PC-Care-Studio.exe /resource:Theme.xaml,Theme.xaml /reference:"%FX%\WPF\PresentationFramework.dll" /reference:"%FX%\WPF\PresentationCore.dll" /reference:"%FX%\WPF\WindowsBase.dll" /reference:"%FX%\System.Xaml.dll" /reference:"%FX%\System.Net.Http.dll" /reference:"%FX%\System.Web.Extensions.dll" /reference:"%FX%\System.Web.dll" /reference:"%FX%\System.Management.dll" /reference:"%FX%\System.Security.dll" /reference:"%FX%\System.Windows.Forms.dll" /reference:"%FX%\System.Drawing.dll" Program.cs Core.cs Services.cs OpenAIClient.cs MainWindow.cs
exit /b %errorlevel%
