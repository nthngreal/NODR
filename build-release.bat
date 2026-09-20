@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title NODR Release Builder

set "VERSION=1.14.15"
set "ARTIFACTS=%CD%\artifacts"
set "PUBLISH=%CD%\dist\win-x64"

echo [1/5] Cleaning old release output...
if exist "%ARTIFACTS%" rmdir /s /q "%ARTIFACTS%"
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
mkdir "%ARTIFACTS%" >nul

echo [2/5] Restoring packages...
dotnet restore "NODR.csproj"
if errorlevel 1 goto :error

echo [3/5] Publishing self-contained Windows x64 build...
dotnet publish "NODR.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%PUBLISH%"
if errorlevel 1 goto :error

if not exist "%PUBLISH%\NODR.exe" (
  echo ERROR: NODR.exe was not produced.
  goto :error
)

echo [4/5] Creating portable ZIP...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Compress-Archive -Path '%PUBLISH%\*' -DestinationPath '%ARTIFACTS%\NODR-Portable-x64-v%VERSION%.zip' -Force"
if errorlevel 1 goto :error

echo [5/5] Building installer if Inno Setup 6 is installed...
set "ISCC="
REM Prefer the known Inno Setup 6 install locations. An older ISCC on PATH
REM must not override the current compiler because dark styling requires 6.6+.
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  where ISCC.exe >nul 2>nul
  if not errorlevel 1 for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%I"
)

if defined ISCC (
  echo Found Inno Setup: "%ISCC%"
  "%ISCC%" /? | findstr /I /C:"Inno Setup"
  echo Building forced-dark NODR installer...
  "%ISCC%" "%CD%\installer\NODR.iss"
  if errorlevel 1 goto :error
) else (
  echo.
  echo Inno Setup 6 was not found. Portable ZIP is ready.
  echo Checked PATH, Program Files, Program Files ^(x86^), and LOCALAPPDATA.
)

echo.
echo Release artifacts:
dir /b "%ARTIFACTS%"
echo.
echo IMPORTANT: test both artifacts on Windows before publishing them.
pause
exit /b 0

:error
echo.
echo RELEASE BUILD FAILED. See the error above.
pause
exit /b 1
