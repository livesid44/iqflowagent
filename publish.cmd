@echo off
REM ============================================================
REM  publish.cmd  —  Publish IQFlowAgent.Web to bin\publish
REM
REM  This script bypasses the Visual Studio Publish dialog and
REM  uses the CLI directly, which avoids any stale VS publish
REM  profiles that may still reference net9.0.
REM
REM  Usage:
REM    1. Open a Developer Command Prompt (or any cmd/PowerShell)
REM    2. cd to the repo root
REM    3. Run:  publish.cmd
REM
REM  Output: src\IQFlowAgent.Web\bin\publish\
REM ============================================================

setlocal

set PROJECT=src\IQFlowAgent.Web\IQFlowAgent.Web.csproj
set PROFILE=FolderPublish
set CONFIG=Release

echo.
echo [1/3] Cleaning bin and obj ...
if exist "src\IQFlowAgent.Web\bin" rmdir /s /q "src\IQFlowAgent.Web\bin"
if exist "src\IQFlowAgent.Web\obj" rmdir /s /q "src\IQFlowAgent.Web\obj"

echo.
echo [2/3] Restoring packages ...
dotnet restore "%PROJECT%"
if errorlevel 1 (
    echo ERROR: dotnet restore failed.
    exit /b 1
)

echo.
echo [3/3] Publishing ...
dotnet publish "%PROJECT%" -c %CONFIG% -p:PublishProfile=%PROFILE% --no-restore
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    exit /b 1
)

echo.
echo ============================================================
echo  Publish succeeded!
echo  Output: src\IQFlowAgent.Web\bin\publish\
echo ============================================================
endlocal
