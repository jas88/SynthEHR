#!/bin/bash
# Generate directory-specific Directory.Build.props files with dynamic target framework values
# based on .NET SDK version. Auto-detects project types:
# - Libraries: multi-target all non-EOL versions
# - Tests (*.Tests): single-target latest
# - Executables (OutputType=Exe): single-target latest
#
# If any files differ from what's in git, commit and push (in CI), then exit with error.

set -e

# Create a temporary project to query SDK properties
TEMP_DIR=$(mktemp -d)
TEMP_PROJ="$TEMP_DIR/temp.csproj"

cat > "$TEMP_PROJ" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF

# Get NETCoreAppMaximumVersion from SDK
MAX_VERSION=$(dotnet msbuild "$TEMP_PROJ" -getProperty:NETCoreAppMaximumVersion 2>/dev/null | tail -1 | tr -d ' ')

# Clean up temp project
rm -rf "$TEMP_DIR"

# Extract major version (e.g., "10.0" -> "10")
MAX_MAJOR=$(echo "$MAX_VERSION" | cut -d. -f1)

# Determine minimum supported major version based on EOL dates
# EOL dates: 8=Nov2026, 9=May2026, 10=Nov2028, 11=May2028, 12=Nov2030, 13=May2030
# Policy: support all non-EOL versions when this SDK version is current
if [ "$MAX_MAJOR" -eq 9 ] || [ "$MAX_MAJOR" -eq 10 ]; then
    MIN_MAJOR=8
elif [ "$MAX_MAJOR" -eq 11 ] || [ "$MAX_MAJOR" -eq 12 ]; then
    MIN_MAJOR=10
elif [ "$MAX_MAJOR" -eq 13 ] || [ "$MAX_MAJOR" -eq 14 ]; then
    MIN_MAJOR=10
else
    MIN_MAJOR=$MAX_MAJOR
fi

# Build list of supported frameworks for library (multi-target)
LIB_FRAMEWORKS=""
for v in $(seq $MIN_MAJOR $MAX_MAJOR); do
    if [ -n "$LIB_FRAMEWORKS" ]; then
        LIB_FRAMEWORKS="${LIB_FRAMEWORKS};net${v}.0"
    else
        LIB_FRAMEWORKS="net${v}.0"
    fi
done

CHANGES_MADE=false
PROPS_FILES=""

# Find all csproj files (excluding source generators which stay on netstandard2.0)
for CSPROJ in $(find . -name "*.csproj" -not -path "./build-standards/*" | sort); do
    DIR=$(dirname "$CSPROJ")
    PROJ_NAME=$(basename "$DIR")
    PROPS_FILE="$DIR/Directory.Build.props"

    # Skip source generator projects (they must target netstandard2.0)
    if grep -q "netstandard2.0" "$CSPROJ" 2>/dev/null; then
        continue
    fi

    # Determine project type
    IS_TEST=false
    IS_EXE=false

    # Check if it's a test project (name ends with Tests or Test)
    if [[ "$PROJ_NAME" == *Tests ]] || [[ "$PROJ_NAME" == *Test ]]; then
        IS_TEST=true
    fi

    # Check if it's an executable
    if grep -qE "<OutputType>Exe</OutputType>" "$CSPROJ" 2>/dev/null; then
        IS_EXE=true
    fi

    # Generate appropriate Directory.Build.props
    TEMP_PROPS=$(mktemp)

    if [ "$IS_TEST" = true ]; then
        cat > "$TEMP_PROPS" << EOF
<Project>
  <!-- Import parent props -->
  <Import Project="\$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '\$(MSBuildThisFileDirectory)../'))" />

  <!-- Test projects target only the latest .NET version -->
  <!-- Auto-generated based on SDK version by scripts/generate-build-props.sh -->
  <PropertyGroup>
    <TargetFramework>net${MAX_MAJOR}.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF
    elif [ "$IS_EXE" = true ]; then
        cat > "$TEMP_PROPS" << EOF
<Project>
  <!-- Import parent props -->
  <Import Project="\$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '\$(MSBuildThisFileDirectory)../'))" />

  <!-- Executable projects target only the latest .NET version -->
  <!-- Auto-generated based on SDK version by scripts/generate-build-props.sh -->
  <PropertyGroup>
    <TargetFramework>net${MAX_MAJOR}.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF
    else
        cat > "$TEMP_PROPS" << EOF
<Project>
  <!-- Import parent props -->
  <Import Project="\$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '\$(MSBuildThisFileDirectory)../'))" />

  <!-- Library projects multi-target all non-EOL .NET versions -->
  <!-- Auto-generated based on SDK version by scripts/generate-build-props.sh -->
  <PropertyGroup>
    <TargetFrameworks>$LIB_FRAMEWORKS</TargetFrameworks>
  </PropertyGroup>
</Project>
EOF
    fi

    if ! diff -q "$PROPS_FILE" "$TEMP_PROPS" > /dev/null 2>&1; then
        echo "$PROPS_FILE needs updating for current .NET SDK version"
        mv "$TEMP_PROPS" "$PROPS_FILE"
        CHANGES_MADE=true
        PROPS_FILES="$PROPS_FILES $PROPS_FILE"
    else
        rm -f "$TEMP_PROPS"
    fi
done

# If changes were made and we're in CI, commit and push
if [ "$CHANGES_MADE" = true ]; then
    if [ -d .git ] && [ -n "$CI" ]; then
        git config user.name "github-actions[bot]"
        git config user.email "github-actions[bot]@users.noreply.github.com"
        git add $PROPS_FILES
        git commit -m "Update Directory.Build.props files for .NET SDK version"
        git push
        echo "ERROR: Directory.Build.props files were out of date and have been updated."
        echo "The changes have been committed and pushed. Please retry the workflow."
        exit 1
    else
        echo "Updated props files locally. Please commit the changes."
        exit 0
    fi
else
    echo "All Directory.Build.props files are up to date"
    exit 0
fi
