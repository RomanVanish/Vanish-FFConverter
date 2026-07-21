@echo off
rem Build Vanish-FFConverter with the built-in .NET Framework 4.8 compiler.
rem No external SDK. ffmpeg is fetched by the program itself on first run.
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /warn:4 /codepage:65001 /target:winexe /out:Vanish-FFConverter.exe ^
  /win32icon:V.ico /win32manifest:app.manifest ^
  /r:System.Web.Extensions.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo *** BUILD FAILED ***
  exit /b 1
)
echo OK: Vanish-FFConverter.exe
