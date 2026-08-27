@echo off
rem Build StockMonitor.exe with the Windows built-in .NET Framework compiler.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:StockMonitor.exe ^
  /resource:"捐赠.png",StockMonitor.Donate.png ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll ^
  StockMonitor.Core.cs StockMonitor.UI.cs StockMonitor.Chart.cs
if %errorlevel% equ 0 (
  echo BUILD OK: StockMonitor.exe
) else (
  echo BUILD FAILED
)
pause
