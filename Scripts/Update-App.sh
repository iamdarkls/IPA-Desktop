#!/bin/sh
# stage, installed directory and parent PID are passed as separate arguments.
set -eu
stage=$1
target=$2
parent=$3
case "$parent" in ''|*[!0-9]*) exit 64;; esac
[ -d "$stage" ] && [ -d "$target" ] && [ "$target" != / ] || exit 64

restart_app() {
 if [ -d "$target/Contents/MacOS" ]; then
  /usr/bin/open -n "$target"
 else
  "$target/IPA-Desktop" >/dev/null 2>&1 &
 fi
}

count=0
while [ "$count" -lt 60 ];do kill -0 "$parent" 2>/dev/null || break;sleep 1;count=$((count+1));done
if kill -0 "$parent" 2>/dev/null;then exit 75;fi
backup="$target.backup-$(date +%s)-$$"
new="$target.update-$$"
trap 'rm -rf "$new"' EXIT

if [ "$(uname -s)" = Darwin ];then
 /usr/bin/ditto "$stage" "$new"
else
 cp -R "$stage" "$new"
fi

if ! mv "$target" "$backup"; then
 restart_app
 exit 1
fi
if ! mv "$new" "$target"; then
 # Only relaunch after the previous bundle is back in place.
 mv "$backup" "$target"
 restart_app
 exit 1
fi

# Keep the rollback copy beside the app; user data lives outside the installation.
restart_app
