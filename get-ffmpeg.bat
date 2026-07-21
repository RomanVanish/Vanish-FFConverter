@echo off
rem Downloads ffmpeg into the ffmpeg\ subfolder (works with Cyrillic paths).
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\get-ffmpeg.ps1"
