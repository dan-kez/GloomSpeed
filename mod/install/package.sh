#!/bin/bash
# Builds dist/GloomSpeed-<version>.zip: the only file players need.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=$(sed -n 's/.*Version = "\(.*\)";/\1/p' "$ROOT/mod/GloomSpeed/src/Plugin.cs")
NAME=GloomSpeed-$VERSION
STAGE=$(mktemp -d)
trap 'rm -rf "${STAGE:?}"' EXIT
mkdir -p "$STAGE/$NAME" "$ROOT/dist"
cp "$ROOT/mod/GloomSpeed/bin/Release/GloomSpeed.dll" "$STAGE/$NAME/"
cp "$ROOT"/mod/install/windows/* "$STAGE/$NAME/"
cp "$ROOT/mod/install/macos/macos.sh" "$STAGE/$NAME/"
cp "$ROOT/INSTALL.md" "$STAGE/$NAME/"
(cd "$STAGE" && python3 -I -m zipfile -c "$ROOT/dist/$NAME.zip" "$NAME")
echo "$ROOT/dist/$NAME.zip"
