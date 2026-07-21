@echo off
rem Headless test harness: test.exe <media dir> <out dir>
cd /d %~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /codepage:65001 /target:exe /main:VanishFF.TestRunner ^
  /out:test.exe /r:System.Web.Extensions.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  src\*.cs test\TestRunner.cs
if errorlevel 1 exit /b 1
echo OK: test.exe
