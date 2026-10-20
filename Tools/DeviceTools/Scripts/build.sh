#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$root"
# Use available CPU cores with a conservative cap. A CI override must be a bounded integer.
jobs="${IPA_DEVICE_JOBS:-$(getconf _NPROCESSORS_ONLN)}"
case "$jobs" in ''|*[!0-9]*) echo 'IPA_DEVICE_JOBS must be an integer' >&2; exit 1;; esac
if (( jobs < 1 )); then echo 'IPA_DEVICE_JOBS must be positive' >&2; exit 1; fi
if (( jobs > 8 )); then jobs=8; fi
prefix="$root/device-build/prefix"
mkdir -p "$prefix" "$root/device-build/source" "$root/device-build/source-archives"
export PATH="$prefix/bin:$PATH"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
export CPPFLAGS="-I$prefix/include"
export LDFLAGS="-L$prefix/lib"
export CFLAGS="-O2 -D_WIN32_WINNT=0x0A00"
for project in libplist libimobiledevice-glue libusbmuxd libtatsu libimobiledevice ideviceinstaller; do
  revision=$(python -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' Tools/DeviceTools/pins.json "$project")
  # Exact source snapshots do not need the upstream commit history.
  git init "device-build/source/$project"
  cd "device-build/source/$project"
  git remote add origin "https://github.com/libimobiledevice/$project.git"
  git fetch --depth=1 origin "$revision"
  git checkout --detach FETCH_HEAD
  test "$(git rev-parse HEAD)" = "$revision"
  git archive --format=tar --prefix="$project/" -o "$root/device-build/source-archives/$project.tar" HEAD
  NOCONFIGURE=1 ./autogen.sh
  options=(--prefix="$prefix" --enable-shared --disable-static)
  case "$project" in
    libplist) options+=(--without-cython --without-tests --without-tools);;
    libimobiledevice) options+=(--without-cython --without-readline --with-openssl);;
  esac
  ./configure "${options[@]}"
  make -j"$jobs"
  make install
  cd "$root"
done
pacman -Q > device-build/msys2-packages.txt
python Tools/DeviceTools/Scripts/package.py
