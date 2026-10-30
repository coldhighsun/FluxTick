#!/usr/bin/env bash
# Builds FluxTick.app from a published osx-arm64 build and packs it into a disk image. Runs on macOS only.
# Usage: build-app.sh <publish-dir> <version> <output-dmg>
#   publish-dir  directory holding the published single-file `FluxTick` executable
#   version      release version, with or without the leading "v", e.g. v1.2.0 or 1.2.0-beta.1
#   output-dmg   path of the disk image to create
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <publish-dir> <version> <output-dmg>" >&2
  exit 2
fi

publish_dir=$(cd "$1" && pwd)
version=${2#v}
numeric_version=${version%%-*}
output_dmg=$3

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

app="$work/dmg/FluxTick.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp "$publish_dir/FluxTick" "$app/Contents/MacOS/FluxTick"
chmod +x "$app/Contents/MacOS/FluxTick"

sed -e "s/__VERSION__/$version/" -e "s/__NUMERIC_VERSION__/$numeric_version/" \
  "$repo_root/installer/macos/Info.plist.template" > "$app/Contents/Info.plist"

# The icon source is a 256x256 png, so the iconset stops at 256x256 (and 128x128@2x).
iconset="$work/fluxtick.iconset"
png="$repo_root/src/FluxTick.App/Assets/fluxtick.png"
mkdir -p "$iconset"
sips -z 16 16 "$png" --out "$iconset/icon_16x16.png" > /dev/null
sips -z 32 32 "$png" --out "$iconset/icon_16x16@2x.png" > /dev/null
sips -z 32 32 "$png" --out "$iconset/icon_32x32.png" > /dev/null
sips -z 64 64 "$png" --out "$iconset/icon_32x32@2x.png" > /dev/null
sips -z 128 128 "$png" --out "$iconset/icon_128x128.png" > /dev/null
sips -z 256 256 "$png" --out "$iconset/icon_128x128@2x.png" > /dev/null
sips -z 256 256 "$png" --out "$iconset/icon_256x256.png" > /dev/null
iconutil -c icns "$iconset" -o "$app/Contents/Resources/fluxtick.icns"

# Apple Silicon refuses to run code without a signature; an ad-hoc one is enough for that. It is not a Developer ID
# signature, so Gatekeeper still asks the user to approve the first launch.
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

ln -s /Applications "$work/dmg/Applications"
rm -f "$output_dmg"
hdiutil create -volname FluxTick -srcfolder "$work/dmg" -ov -format UDZO "$output_dmg"
