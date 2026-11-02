#!/usr/bin/env bash
# Run a Unity project in the editor with graphics (not batchmode), for checks that draw windows.
#
# Usage:
#   scripts/unity-gui.sh <project> <executeMethod> [args...]
#
#   <project>        Unity project path (e.g. ~/work/MyAvatarProject)
#   <executeMethod>  Static method to call. It must end Unity with EditorApplication.Exit
#                    (Kmnk.FaceTrackingGenerator.TableEditorChecks.Capture does).
#
# The editor opens on the screen while the method runs, and closes when it calls Exit.
#
# Safety (the same stops as scripts/unity-batch.sh):
#   - Stops without starting Unity when the project's current git branch is main or master.
#   - Stops when a process holds Temp/UnityLockfile (the project is open in Unity).
#   - Stops Unity after UNITY_GUI_TIMEOUT seconds (default 300) if the method never exits.
#   - Never uploads. It only runs Editor methods.
#
# Output: the log path, the exit code, lines starting with [FTG], errors in the log,
# and `git status --short` of the project after the run.
#
# Environment:
#   UNITY              Unity executable (default: Unity 2022.3.22f1 from Unity Hub on macOS)
#   UNITY_BATCH_LOG_DIR Directory for logs (default: $TMPDIR or /tmp)
#   UNITY_GUI_TIMEOUT  Seconds before Unity is stopped (default 300)
#
# Unity needs to run outside the Claude Code sandbox (license files under ~/Library/Unity).
set -u
if [ $# -lt 2 ] || [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
  exit 2
fi

project=$1
method=$2
shift 2

if [ ! -d "$project/ProjectSettings" ]; then
  echo "unity-gui: not a Unity project (no ProjectSettings/): $project" >&2
  exit 2
fi

branch=$(git -C "$project" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
if [ "$branch" = "main" ] || [ "$branch" = "master" ]; then
  echo "unity-gui: stopped. $project is on branch '$branch'." >&2
  exit 3
fi

lock="$project/Temp/UnityLockfile"
if [ -e "$lock" ]; then
  if [ -n "$(lsof -t "$lock" 2>/dev/null)" ]; then
    echo "unity-gui: stopped. $project is open in Unity (a process holds Temp/UnityLockfile)." >&2
    exit 4
  fi
  echo "unity-gui: removing a stale Temp/UnityLockfile (no process holds it)"
  rm -f "$lock"
fi

unity=${UNITY:-/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity}
if [ ! -x "$unity" ]; then
  echo "unity-gui: Unity not found: $unity (set UNITY)" >&2
  exit 2
fi

log_dir=${UNITY_BATCH_LOG_DIR:-${TMPDIR:-/tmp}}
mkdir -p "$log_dir"
log="$log_dir/unity-gui-$(basename "$project")-$(date +%Y%m%d-%H%M%S).log"
timeout=${UNITY_GUI_TIMEOUT:-300}

echo "unity-gui: project=$project branch=$branch method=$method"
echo "unity-gui: log=$log"
"$unity" -projectPath "$project" -logFile "$log" -executeMethod "$method" "$@" &
pid=$!
waited=0
while kill -0 "$pid" 2>/dev/null; do
  if [ "$waited" -ge "$timeout" ]; then
    echo "unity-gui: stopping Unity after ${timeout}s (the method did not exit)"
    kill "$pid" 2>/dev/null
    sleep 5
    kill -9 "$pid" 2>/dev/null
    break
  fi
  sleep 1
  waited=$((waited + 1))
done
wait "$pid"
code=$?

echo "unity-gui: exit=$code"
echo "--- [FTG] lines"
grep '^\[FTG\]' "$log" || echo "(none)"
echo "--- errors"
grep -E 'error CS[0-9]+|^[A-Za-z.]*Exception:|executeMethod method .* threw' "$log" | sort -u | head -40 || true
echo "--- git status --short (after run)"
git -C "$project" status --short
exit $code
