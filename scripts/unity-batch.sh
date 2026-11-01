#!/usr/bin/env bash
# Run a Unity project in batchmode for checking changes to the generator.
#
# Usage:
#   scripts/unity-batch.sh [--no-quit] <project> [<executeMethod> [args...]]
#
#   <project>        Unity project path (e.g. ~/work/MyAvatarProject)
#   <executeMethod>  Static method to call. Without it, the project is opened and closed.
#   --no-quit        Do not pass -quit. Use for methods that enter Play Mode and call
#                    EditorApplication.Exit themselves (ScenarioRunner).
#
# Safety:
#   - Stops without starting Unity when the project's current git branch is main.
#     The avatar projects are uploaded from main on Windows, so checks run on a work branch.
#   - Stops when a process holds Temp/UnityLockfile (the project is open in the editor).
#     A lock file that no process holds is left over from a crash and is removed.
#   - Never uploads. It only runs Editor methods.
#
# Output: the log path, the exit code, lines starting with [FTG], errors in the log,
# and `git status --short` of the project after the run (files the run wrote).
#
# Environment:
#   UNITY                Unity executable (default: Unity 2022.3.22f1 from Unity Hub on macOS)
#   UNITY_BATCH_LOG_DIR  Directory for logs (default: $TMPDIR or /tmp)
#   UNITY_BATCH_GRAPHICS Set to 1 to keep the graphics device (for methods that render images)
#
# Unity needs to run outside the Claude Code sandbox (license files under ~/Library/Unity).

set -u

no_quit=0
if [ "${1:-}" = "--no-quit" ]; then
  no_quit=1
  shift
fi

if [ $# -lt 1 ] || [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  sed -n '2,29p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
fi

project=$1
shift
method=${1:-}
[ $# -gt 0 ] && shift

if [ ! -d "$project/ProjectSettings" ]; then
  echo "unity-batch: not a Unity project (no ProjectSettings/): $project" >&2
  exit 2
fi

branch=$(git -C "$project" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
if [ "$branch" = "main" ] || [ "$branch" = "master" ]; then
  echo "unity-batch: stopped. $project is on branch '$branch'." >&2
  echo "unity-batch: switch to a work branch first (e.g. git -C $project switch ft-generator)." >&2
  exit 3
fi

lock="$project/Temp/UnityLockfile"
if [ -e "$lock" ]; then
  if [ -n "$(lsof -t "$lock" 2>/dev/null)" ]; then
    echo "unity-batch: stopped. $project is open in Unity (a process holds Temp/UnityLockfile)." >&2
    echo "unity-batch: close the editor and run again." >&2
    exit 4
  fi
  # Left behind by a Unity that exited without cleaning up (no process holds it).
  echo "unity-batch: removing a stale Temp/UnityLockfile (no process holds it)"
  rm -f "$lock"
fi

unity=${UNITY:-/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity}
if [ ! -x "$unity" ]; then
  echo "unity-batch: Unity not found: $unity (set UNITY)" >&2
  exit 2
fi

log_dir=${UNITY_BATCH_LOG_DIR:-${TMPDIR:-/tmp}}
mkdir -p "$log_dir"
name=$(basename "$project")
log="$log_dir/unity-batch-$name-$(date +%Y%m%d-%H%M%S).log"

args=(-batchmode -projectPath "$project" -logFile "$log")
[ "${UNITY_BATCH_GRAPHICS:-0}" = "1" ] || args+=(-nographics)
[ $no_quit -eq 0 ] && args+=(-quit)
[ -n "$method" ] && args+=(-executeMethod "$method")

echo "unity-batch: project=$project branch=$branch method=${method:-<none>}"
echo "unity-batch: log=$log"
"$unity" "${args[@]}" "$@"
code=$?

echo "unity-batch: exit=$code"
echo "--- [FTG] lines"
grep '^\[FTG\]' "$log" || echo "(none)"
echo "--- errors"
grep -E 'error CS[0-9]+|^[A-Za-z.]*Exception:|executeMethod method .* threw|Aborting batchmode' "$log" | sort -u | head -40 || true
echo "--- git status --short (after run)"
git -C "$project" status --short
exit $code
