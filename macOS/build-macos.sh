#!/usr/bin/env bash
set -euo pipefail

# Run on macOS with .NET SDK 10 and Xcode Command Line Tools installed.
# bash macOS/build-macos.sh [osx-arm64|osx-x64|all] [output-directory]
task_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
task_project_dir="$(cd -- "$task_script_dir/.." && pwd)"
task_runtime="${1:-all}"
task_output="${2:-$task_project_dir/artifacts/macOS}"
case "$task_runtime" in
  osx-arm64|osx-x64) task_runtimes=("$task_runtime") ;;
  all) task_runtimes=(osx-arm64 osx-x64) ;;
  *) printf 'Unknown runtime: %s\n' "$task_runtime" >&2; exit 2 ;;
esac
if [[ "$(uname -s)" != Darwin ]]; then
  printf 'Packaging requires macOS (codesign, plutil and ditto).\n' >&2
  exit 2
fi
for task_tool in dotnet codesign plutil ditto file; do
  command -v "$task_tool" >/dev/null || { printf 'Missing tool: %s\n' "$task_tool" >&2; exit 2; }
done
task_sdk="$(dotnet --version)"
if [[ "${task_sdk%%.*}" -lt 10 ]]; then
  printf '.NET SDK 10 or newer is required.\n' >&2
  exit 2
fi
mkdir -p -- "$task_output"
task_output="$(cd -- "$task_output" && pwd)"
task_version="$(dotnet msbuild "$task_script_dir/Kitsu.Mac.csproj" -nologo -getProperty:Version)"
if [[ ! "$task_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  printf 'Invalid app version: %s\n' "$task_version" >&2
  exit 2
fi
task_entitlements="$task_script_dir/entitlements.plist"

for task_rid in "${task_runtimes[@]}"; do
  # A new staging directory avoids stale files from a previous release.
  task_stage="$(mktemp -d "$task_output/.kitsu-$task_rid.XXXXXX")"
  task_publish="$task_stage/publish"
  task_package="$task_stage/package"
  task_bundle="$task_package/Kitsu.app"
  mkdir -p -- "$task_bundle/Contents/MacOS" "$task_bundle/Contents/Resources"
  dotnet publish "$task_script_dir/Kitsu.Mac.csproj" \
    --configuration Release --runtime "$task_rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:UseAppHost=true -p:PublishTrimmed=false -p:DebugType=none \
    -p:DebugSymbols=false --output "$task_publish"
  ditto "$task_publish" "$task_bundle/Contents/MacOS"
  test -f "$task_bundle/Contents/MacOS/Kitsu"
  # Managed PE assemblies are embedded in the apphost. Apple treats loose DLLs
  # under MacOS as unsigned nested code, so refuse an incorrectly laid-out build.
  task_loose_dll="$(find "$task_bundle/Contents/MacOS" -type f -iname '*.dll' -print -quit)"
  if [[ -n "$task_loose_dll" ]]; then
    printf 'Managed assembly must be embedded in the single-file apphost: %s\n' "$task_loose_dll" >&2
    exit 1
  fi
  chmod 755 "$task_bundle/Contents/MacOS/Kitsu"
  cat > "$task_bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.kales380.kitsu</string>
  <key>CFBundleName</key><string>Kitsu</string>
  <key>CFBundleDisplayName</key><string>Кицу</string>
  <key>CFBundleExecutable</key><string>Kitsu</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$task_version</string>
  <key>CFBundleVersion</key><string>$task_version</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict></plist>
PLIST
  plutil -lint "$task_bundle/Contents/Info.plist"
  # Sign nested native code first, then the apphost and the completed bundle.
  # An ad-hoc signature is integrity protection; it is not Developer ID notarization.
  while IFS= read -r -d '' task_binary; do
    if [[ "$task_binary" != "$task_bundle/Contents/MacOS/Kitsu" ]] && file -b "$task_binary" | grep -q 'Mach-O'; then
      codesign --force --sign - --timestamp=none "$task_binary"
    fi
  done < <(find "$task_bundle/Contents" -type f -print0)
  codesign --force --sign - --timestamp=none --entitlements "$task_entitlements" \
    "$task_bundle/Contents/MacOS/Kitsu"
  codesign --force --sign - --timestamp=none --entitlements "$task_entitlements" "$task_bundle"
  codesign --verify --deep --strict --verbose=2 "$task_bundle"
  task_expected_arch=x86_64
  [[ "$task_rid" == osx-arm64 ]] && task_expected_arch=arm64
  task_binary_info="$(file -b "$task_bundle/Contents/MacOS/Kitsu")"
  printf '%s\n' "$task_binary_info"
  [[ "$task_binary_info" == *"$task_expected_arch"* ]] || { printf 'Wrong executable architecture.\n' >&2; exit 1; }
  if [[ "$(uname -m)" == "$task_expected_arch" ]]; then
    "$task_bundle/Contents/MacOS/Kitsu" --self-test
    "$task_bundle/Contents/MacOS/Kitsu" --smoke-test
    # The smoke test must write its diagnostics outside the signed app.
    codesign --verify --deep --strict --verbose=2 "$task_bundle"
  else
    printf 'Runtime test skipped for non-native %s; use its native CI job.\n' "$task_rid"
  fi
  cp "$task_script_dir/START-HERE.txt" "$task_package/START-HERE.txt"
  cp "$task_script_dir/ПРОЧТИ-МЕНЯ.txt" "$task_package/ПРОЧТИ-МЕНЯ.txt"
  task_zip="$task_output/Kitsu-macos-${task_rid#osx-}.zip"
  # ditto preserves the executable mode, symlinks and macOS bundle layout inside ZIP.
  ditto -c -k --sequesterRsrc "$task_package" "$task_zip"
  (cd -- "$task_output" && shasum -a 256 "$(basename -- "$task_zip")" > "SHA256SUMS-$task_rid.txt")
  printf 'Ready: %s\n' "$task_zip"
done
