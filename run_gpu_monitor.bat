@echo off
cd /d "%~dp0"
if not exist "bin\NvidiaCrashDetective.exe" call build_native.bat
if errorlevel 1 (
  pause
  exit /b 1
)
start "" "bin\NvidiaCrashDetective.exe"
