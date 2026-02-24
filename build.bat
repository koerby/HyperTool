@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "CONFIG=Release"
set "RUNTIME=win-x64"
set "SELF_CONTAINED=false"

if /I "%~1"=="self-contained" (
    set "SELF_CONTAINED=true"
)

echo [1/4] Restore...
dotnet restore HyperTool.sln
if errorlevel 1 goto :fail

echo [2/4] Build...
dotnet build HyperTool.sln -c %CONFIG% --no-restore
if errorlevel 1 goto :fail

set "DIST_DIR=%ROOT%dist\HyperTool"
if exist "%DIST_DIR%" rmdir /s /q "%DIST_DIR%"
mkdir "%DIST_DIR%"

echo [3/4] Publish to dist...
dotnet publish src\HyperTool\HyperTool.csproj -c %CONFIG% -r %RUNTIME% --self-contained %SELF_CONTAINED% --no-build -o "%DIST_DIR%"
if errorlevel 1 goto :fail

echo [4/4] Copy default config...
copy /Y "%ROOT%HyperTool.config.json" "%DIST_DIR%\HyperTool.config.json" >nul

echo.
echo Fertig. Ausgabe liegt in:
echo %DIST_DIR%
echo.
echo Hinweis: Fuer self-contained Build starte mit:
echo build.bat self-contained
goto :eof

:fail
echo.
echo FEHLER: Build/Publish fehlgeschlagen.
exit /b 1
