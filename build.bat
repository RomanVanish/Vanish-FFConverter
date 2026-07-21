@echo off
rem Build Vanish-FFConverter with the built-in .NET Framework 4.8 compiler.
rem If ffmpeg is missing, it is downloaded first (works with Cyrillic paths).
cd /d "%~dp0"

if not exist "ffmpeg\ffmpeg.exe" (
  echo ffmpeg not found, downloading first...
  powershell -NoProfile -ExecutionPolicy Bypass -File ".\get-ffmpeg.ps1" -NoPause
  if errorlevel 1 (
    echo.
    echo *** Failed to get ffmpeg ***
    exit /b 1
  )
)

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /warn:4 /codepage:65001 /target:winexe /out:Vanish-FFConverter.exe ^
  /win32icon:V.ico /win32manifest:app.manifest /r:System.Web.Extensions.dll src\*.cs
if errorlevel 1 (
  echo.
  echo *** BUILD FAILED ***
  exit /b 1
)
echo OK: Vanish-FFConverter.exe
