#!/usr/bin/env bash
set -euo pipefail
root="$(pwd)"
source_root="$root/artifacts/guarded-engine/source"
build_root="$root/artifacts/guarded-engine/build"
stage="$root/artifacts/guarded-engine/package"
pin="Tools/AccountSafety/Windows/pin.json"
pin_value() { python -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' "$pin" "$1"; }
pinned_revision="$(pin_value revision)"
mkdir -p "$source_root" "$stage/LICENSES"
git -C "$source_root" init
git -C "$source_root" fetch --depth 1 "$(pin_value repository)" "$pinned_revision"
git -C "$source_root" -c core.autocrlf=false checkout --detach FETCH_HEAD
test "$(git -C "$source_root" rev-parse HEAD)" = "$pinned_revision"
python Tools/AccountSafety/patch.py "$source_root" "$pin"
python Tools/AccountSafety/test_auth.py "$source_root"
# The MSYS2 Minizip config can reference an unshipped minizipstatic.exe.
# Keep IPA metadata support by using the installed headers and static archive.
mingw_prefix="$(cygpath -m /mingw64)"
for file in include/minizip/zip.h include/minizip/unzip.h lib/libminizip.a; do
  if [[ ! -f "$mingw_prefix/$file" ]]; then
    printf 'Required static Minizip file missing: %s\n' "$file" >&2
    exit 1
  fi
done
cmake -S "$source_root" -B "$build_root" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DSTATIC_BUILD=ON \
  -DCMAKE_DISABLE_FIND_PACKAGE_minizip=ON \
  -DCMAKE_DISABLE_FIND_PACKAGE_unofficial-minizip=ON \
  -DMINIZIP_INCLUDE_DIR:PATH="$mingw_prefix/include" \
  -DMINIZIP_LIBRARY:FILEPATH="$mingw_prefix/lib/libminizip.a"
cmake --build "$build_root" --parallel 2
python Tools/AccountSafety/verify_cfg.py "$build_root" "$stage"
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
python Tools/AccountSafety/package.py "$stage" "$root/artifacts/guarded-engine/source-release" "$pin"
