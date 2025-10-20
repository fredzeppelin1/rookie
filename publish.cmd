@echo off
REM Publish script for creating GitHub release assets
REM Builds self-contained executables for Windows x64 and macOS ARM64

setlocal EnableDelayedExpansion

REM Colors (Windows 10+ supports ANSI colors with VT100 enabled)
echo [94m================================[0m
echo [94mRookie Sideloader - Release Build[0m
echo [94m================================[0m
echo.

REM Check if dotnet CLI is available
where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [91mERROR: dotnet CLI not found![0m
    echo Please install .NET 9.0 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

REM Check for version file
if not exist "version" (
    echo [91mERROR: version file not found![0m
    echo Please create a 'version' file with the version number ^(e.g., '1.0.0'^)
    exit /b 1
)

REM Read version from file
set /p VERSION=<version
REM Trim whitespace
for /f "tokens=* delims= " %%a in ("%VERSION%") do set VERSION=%%a
echo [92mBuilding version: %VERSION%[0m
echo.

REM Configuration
set APP_NAME=Rookie
set PROJECT_FILE=AndroidSideloader.csproj
set OUTPUT_DIR=releases
set CONFIG=Release

REM Save the project root directory
set PROJECT_ROOT=%CD%

REM Clean previous releases
echo [93m[1/5] Cleaning previous releases...[0m
if exist "%OUTPUT_DIR%" (
    rmdir /s /q "%OUTPUT_DIR%" >nul 2>&1
)
mkdir "%OUTPUT_DIR%" >nul 2>&1

REM Clean bin/obj
echo [93m[2/5] Cleaning build artifacts...[0m
dotnet clean --configuration %CONFIG% >nul 2>&1

REM Restore dependencies
echo [93m[3/5] Restoring dependencies...[0m
dotnet restore

echo.
echo [93m[4/5] Publishing for all platforms...[0m
echo.

REM ==========================================
REM Build Windows x64
REM ==========================================
set RID=win-x64
set PLATFORM_NAME=Windows-x64
set PUBLISH_DIR=bin\%CONFIG%\net9.0\%RID%\publish
set ARCHIVE_NAME=%APP_NAME%-%VERSION%-%PLATFORM_NAME%

echo [94mBuilding for %PLATFORM_NAME%...[0m

dotnet publish "%PROJECT_FILE%" ^
    --configuration %CONFIG% ^
    --runtime %RID% ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:PublishTrimmed=false ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    --output "%PUBLISH_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo [91mERROR: Failed to publish for %PLATFORM_NAME%[0m
    exit /b 1
)

REM Create ZIP archive for Windows
echo   [92m√[0m Creating %ARCHIVE_NAME%.zip...
pushd "%PUBLISH_DIR%" >nul
if exist "%PROJECT_ROOT%\%OUTPUT_DIR%\%ARCHIVE_NAME%.zip" del "%PROJECT_ROOT%\%OUTPUT_DIR%\%ARCHIVE_NAME%.zip" >nul 2>&1

REM Use PowerShell to create ZIP (available on Windows 10+)
powershell -Command "Compress-Archive -Path * -DestinationPath '%PROJECT_ROOT%\%OUTPUT_DIR%\%ARCHIVE_NAME%.zip' -Force" >nul 2>&1

if %ERRORLEVEL% NEQ 0 (
    echo [91m  ERROR: Failed to create ZIP. Trying alternative method...[0m
    REM Fallback: Use built-in tar (Windows 10 1803+)
    tar -czf "%PROJECT_ROOT%\%OUTPUT_DIR%\%ARCHIVE_NAME%.zip" * >nul 2>&1
)

popd >nul
echo [92m√ %PLATFORM_NAME% build complete[0m
echo.

REM ==========================================
REM Build macOS ARM64
REM ==========================================
set RID=osx-arm64
set PLATFORM_NAME=macOS-arm64
set PUBLISH_DIR=bin\%CONFIG%\net9.0\%RID%\publish
set ARCHIVE_NAME=%APP_NAME%-%VERSION%-%PLATFORM_NAME%

echo [94mBuilding for %PLATFORM_NAME%...[0m

dotnet publish "%PROJECT_FILE%" ^
    --configuration %CONFIG% ^
    --runtime %RID% ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:PublishTrimmed=false ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    --output "%PUBLISH_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo [91mERROR: Failed to publish for %PLATFORM_NAME%[0m
    exit /b 1
)

REM Create ZIP archive for macOS
echo   [92m√[0m Creating %ARCHIVE_NAME%.zip...
pushd "%PUBLISH_DIR%" >nul
if exist "%PROJECT_ROOT%\%OUTPUT_DIR%\%ARCHIVE_NAME%.zip" del "%PROJECT_ROOT%\%OUTPUT_DIR%\%ARCHIVE_NAME%.zip" >nul 2>&1
powershell -Command "Compress-Archive -Path * -DestinationPath '%PROJECT_ROOT%\%OUTPUT_DIR%\%ARCHIVE_NAME%.zip' -Force" >nul 2>&1

if %ERRORLEVEL% NEQ 0 (
    echo [91m  WARNING: Failed to create ZIP for macOS.[0m
)

popd >nul
echo [92m√ %PLATFORM_NAME% build complete[0m
echo.

REM ==========================================
REM Create release notes template
REM ==========================================
echo [93m[5/5] Creating release notes template...[0m

set RELEASE_NOTES=%OUTPUT_DIR%\RELEASE_NOTES.md

(
echo # Rookie Sideloader v%VERSION%
echo.
echo ## What's New
echo - [Add your changelog here]
echo.
echo ## Downloads
echo.
echo ### Windows
echo - **%APP_NAME%-%VERSION%-Windows-x64.zip** - Windows 10/11 ^(x64^)
echo   - Extract and run `AndroidSideloader.exe`
echo.
echo ### macOS
echo - **%APP_NAME%-%VERSION%-macOS-arm64.zip** - macOS ^(Apple Silicon^)
echo   - Extract and run `AndroidSideloader`
echo.
echo ## System Requirements
echo - Windows: Windows 10 ^(1809+^) or Windows 11
echo - macOS: macOS 10.15 ^(Catalina^) or later, Apple Silicon ^(M1/M2/M3^)
echo.
echo ## Installation Notes
echo.
echo ### macOS
echo If you get a security warning:
echo 1. Right-click the app and select "Open"
echo 2. Click "Open" in the security dialog
echo 3. Or run: `xattr -cr AndroidSideloader` to remove quarantine flag
echo.
echo ### Windows
echo If Windows Defender blocks the app:
echo 1. Click "More info"
echo 2. Click "Run anyway"
echo.
echo ## Known Issues
echo - [Add any known issues here]
echo.
echo ## Credits
echo - Original Rookie by VRPirates
echo - Migrated to Avalonia/.NET 9 for cross-platform support
) > "%RELEASE_NOTES%"

echo.
echo [94m================================[0m
echo [92m√ All builds completed successfully![0m
echo [94m================================[0m
echo.
echo Release artifacts created in: %OUTPUT_DIR%\
echo.
echo Files created:
dir /b "%OUTPUT_DIR%" 2>nul | findstr /v "^$"
echo.
echo [93mNext steps:[0m
echo 1. Review the release notes: %RELEASE_NOTES%
echo 2. Test the builds on target platforms
echo 3. Create a GitHub release:
echo    gh release create v%VERSION% %OUTPUT_DIR%\* --title "Rookie v%VERSION%" --notes-file %RELEASE_NOTES%
echo.

endlocal
pause
