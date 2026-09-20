@echo off
setlocal
cd /d "%~dp0"
title NODR Launcher
set "LOG=%LOCALAPPDATA%\NODR\nodr.log"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo NODR could not start because the .NET SDK was not found.
  echo.
  echo This source build currently requires the .NET 10 SDK.
  echo Install it from: https://dotnet.microsoft.com/download/dotnet/10.0
  echo Then run this file again.
  echo.
  pause
  exit /b 1
)

if exist "%LOG%" del /q "%LOG%" >nul 2>nul

rem Never reuse generated WPF/BAML artifacts from another package version.
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

echo Restoring NODR packages...
dotnet restore "NODR.csproj"
if errorlevel 1 goto :error

echo.
echo Building NODR...
dotnet build "NODR.csproj" --no-restore
if errorlevel 1 goto :error

echo.
echo Starting NODR...
dotnet run --project "NODR.csproj" --no-build
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" goto :error

rem A WPF process can terminate cleanly before a window is shown. Treat that as a startup failure too.
if exist "%LOG%" (
  findstr /C:"STARTUP MainWindow shown" "%LOG%" >nul 2>nul
  if errorlevel 1 goto :silent_exit
) else (
  goto :silent_exit
)
exit /b 0

:silent_exit
echo.
echo NODR exited before the main window was shown.
echo.
if exist "%LOG%" (
  echo ---- NODR startup log ----
  type "%LOG%"
  echo ---- end log ----
) else (
  echo No startup log was created.
)
echo.
echo Screenshot this window and send it to ChatGPT.
echo.
pause
exit /b 1

:error
echo.
echo NODR did not start successfully.
if exist "%LOG%" (
  echo.
  echo ---- NODR crash log ----
  type "%LOG%"
  echo ---- end log ----
)
echo.
echo Copy or screenshot the error text above and send it to ChatGPT.
echo.
pause
if not defined EXITCODE set "EXITCODE=1"
exit /b %EXITCODE%
