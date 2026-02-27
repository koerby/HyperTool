@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "CONFIG=Release"
set "RUNTIME=win-x64"
set "SELF_CONTAINED=true"
set "NO_PAUSE=false"
set "CREATE_INSTALLER=false"
set "INSTALLER_DECIDED=false"
set "VERSION=1.2.0"
set "VERSION_ARG="
set "VERSION_PROMPT=Bitte Version eingeben (Default 1.2.0): "

for %%A in (%*) do (
    if /I "%%~A"=="self-contained" set "SELF_CONTAINED=true"
    if /I "%%~A"=="framework-dependent" set "SELF_CONTAINED=false"
    if /I "%%~A"=="no-pause" set "NO_PAUSE=true"
    if /I "%%~A"=="installer" (
        set "CREATE_INSTALLER=true"
        set "INSTALLER_DECIDED=true"
    )
    if /I "%%~A"=="no-installer" (
        set "CREATE_INSTALLER=false"
        set "INSTALLER_DECIDED=true"
    )
    echo %%~A | findstr /I /B "version=" >nul && set "VERSION_ARG=%%~A"
)

if defined VERSION_ARG (
    for /f "tokens=1,* delims==" %%K in ("%VERSION_ARG%") do set "VERSION=%%L"
)

if not defined VERSION_ARG (
    set /p "VERSION=!VERSION_PROMPT!"
)

if not defined VERSION set "VERSION=1.2.0"

echo ==========================================
echo HyperTool Build Script
echo ROOT: %ROOT%
echo CONFIG: %CONFIG%
echo RUNTIME: %RUNTIME%
echo SELF_CONTAINED: %SELF_CONTAINED%
echo VERSION: %VERSION%
echo ==========================================
echo.

echo [1/4] Restore...
dotnet restore HyperTool.sln
if errorlevel 1 goto :fail

echo [2/4] Build...
dotnet build HyperTool.sln -c %CONFIG% --no-restore /p:Version=%VERSION% /p:FileVersion=%VERSION% /p:AssemblyVersion=%VERSION% /p:InformationalVersion=%VERSION%
if errorlevel 1 goto :fail

set "DIST_DIR=%ROOT%dist\HyperTool"
if exist "%DIST_DIR%" rmdir /s /q "%DIST_DIR%"
mkdir "%DIST_DIR%"

echo [3/4] Publish to dist...
dotnet publish src\HyperTool\HyperTool.csproj -c %CONFIG% -r %RUNTIME% --self-contained %SELF_CONTAINED% -o "%DIST_DIR%" /p:Version=%VERSION% /p:FileVersion=%VERSION% /p:AssemblyVersion=%VERSION% /p:InformationalVersion=%VERSION%
if errorlevel 1 goto :fail

echo [4/4] Copy default config...
copy /Y "%ROOT%HyperTool.config.json" "%DIST_DIR%\HyperTool.config.json" >nul
if errorlevel 1 goto :fail

if /I "%INSTALLER_DECIDED%"=="false" (
    if /I "%NO_PAUSE%"=="false" (
        echo.
        choice /C JN /N /M "Installer auch erstellen? [J/N]: "
        if errorlevel 2 (
            set "CREATE_INSTALLER=false"
        ) else (
            set "CREATE_INSTALLER=true"
        )
    )
)

if /I "%CREATE_INSTALLER%"=="true" (
    echo [5/5] Erzeuge Installer...
    call "%ROOT%build-installer.bat" "version=%VERSION%" no-version-prompt no-pause
    if errorlevel 1 goto :fail
)

if not exist "%DIST_DIR%\HyperTool.exe" goto :fail

echo.
echo SUCCESS: Build und Publish abgeschlossen.
echo Ausgabe liegt in:
echo %DIST_DIR%
echo.
echo Inhalt von dist:
dir /b "%DIST_DIR%"
echo.
echo Hinweis: Fuer self-contained Build starte mit:
echo build.bat self-contained
echo Fuer framework-dependent Build:
echo build.bat framework-dependent
echo Fuer eine eigene Version:
echo build.bat version=1.2.3

if /I "%NO_PAUSE%"=="false" pause
goto :success

:fail
echo.
echo FEHLER: Build/Publish fehlgeschlagen.
echo Bitte die Ausgabe oben pruefen.
if /I "%NO_PAUSE%"=="false" pause
exit /b 1

:success
exit /b 0
