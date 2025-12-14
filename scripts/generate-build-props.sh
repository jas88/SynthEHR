#!/bin/bash
# Update Directory.Build.props with dynamic target framework values based on .NET SDK version.
# Uses markers in the root file to update the frameworks section:
# - Libraries: multi-target all non-EOL versions
# - Tests (*Tests), Benchmarks (*Benchmark), and CLI (SynthEHR): latest only
# Note: Uses name patterns because OutputType isn't available in .props
#
# If the file differs from what's in git, commit and push (in CI), then exit with error.

set -e

PROPS_FILE="Directory.Build.props"

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

# Generate the new frameworks section (uses name patterns since OutputType isn't available in .props)
NEW_SECTION="  <!-- @FRAMEWORKS_START@ -->
  <PropertyGroup Condition=\"!\$(MSBuildProjectName.Contains('SourceGenerator')) AND !\$(MSBuildProjectName.EndsWith('Tests')) AND '\$(MSBuildProjectName)' != 'SynthEHR' AND !\$(MSBuildProjectName.Contains('Benchmark'))\">
    <TargetFrameworks>$LIB_FRAMEWORKS</TargetFrameworks>
  </PropertyGroup>
  <PropertyGroup Condition=\"!\$(MSBuildProjectName.Contains('SourceGenerator')) AND (\$(MSBuildProjectName.EndsWith('Tests')) OR '\$(MSBuildProjectName)' == 'SynthEHR' OR \$(MSBuildProjectName.Contains('Benchmark')))\">
    <TargetFramework>net${MAX_MAJOR}.0</TargetFramework>
  </PropertyGroup>
  <!-- @FRAMEWORKS_END@ -->"

# Extract current section from file
CURRENT_SECTION=$(sed -n '/@FRAMEWORKS_START@/,/@FRAMEWORKS_END@/p' "$PROPS_FILE")

# Compare sections
if [ "$CURRENT_SECTION" = "$NEW_SECTION" ]; then
    echo "Directory.Build.props is up to date"
    exit 0
fi

echo "Directory.Build.props needs updating for current .NET SDK version"

# Create temp file with updated content
TEMP_FILE=$(mktemp)

# Use awk to replace the section between markers
awk -v new="$NEW_SECTION" '
    /@FRAMEWORKS_START@/ { printing=1; print new; next }
    /@FRAMEWORKS_END@/ { printing=0; next }
    !printing { print }
' "$PROPS_FILE" > "$TEMP_FILE"

mv "$TEMP_FILE" "$PROPS_FILE"

# If we're in CI, commit and push
if [ -d .git ] && [ -n "$CI" ]; then
    git config user.name "github-actions[bot]"
    git config user.email "github-actions[bot]@users.noreply.github.com"
    git add "$PROPS_FILE"
    git commit -m "Update Directory.Build.props for .NET SDK version"
    git push
    echo "ERROR: Directory.Build.props was out of date and has been updated."
    echo "The changes have been committed and pushed. Please retry the workflow."
    exit 1
else
    echo "Updated $PROPS_FILE locally. Please commit the changes."
    exit 0
fi
