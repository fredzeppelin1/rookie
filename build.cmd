@echo off
REM Modern .NET 9.0 build script using dotnet CLI
REM No Visual Studio required!

REM Default to Release if no argument is provided
SET CONFIG=Release
IF NOT "%1"=="" (
    IF /I "%1"=="debug" SET CONFIG=Debug
)

REM Check if dotnet CLI is available
WHERE dotnet >nul 2>nul
IF %ERRORLEVEL% NEQ 0 (
    echo dotnet CLI not found! Please install .NET 9.0 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

echo Building Rookie Sideloader in %CONFIG% configuration...
echo.

REM Restore dependencies
echo [1/2] Restoring dependencies...
dotnet restore

REM Build the project
echo [2/2] Building project...
dotnet build --configuration %CONFIG% --no-restore

IF %ERRORLEVEL% EQU 0 (
    echo.
    echo ================================
    echo Build completed successfully!
    echo Configuration: %CONFIG%
    echo Output: bin\%CONFIG%\net9.0\
    echo ================================
) ELSE (
    echo.
    echo ================================
    echo Build FAILED!
    echo ================================
    exit /b %ERRORLEVEL%
)