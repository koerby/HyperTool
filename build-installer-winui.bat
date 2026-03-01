@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "VERSION=2.0.0"
set "NO_PAUSE=false"
set "NO_VERSION_PROMPT=false"
set "VERSION_ARG="
set "VERSION_PROMPT=Bitte Version fuer den WinUI Installer eingeben (Default 2.0.0): "
set "REQUIRED_DOTNET_MAJOR=8"
set "REQUIRED_DOTNET_VERSION=8.0.0"
set "DOTNET_RUNTIME_URL=https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

for %%A in (%*) do (
    if /I "%%~A"=="no-pause" set "NO_PAUSE=true"
    if /I "%%~A"=="no-version-prompt" set "NO_VERSION_PROMPT=true"
    echo %%~A | findstr /I /B "version=" >nul && set "VERSION_ARG=%%~A"
)

if defined VERSION_ARG (
    for /f "tokens=1,* delims==" %%K in ("%VERSION_ARG%") do set "VERSION=%%L"
)

if not defined VERSION_ARG if /I "%NO_VERSION_PROMPT%"=="false" (
    set /p "VERSION=!VERSION_PROMPT!"
)

if not defined VERSION set "VERSION=2.0.0"

if not exist "%ROOT%dist\HyperTool.WinUI\HyperTool.exe" (
    echo WinUI DIST-Build nicht gefunden. Fuehre zuerst build-winui.bat aus.
    if /I "%NO_PAUSE%"=="false" pause
    exit /b 1
)

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo Inno Setup wurde nicht gefunden.
    echo Bitte Inno Setup 6 installieren: https://jrsoftware.org/isinfo.php
    if /I "%NO_PAUSE%"=="false" pause
    exit /b 1
)

set "OUT_DIR=%ROOT%dist\installer-winui"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

set "PREREQ_DIR=%OUT_DIR%\prerequisites"
if not exist "%PREREQ_DIR%" mkdir "%PREREQ_DIR%"
set "DOTNET_RUNTIME_INSTALLER=%PREREQ_DIR%\windowsdesktop-runtime-%REQUIRED_DOTNET_MAJOR%-x64.exe"

echo Pruefe .NET Desktop Runtime Installer (%REQUIRED_DOTNET_VERSION%)...
if not exist "%DOTNET_RUNTIME_INSTALLER%" (
    echo Lade Runtime Installer von %DOTNET_RUNTIME_URL% ...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%DOTNET_RUNTIME_URL%' -OutFile '%DOTNET_RUNTIME_INSTALLER%'"
    if errorlevel 1 (
        echo Download der .NET Desktop Runtime fehlgeschlagen.
        if /I "%NO_PAUSE%"=="false" pause
        exit /b 1
    )
)

echo Erzeuge WinUI Installer fuer Version %VERSION%...
"%ISCC%" /DMyAppVersion=%VERSION% /DMySourceDir="%ROOT%dist\HyperTool.WinUI" /DMyOutputDir="%OUT_DIR%" /DRequiredDotNetVersion=%REQUIRED_DOTNET_VERSION% /DRequiredDotNetMajor=%REQUIRED_DOTNET_MAJOR% /DDotNetRuntimeInstaller="%DOTNET_RUNTIME_INSTALLER%" "%ROOT%installer\HyperTool.iss"

if errorlevel 1 (
    echo WinUI Installer-Erstellung fehlgeschlagen.
    if /I "%NO_PAUSE%"=="false" pause
    exit /b 1
)

echo.
echo SUCCESS: WinUI Installer erstellt in %OUT_DIR%
dir /b "%OUT_DIR%"

if /I "%NO_PAUSE%"=="false" pause
exit /b 0
