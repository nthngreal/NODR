@echo off
setlocal
cd /d "%~dp0"
title NODR Publisher

echo Publishing self-contained NODR for Windows x64...
dotnet publish "NODR.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "dist\win-x64"
if errorlevel 1 goto :error

echo.
echo Done. Release executable:
echo %CD%\dist\win-x64\NODR.exe
echo.
pause
exit /b 0

:error
echo.
echo Publish failed. This step requires the .NET 10 SDK on the build PC.
echo.
pause
exit /b 1
