#!/usr/bin/env bash
# Checks saving from the table editor (TableEditorChecks.Edit) on copies of a set of tables.
#
# Usage:
#   scripts/test-table-save.sh <project> <avatar.jsonc> <work dir> [<edits.jsonl>]
#
#   <project>       Unity project with the generator (development copy)
#   <avatar.jsonc>  The avatar table of the set to check. The three tables are copied; the originals are not written
#   <work dir>      An empty or new directory outside the project (Unity deletes the project's Temp/ when it opens)
#   <edits.jsonl>   Optional edits (TableEdits.Apply operations) for the third step
#
# Steps and checks:
#   1. Save all three tables unchanged. Cli.Load output equals the output for the original tables
#   2. Save again. The files do not change (cmp)
#   3. With <edits.jsonl>: apply the edits and save. Prints the Cli.Load difference and `git diff --no-index`
#      against step 2, for the caller to compare with what the edits should change
#
# Output: "test-table-save: <check> pass|FAIL" lines, and exit code 0 when all pass, 1 otherwise, 2 on errors.
set -u
if [ $# -lt 3 ]; then
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
  exit 2
fi
here=$(cd "$(dirname "$0")" && pwd)
project=$1; table=$2; work=$3; edits=${4:-}
case "$table" in /*) ;; *) table="$project/$table" ;; esac
src=$(dirname "$table")
mkdir -p "$work/orig" "$work/tables" "$work/out"
export UNITY_BATCH_LOG_DIR="$work/logs"
cp "$src"/*.jsonc "$work/orig/"
cp "$src"/*.jsonc "$work/tables/"
t="$work/tables/$(basename "$table")"
o="$work/orig/$(basename "$table")"
fail=0

run() {  # run <label> <method> args...
  local label=$1; shift
  "$here/unity-batch.sh" "$project" "$@" > "$work/out/$label.txt" 2>&1
  local code=$?
  grep -E '^\[FTG\]' "$work/out/$label.txt" | sed "s/^/  $label: /"
  if [ $code -ne 0 ]; then echo "test-table-save: $label exited $code (see $work/out/$label.txt)"; exit 2; fi
}
check() {  # check <name> <command...>
  local name=$1; shift
  if "$@" > /dev/null 2>&1; then echo "test-table-save: $name pass"; else echo "test-table-save: $name FAIL"; fail=1; fi
}

run load-orig Kmnk.FaceTrackingGenerator.Cli.Load -ftTable "$o" -ftOut "$work/out/orig.load.json"
run save1 Kmnk.FaceTrackingGenerator.TableEditorChecks.Edit -ftTable "$t" -ftAll 1
run load1 Kmnk.FaceTrackingGenerator.Cli.Load -ftTable "$t" -ftOut "$work/out/save1.load.json"
# The model records the table path; compare without it.
strip() { grep -v '"avatarTablePath"' "$1"; }
check "1 load after saving unchanged equals original" diff <(strip "$work/out/orig.load.json") <(strip "$work/out/save1.load.json")
mkdir -p "$work/save1"; cp "$work/tables"/*.jsonc "$work/save1/"
run save2 Kmnk.FaceTrackingGenerator.TableEditorChecks.Edit -ftTable "$t" -ftAll 1
for f in "$work/save1"/*.jsonc; do
  check "2 second save leaves $(basename "$f") unchanged" cmp "$f" "$work/tables/$(basename "$f")"
done

if [ -n "$edits" ]; then
  run edit Kmnk.FaceTrackingGenerator.TableEditorChecks.Edit -ftTable "$t" -ftEdits "$edits"
  run load2 Kmnk.FaceTrackingGenerator.Cli.Load -ftTable "$t" -ftOut "$work/out/edit.load.json"
  echo "--- 3 Cli.Load difference after the edits"
  diff <(strip "$work/out/save1.load.json") <(strip "$work/out/edit.load.json")
  echo "--- 3 git diff --no-index after the edits"
  for f in "$work/save1"/*.jsonc; do git diff --no-index --no-color -U0 "$f" "$work/tables/$(basename "$f")"; done
fi
echo "--- git status --short of the project"
git -C "$project" status --short
exit $fail
