@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "VERSION=1.2.0"
set "NO_PAUSE=false"
set "VERSION_ARG="

for %%A in (%*) do (
    if /I "%%~A"=="no-pause" set "NO_PAUSE=true"
    echo %%~A | findstr /I /B "version=" >nul && set "VERSION_ARG=%%~A"
)

if defined VERSION_ARG (
    for /f "tokens=1,* delims==" %%K in ("%VERSION_ARG%") do set "VERSION=%%L"
)

if not exist "%ROOT%dist\HyperTool\HyperTool.exe" (
    echo DIST-Build nicht gefunden. Fuehre zuerst build.bat aus.
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

set "OUT_DIR=%ROOT%dist\installer"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo Erzeuge Installer fuer Version %VERSION%...
"%ISCC%" /DMyAppVersion=%VERSION% /DMySourceDir="%ROOT%dist\HyperTool" /DMyOutputDir="%OUT_DIR%" "%ROOT%installer\HyperTool.iss"

if errorlevel 1 (
    echo Installer-Erstellung fehlgeschlagen.
    if /I "%NO_PAUSE%"=="false" pause
    exit /b 1
)

echo.
echo SUCCESS: Installer erstellt in %OUT_DIR%
dir /b "%OUT_DIR%"

if /I "%NO_PAUSE%"=="false" pause
exit /b 0
