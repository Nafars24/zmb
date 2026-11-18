@echo off
setlocal
cd /d "%~dp0"
title Build ZMB
echo ==================================================
echo   ZMB - membuat ZMB.exe
echo ==================================================
echo.

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
    echo [ERROR] Compiler .NET Framework 4 tidak ditemukan.
    echo Buka "Turn Windows features on or off" lalu centang ".NET Framework 4.8 Advanced Services".
    echo.
    pause
    exit /b 1
)

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /out:ZMB.exe ^
  /win32icon:assets\zmb.ico ^
  /win32manifest:assets\app.manifest ^
  /nowarn:168,219,414,649,618,252 ^
  /lib:"%FW%" /lib:"%FW%\WPF" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll ^
  /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.Xaml.dll /r:WindowsBase.dll /r:PresentationCore.dll /r:PresentationFramework.dll ^
  src\*.cs

if errorlevel 1 (
    echo.
    echo [GAGAL] Ada error saat build. Screenshot / copy pesan di atas lalu kirim ke Claude.
    echo.
    pause
    exit /b 1
)

echo.
echo [BERHASIL] ZMB.exe sudah jadi di folder ini.
echo Klik dua kali ZMB.exe untuk menjalankan.
echo.
pause
