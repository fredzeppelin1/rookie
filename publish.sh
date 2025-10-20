#!/bin/bash
# Publish script for creating GitHub release assets
# Builds self-contained executables for Windows x64 and macOS ARM64

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}================================${NC}"
echo -e "${BLUE}Rookie Sideloader - Release Build${NC}"
echo -e "${BLUE}================================${NC}"
echo ""

# Check if dotnet CLI is available
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}ERROR: dotnet CLI not found!${NC}"
    echo "Please install .NET 9.0 SDK from:"
    echo "https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
fi

# Check for version file
if [ ! -f "version" ]; then
    echo -e "${RED}ERROR: version file not found!${NC}"
    echo "Please create a 'version' file with the version number (e.g., '1.0.0')"
    exit 1
fi

# Read version from file
VERSION=$(cat version | tr -d '\n' | tr -d '\r')
echo -e "${GREEN}Building version: ${VERSION}${NC}"
echo ""

# Configuration
APP_NAME="Rookie"
PROJECT_FILE="AndroidSideloader.csproj"
OUTPUT_DIR="releases"
CONFIG="Release"

# Save the project root directory
PROJECT_ROOT="$(pwd)"

# Platform configurations (RID|Display Name pairs)
PLATFORMS="win-x64:Windows-x64 osx-arm64:macOS-arm64"

# Clean previous releases
echo -e "${YELLOW}[1/5] Cleaning previous releases...${NC}"
if [ -d "$OUTPUT_DIR" ]; then
    rm -rf "$OUTPUT_DIR"
fi
mkdir -p "$OUTPUT_DIR"

# Clean build artifacts
echo -e "${YELLOW}[2/5] Cleaning build artifacts...${NC}"
dotnet clean --configuration $CONFIG > /dev/null

# Also remove bin/Release directory to ensure clean build
if [ -d "bin/${CONFIG}" ]; then
    echo -e "  Removing bin/${CONFIG}..."
    rm -rf "bin/${CONFIG}"
fi

# Restore dependencies
echo -e "${YELLOW}[3/5] Restoring dependencies...${NC}"
dotnet restore

echo ""
echo -e "${YELLOW}[4/5] Publishing for all platforms...${NC}"
echo ""

# Publish for each platform
for PLATFORM_PAIR in $PLATFORMS; do
    RID="${PLATFORM_PAIR%%:*}"
    PLATFORM_NAME="${PLATFORM_PAIR##*:}"
    PUBLISH_DIR="bin/${CONFIG}/net9.0/${RID}/publish"
    ARCHIVE_NAME="${APP_NAME}-${VERSION}-${PLATFORM_NAME}"

    echo -e "${BLUE}Building for ${PLATFORM_NAME}...${NC}"

    # Publish with options:
    # - Self-contained: includes .NET runtime
    # - Single file: packages everything into one executable
    # - Ready to run: improves startup time
    # - Trimmed: removes unused code (disabled for now due to WebView issues we saw earlier)
    dotnet publish "$PROJECT_FILE" \
        --configuration $CONFIG \
        --runtime $RID \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=false \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        --output "$PUBLISH_DIR"

    if [ $? -ne 0 ]; then
        echo -e "${RED}ERROR: Failed to publish for ${PLATFORM_NAME}${NC}"
        exit 1
    fi

    # Create platform-specific archive
    echo -e "  ${GREEN}✓${NC} Creating ${ARCHIVE_NAME}.zip..."
    cd "$PUBLISH_DIR"
    zip -r -q "${PROJECT_ROOT}/${OUTPUT_DIR}/${ARCHIVE_NAME}.zip" .
    cd - > /dev/null

    echo -e "${GREEN}✓ ${PLATFORM_NAME} build complete${NC}"
    echo ""
done

echo -e "${YELLOW}[5/5] Creating release notes template...${NC}"

# Create release notes template
RELEASE_NOTES="${OUTPUT_DIR}/RELEASE_NOTES.md"
cat > "$RELEASE_NOTES" << EOF
# Rookie Sideloader v${VERSION}

## What's New
- [Add your changelog here]

## Downloads

### Windows
- **${APP_NAME}-${VERSION}-Windows-x64.zip** - Windows 10/11 (x64)
  - Extract and run \`AndroidSideloader.exe\`

### macOS
- **${APP_NAME}-${VERSION}-macOS-arm64.zip** - macOS (Apple Silicon)
  - Extract and run \`AndroidSideloader\`

## System Requirements
- Windows: Windows 10 (1809+) or Windows 11
- macOS: macOS 10.15 (Catalina) or later, Apple Silicon (M1/M2/M3)

## Installation Notes

### macOS
If you get a security warning:
1. Right-click the app and select "Open"
2. Click "Open" in the security dialog
3. Or run: \`xattr -cr AndroidSideloader\` to remove quarantine flag

### Windows
If Windows Defender blocks the app:
1. Click "More info"
2. Click "Run anyway"

## Known Issues
- [Add any known issues here]

## Credits
- Original Rookie by VRPirates
- Migrated to Avalonia/.NET 9 for cross-platform support
EOF

echo ""
echo -e "${BLUE}================================${NC}"
echo -e "${GREEN}✓ All builds completed successfully!${NC}"
echo -e "${BLUE}================================${NC}"
echo ""
echo "Release artifacts created in: ${OUTPUT_DIR}/"
echo ""
echo "Files created:"
ls -lh "$OUTPUT_DIR/" | grep -v "^total" | awk '{print "  - " $9 " (" $5 ")"}'
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Review the release notes: ${RELEASE_NOTES}"
echo "2. Test the builds on target platforms"
echo "3. Create a GitHub release:"
echo "   gh release create v${VERSION} ${OUTPUT_DIR}/* --title \"Rookie v${VERSION}\" --notes-file ${RELEASE_NOTES}"
echo ""
