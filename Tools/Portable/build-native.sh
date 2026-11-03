#!/usr/bin/env bash
set -euo pipefail
root="$PWD"
case "$(uname -s)-$(uname -m)" in
 Linux-x86_64) rid=linux-x64;; Darwin-arm64) rid=osx-arm64;; Darwin-x86_64) rid=osx-x64;; *) echo 'Unsupported native target' >&2;exit 1;;
esac
prefix="$root/artifacts/unix/prefix"
stage="$root/artifacts/unix/native"
mkdir -p "$prefix" "$stage" "$root/artifacts/unix/source-archives"
export PATH="$prefix/bin:$PATH"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
export CPPFLAGS="-I$prefix/include"
export LDFLAGS="-L$prefix/lib"
export CFLAGS="-O2"
# Build the same immutable device sources and placeholder patch as Windows.
for project in libplist libimobiledevice-glue libusbmuxd libtatsu libimobiledevice ideviceinstaller; do
 revision=$(python3 -c 'import json,sys;print(json.load(open("Tools/DeviceTools/pins.json"))[sys.argv[1]])' "$project")
 source="$root/artifacts/unix/source/$project"
 git init "$source"
 git -C "$source" remote add origin "https://github.com/libimobiledevice/$project.git"
 git -C "$source" fetch --depth=1 origin "$revision"
 git -C "$source" -c core.autocrlf=false checkout --detach FETCH_HEAD
 git -C "$source" archive --format=tar --prefix="$project/" -o "$root/artifacts/unix/source-archives/$project.tar" HEAD
 if [[ "$project" == ideviceinstaller ]];then python3 Tools/DeviceTools/Scripts/patch_installer.py "$source" "$root/artifacts/unix/source-archives/ideviceinstaller.tar";fi
 options=(--prefix="$prefix" --enable-shared --disable-static)
 case "$project" in libplist) options+=(--without-cython --without-tests --without-tools);;libimobiledevice) options+=(--without-cython --without-readline --with-openssl);;esac
 (cd "$source";NOCONFIGURE=1 ./autogen.sh;./configure "${options[@]}";make -j4;make install)
done
# Guarded ipatool: identical credential/retry restrictions on every OS.
source="$root/artifacts/unix/auth-source"
revision=$(python3 -c 'import json;print(json.load(open("Tools/AccountSafety/pins.json"))["revision"])')
git init "$source";git -C "$source" remote add origin https://github.com/Sorvigolova/ipatool.git
git -C "$source" fetch --depth=1 origin "$revision"
git -C "$source" -c core.autocrlf=false checkout --detach FETCH_HEAD
python3 Tools/AccountSafety/patch.py "$source"
python3 Tools/AccountSafety/test_auth.py "$source"
options=(-DCMAKE_BUILD_TYPE=Release -DSTATIC_BUILD=ON)
if [[ "$rid" == osx-* ]];then options+=(-DCMAKE_OSX_DEPLOYMENT_TARGET=13.0 -DCMAKE_OSX_ARCHITECTURES="$(uname -m)");fi
cmake -S "$source" -B artifacts/unix/auth-build -G 'Unix Makefiles' "${options[@]}"
cmake --build artifacts/unix/auth-build --parallel 4
cp artifacts/unix/auth-build/ipatool "$stage/ipatool"
for name in idevice_id ideviceinfo ideviceinstaller idevicepair;do cp "$prefix/bin/$name" "$stage/$name";done
if [[ "$rid" == linux-* ]];then
 cc -O2 -Wall -Wextra Tools/Portable/ipa-pty.c -o "$stage/ipa-pty" -lutil
 python3 Tools/Portable/bundle-libraries.py "$stage"
else
 cc -O2 -Wall -Wextra Tools/Portable/ipa-pty.c -o "$stage/ipa-pty"
 dylibbundler -of -b -x "$stage/idevice_id" -x "$stage/ideviceinfo" -x "$stage/ideviceinstaller" -x "$stage/idevicepair" -d "$stage" -p @executable_path/
 for file in "$stage"/*;do codesign --force --sign - "$file";done
fi
for name in idevice_id ideviceinfo ideviceinstaller idevicepair;do "$stage/$name" --version;done
"$stage/ideviceinstaller" --help | grep -- --placeholders
test "$("$stage/ipatool" account-safety-info)" = IPA-DESKTOP-AUTH-SAFETY/1
# Keep exact patched source, build recipes and licenses with the release.
cp "$source/LICENSE" "$stage/ipatool-LICENSE.txt"
cp Tools/DeviceTools/pins.json "$stage/sources.json"
for project in libplist libimobiledevice-glue libusbmuxd libtatsu libimobiledevice ideviceinstaller;do
 for license in COPYING COPYING.LESSER LICENSE;do
  file="$root/artifacts/unix/source/$project/$license"
  if [[ -f "$file" ]];then cp "$file" "$stage/$project-$license.txt";fi
 done
done
python3 Tools/Portable/source-archive.py "$rid"
