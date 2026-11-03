#!/bin/sh
# stage, installed directory and parent PID are passed as separate arguments.
set -eu
stage=$1
target=$2
parent=$3
case "$parent" in ''|*[!0-9]*) exit 64;; esac
[ -d "$stage" ] && [ -d "$target" ] && [ "$target" != / ] || exit 64
count=0
while [ "$count" -lt 60 ];do kill -0 "$parent" 2>/dev/null || break;sleep 1;count=$((count+1));done
if kill -0 "$parent" 2>/dev/null;then exit 75;fi
backup="$target.backup-$(date +%s)-$$"
new="$target.update-$$"
cp -R "$stage" "$new"
if ! mv "$target" "$backup";then rm -rf "$new";exit 1;fi
if ! mv "$new" "$target";then mv "$backup" "$target";exit 1;fi
if [ -d "$target/Contents/MacOS" ];then
 /usr/bin/open -n "$target"
else
 "$target/IPA-Desktop" >/dev/null 2>&1 &
fi
# Keep one rollback copy beside the app; user data lives outside the installation.
