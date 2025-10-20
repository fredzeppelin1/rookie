# Publishing Rookie Sideloader

This document describes how to create release builds for GitHub.

## Prerequisites

1. **.NET 9.0 SDK** installed
   - Download: https://dotnet.microsoft.com/download/dotnet/9.0

2. **Version file** - Update the `version` file with the new version number
   ```bash
   echo "2.35.0" > version
   ```

3. **GitHub CLI** (optional, for automated release creation)
   ```bash
   # macOS
   brew install gh

   # Or download from: https://cli.github.com/
   ```

## Building Release Assets

### Automatic (Recommended)

Run the publish script:

**On macOS/Linux:**
```bash
./publish.sh
```

**On Windows:**
```cmd
publish.cmd
```

This will:
- Clean previous builds
- Restore dependencies
- Build self-contained executables for:
  - **Windows x64** - Single .exe file with bundled .NET runtime
  - **macOS ARM64** - Single executable for Apple Silicon Macs
- Create ZIP archives:
  - `Rookie-{VERSION}-Windows-x64.zip`
  - `Rookie-{VERSION}-macOS-arm64.zip`
- Generate a release notes template

All artifacts will be in the `releases/` directory.

### Manual Publishing

If you need to publish manually or for other platforms:

#### Windows x64
```bash
dotnet publish AndroidSideloader.csproj \
    --configuration Release \
    --runtime win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    --output bin/Release/net9.0/win-x64/publish
```

#### macOS ARM64 (Apple Silicon)
```bash
dotnet publish AndroidSideloader.csproj \
    --configuration Release \
    --runtime osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    --output bin/Release/net9.0/osx-arm64/publish
```

#### macOS x64 (Intel)
```bash
dotnet publish AndroidSideloader.csproj \
    --configuration Release \
    --runtime osx-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    --output bin/Release/net9.0/osx-x64/publish
```

#### Linux x64
```bash
dotnet publish AndroidSideloader.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    --output bin/Release/net9.0/linux-x64/publish
```

## Creating a GitHub Release

### Using GitHub CLI (Recommended)

1. Update `releases/RELEASE_NOTES.md` with changelog
2. Create the release:

```bash
VERSION=$(cat version)
gh release create "v${VERSION}" \
    releases/* \
    --title "Rookie Sideloader v${VERSION}" \
    --notes-file releases/RELEASE_NOTES.md
```

### Using GitHub Web Interface

1. Go to: https://github.com/VRPirates/rookie/releases/new
2. Create a new tag: `v{VERSION}` (e.g., `v2.35.0`)
3. Upload the files from `releases/` directory
4. Copy content from `releases/RELEASE_NOTES.md` to release description
5. Click "Publish release"

## Testing Release Builds

### Windows
1. Extract `Rookie-{VERSION}-Windows-x64.zip`
2. Run `AndroidSideloader.exe`
3. Test core functionality:
   - Device detection
   - Game list loading
   - APK installation
   - Settings persistence

### macOS
1. Extract archive:
   ```bash
   unzip Rookie-{VERSION}-macOS-arm64.zip
   ```

2. Remove quarantine flag (if needed):
   ```bash
   xattr -cr AndroidSideloader
   ```

3. Run the app:
   ```bash
   ./AndroidSideloader
   ```

4. Test core functionality

## Troubleshooting

### macOS: "Cannot be opened because the developer cannot be verified"

**Solution 1:** Right-click → Open → Click "Open" in dialog

**Solution 2:** Remove quarantine flag:
```bash
xattr -cr AndroidSideloader
```

**Solution 3:** Add to System Settings:
1. Go to System Settings → Privacy & Security
2. Scroll down and click "Open Anyway"

### Windows Defender Blocking

1. Click "More info" in SmartScreen dialog
2. Click "Run anyway"

Or add an exclusion in Windows Defender settings.

### Build Fails with WebView Errors

The publish script disables trimming (`-p:PublishTrimmed=false`) to avoid issues with the WebView dependencies. If you encounter WebView-related errors:

1. Ensure you're not using `-p:PublishTrimmed=true`
2. Check that all WebView dependencies are properly referenced
3. Try cleaning and rebuilding:
   ```bash
   dotnet clean
   ./publish.sh
   ```

## Publish Options Explained

- **`--self-contained true`** - Includes .NET runtime (users don't need .NET installed)
- **`-p:PublishSingleFile=true`** - Packages into a single executable
- **`-p:PublishTrimmed=false`** - Keeps all code (disabled for WebView compatibility)
- **`-p:IncludeNativeLibrariesForSelfExtract=true`** - Includes native libs in single file
- **`-p:DebugType=None`** - Excludes debug symbols (smaller size)

## File Sizes

Approximate sizes for self-contained builds:

- Windows x64: ~80-120 MB (compressed: ~30-40 MB)
- macOS ARM64: ~60-90 MB (compressed: ~25-35 MB)

These include the full .NET 9 runtime, so users don't need to install anything.

## Version Management

The version is stored in the `version` file at the project root. Update this file before running the publish script:

```bash
echo "2.35.0" > version
git add version
git commit -m "Bump version to 2.35.0"
git push
```

## Changelog Template

When creating a release, include:

```markdown
## What's New
- Feature: Description of new feature
- Fix: Description of bug fix
- Improvement: Description of improvement

## Breaking Changes
- List any breaking changes (if applicable)

## Known Issues
- List any known issues

## Full Changelog
https://github.com/VRPirates/rookie/compare/v{PREV}...v{CURRENT}
```

## Additional Resources

- .NET Publishing Guide: https://learn.microsoft.com/en-us/dotnet/core/deploying/
- Runtime Identifiers: https://learn.microsoft.com/en-us/dotnet/core/rid-catalog
- GitHub Releases: https://docs.github.com/en/repositories/releasing-projects-on-github
