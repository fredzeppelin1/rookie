#!/bin/bash
# Modern .NET 9.0 build script using dotnet CLI
# Cross-platform build script for macOS and Linux

# Default to Release if no argument is provided
CONFIG="Release"
if [ ! -z "$1" ]; then
    if [ "$1" = "debug" ] || [ "$1" = "Debug" ]; then
        CONFIG="Debug"
    fi
fi

# Check if dotnet CLI is available
if ! command -v dotnet &> /dev/null; then
    echo "dotnet CLI not found! Please install .NET 9.0 SDK from:"
    echo "https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
fi

echo "Building Rookie Sideloader in $CONFIG configuration..."
echo ""

# Restore dependencies
echo "[1/2] Restoring dependencies..."
dotnet restore

# Build the project
echo "[2/2] Building project..."
dotnet build --configuration $CONFIG --no-restore

if [ $? -eq 0 ]; then
    echo ""
    echo "================================"
    echo "Build completed successfully!"
    echo "Configuration: $CONFIG"
    echo "Output: bin/$CONFIG/net9.0/"
    echo "================================"
else
    echo ""
    echo "================================"
    echo "Build FAILED!"
    echo "================================"
    exit 1
fi
