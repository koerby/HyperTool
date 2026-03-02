@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "VERSION=2.0.0"
set "NO_PAUSE=false"
set "NO_VERSION_PROMPT=false"
set "VERSION_ARG="
set "VERSION_PROMPT=Bitte Version fuer den WinUI Installer eingeben (Default 2.0.0): "

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
if exist "%OUT_DIR%\prerequisites" rmdir /s /q "%OUT_DIR%\prerequisites"

echo Erzeuge WinUI Installer fuer Version %VERSION%...
"%ISCC%" /DMyAppVersion=%VERSION% /DMySourceDir="%ROOT%dist\HyperTool.WinUI" /DMyOutputDir="%OUT_DIR%" "%ROOT%installer\HyperTool.iss"

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
