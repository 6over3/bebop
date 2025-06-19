#!/bin/sh

set -e

# Define the WASI version
export WASI_VERSION=25
export WASI_VERSION_FULL=${WASI_VERSION}.0
# Path to the hidden WASI SDK directory
install_path="$(readlink -f ~/.wasi-sdk)"
export WASI_SDK_PATH="$install_path/wasi-sdk-${WASI_VERSION_FULL}"

# Function to get architecture
get_arch() {
    case "$(uname -m)" in
        x86_64|amd64)
            echo "x86_64"
            ;;
        arm64|aarch64)
            echo "arm64"
            ;;
        *)
            echo "Unsupported architecture: $(uname -m)"
            exit 1
            ;;
    esac
}

# Function to download and extract WASI SDK
download_and_extract() {
    os_arch=$1
    file_name="wasi-sdk-${WASI_VERSION_FULL}-${os_arch}.tar.gz"
    extracted_dir="wasi-sdk-${WASI_VERSION_FULL}-${os_arch}"
    url="https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-${WASI_VERSION}/$file_name"
    
    echo "Downloading $file_name from $url"
    
    # Download the file
    if ! wget "$url"; then
        echo "Failed to download $file_name"
        exit 1
    fi
    
    # Extract the file
    tar -xvf "$file_name" -C "$install_path"
    
    # Rename the extracted directory to the expected name
    if [ -d "$install_path/$extracted_dir" ]; then
        mv "$install_path/$extracted_dir" "$WASI_SDK_PATH"
        echo "Renamed $extracted_dir to wasi-sdk-${WASI_VERSION_FULL}"
    else
        echo "Warning: Expected directory $extracted_dir not found after extraction"
        echo "Available directories:"
        ls -la "$install_path"
    fi
    
    # Clean up: remove the downloaded tar.gz file
    rm "$file_name"
}

cleanup() {
    # Check if any tar files exist and delete them
    for file in wasi-sdk-${WASI_VERSION_FULL}-*.tar.gz; do
        if [ -f "$file" ]; then
            echo "Cleaning up $file"
            rm "$file"
        fi
    done
}

# Trap to ensure cleanup on exit
trap cleanup EXIT

# Check if the .wasi-sdk directory exists and delete it if it does
if [ -d "$install_path" ]; then
    echo "Existing WASI SDK directory found. Deleting it..."
    rm -rf "$install_path"
fi

# Create a new hidden directory for WASI SDK
mkdir -p "$install_path"

# Get architecture
arch=$(get_arch)

# Check the operating system and architecture
case "$(uname -s)" in
Darwin)
    echo "MacOS detected with architecture: $arch"
    download_and_extract "${arch}-macos"
    ;;
Linux)
    echo "Linux detected with architecture: $arch"
    download_and_extract "${arch}-linux"
    ;;
*)
    echo "Unsupported operating system: $(uname -s)"
    exit 1
    ;;
esac

echo "WASI SDK installed successfully at $WASI_SDK_PATH"

# Verify installation
if [ -d "$WASI_SDK_PATH" ]; then
    echo "Installation verified: WASI SDK directory exists"
    # List contents to confirm
    ls -la "$WASI_SDK_PATH" | head -10
else
    echo "Warning: WASI SDK directory not found after installation"
    echo "Contents of install path:"
    ls -la "$install_path"
fi

# Check if wasi-experimental workload is already installed
if dotnet workload list | grep -q 'wasi-experimental'; then
    echo "WASI experimental workload is already installed."
else
    echo "Installing WASI experimental workload..."
    dotnet workload install wasi-experimental
    echo "WASI workload installed successfully"
fi