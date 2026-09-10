@echo off
setlocal
cd /d "%~dp0"
if exist ColorShade.exe (
  start "" /wait ColorShade.exe --restore
  exit /b
)
if exist artifacts\win-x64\ColorShade.exe (
  start "" /wait artifacts\win-x64\ColorShade.exe --restore
  exit /b
)
echo Build ColorShade first, or place this file next to ColorShade.exe.
pause
