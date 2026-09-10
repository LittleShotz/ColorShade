@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 10 SDK x64, then run this file again.
  echo https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
dotnet run --project tests\ColorShade.Tests\ColorShade.Tests.csproj -c Release
if errorlevel 1 goto failed
dotnet publish src\ColorShade\ColorShade.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts\win-x64
if errorlevel 1 goto failed
echo.
echo Built artifacts\win-x64\ColorShade.exe
echo Double-click it, then open ColorShade from the system tray.
pause
exit /b 0
:failed
echo Build failed. Read the error above.
pause
exit /b 1
