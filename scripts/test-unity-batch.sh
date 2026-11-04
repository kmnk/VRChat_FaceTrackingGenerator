#!/usr/bin/env bash
# Checks that unity-batch.sh and unity-gui.sh stop before starting Unity on main or master
# (unless the project has .ftg-dev-project) and that unity-batch.sh stops while the editor is open.
# Does not start Unity: UNITY points to a stub that records whether it was called.
set -u
here=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

proj="$tmp/proj"
mkdir -p "$proj/ProjectSettings"
git -C "$proj" init -q -b main
git -C "$proj" -c user.name=t -c user.email=t@example.com commit -q --allow-empty -m init

stub="$tmp/unity-stub"
printf '#!/bin/sh\ntouch "%s/called"\nexit 0\n' "$tmp" > "$stub"
chmod +x "$stub"

fail=0
check() { # name expected_exit actual_exit expect_called
  local called=no
  [ -e "$tmp/called" ] && called=yes
  if [ "$2" = "$3" ] && [ "$4" = "$called" ]; then
    echo "ok   $1 (exit=$3 unity_called=$called)"
  else
    echo "FAIL $1 (exit=$3 want=$2 unity_called=$called want=$4)"
    fail=1
  fi
  rm -f "$tmp/called"
}

UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-batch.sh" "$proj" >/dev/null 2>&1
check "stops on main" 3 $? no

UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-gui.sh" "$proj" Some.Method >/dev/null 2>&1
check "gui: stops on main" 3 $? no

# A project used only for developing the generator marks itself with .ftg-dev-project
# and may use main.
touch "$proj/.ftg-dev-project"
UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-batch.sh" "$proj" >/dev/null 2>&1
check "runs on main with .ftg-dev-project" 0 $? yes
UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-gui.sh" "$proj" Some.Method >/dev/null 2>&1
check "gui: runs on main with .ftg-dev-project" 0 $? yes
rm "$proj/.ftg-dev-project"

git -C "$proj" branch -q -m main master
UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-batch.sh" "$proj" >/dev/null 2>&1
check "stops on master" 3 $? no
UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-gui.sh" "$proj" Some.Method >/dev/null 2>&1
check "gui: stops on master" 3 $? no
git -C "$proj" branch -q -m master main

git -C "$proj" switch -q -c work
mkdir -p "$proj/Temp" && touch "$proj/Temp/UnityLockfile"
# A process holds the lock file, like the editor does.
tail -f "$proj/Temp/UnityLockfile" >/dev/null 2>&1 &
holder=$!
sleep 1
UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-batch.sh" "$proj" >/dev/null 2>&1
check "stops when editor is open" 4 $? no
kill $holder 2>/dev/null; wait $holder 2>/dev/null

UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-batch.sh" "$proj" >/dev/null 2>&1
check "removes a stale lock file and runs" 0 $? yes
[ -e "$proj/Temp/UnityLockfile" ] && { echo "FAIL stale lock file is still there"; fail=1; }

UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-batch.sh" "$proj" >/dev/null 2>&1
check "runs on work branch" 0 $? yes

UNITY=$stub UNITY_BATCH_LOG_DIR=$tmp "$here/unity-gui.sh" "$proj" Some.Method >/dev/null 2>&1
check "gui: runs on work branch" 0 $? yes

exit $fail
