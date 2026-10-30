#!/usr/bin/env bash
set -euo pipefail
root="$(pwd)"
source_root="$root/artifacts/guarded-engine/source"
build_root="$root/artifacts/guarded-engine/build"
stage="$root/artifacts/guarded-engine/package"
pinned_revision="$(python -c 'import json;print(json.load(open("Tools/AccountSafety/pins.json"))["revision"])')"
mkdir -p "$source_root" "$stage/LICENSES"
git -C "$source_root" init
git -C "$source_root" remote add origin https://github.com/Sorvigolova/ipatool.git
git -C "$source_root" fetch --depth 1 origin "$pinned_revision"
git -C "$source_root" -c core.autocrlf=false checkout --detach FETCH_HEAD
test "$(git -C "$source_root" rev-parse HEAD)" = "$pinned_revision"
python Tools/AccountSafety/patch.py "$source_root"
python Tools/AccountSafety/test_auth.py "$source_root"
cmake -S "$source_root" -B "$build_root" -G Ninja -DCMAKE_BUILD_TYPE=Release -DSTATIC_BUILD=ON
cmake --build "$build_root" --parallel 2
cp "$build_root/ipatool.exe" "$stage/ipatool.exe"
cp "$source_root/LICENSE" "$stage/LICENSES/ipatool-MIT.txt"
pacman -Q > "$stage/msys2-packages.txt"
cp -r /mingw64/share/licenses "$stage/LICENSES/msys2"
find "$build_root" -type f \( -name LICENSE -o -name COPYING -o -name LICENSE.txt -o -name LICENSE.md \) -print0 | while IFS= read -r -d '' file; do
  relative="${file#"$build_root"/}"
  mkdir -p "$stage/LICENSES/build/$(dirname "$relative")"
  cp "$file" "$stage/LICENSES/build/$relative"
done
# Source distribution includes the reviewed patch and all embedded upstream SAP assets.
mkdir -p "$root/artifacts/guarded-engine/source-release/AccountSafety"
cp -r Tools/AccountSafety/. "$root/artifacts/guarded-engine/source-release/AccountSafety/"
cp -r "$source_root" "$root/artifacts/guarded-engine/source-release/ipatool"
rm -rf "$root/artifacts/guarded-engine/source-release/ipatool/.git"
python Tools/AccountSafety/package.py "$stage" "$root/artifacts/guarded-engine/source-release"
