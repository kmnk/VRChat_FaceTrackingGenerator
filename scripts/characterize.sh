#!/usr/bin/env bash
# Record what the generator does for a set of cases, to compare before and after a change with diff -r.
#
# Usage:
#   scripts/characterize.sh <project> <cases file> <out dir>
#
#   <project>     Unity project with the generator and its checks package (dev.kmnk.face-tracking-generator.checks)
#   <cases file>  One case per line: <id> <avatar table> [<scenario> <avatar prefab> <mesh> <ndmf true|false>]
#                 (paths relative to the project; # starts a comment)
#   <out dir>     Written per case: <id>/load.json, load.log, generate.log, output/, check.log, queries.jsonl,
#                 curve-vr.jsonl, curve-desktop.jsonl, and with a scenario scenario.jsonl (and build/ with ndmf true:
#                 the output folder's Build/ and Menus/ after the build-time generation)
#
# Runs Kmnk.FaceTrackingGenerator.Characterize.Run once for all cases (Cli.Load, Generate, Check and Curve as from the
# command line), then ScenarioRunner.Run once per scenario. The output folders are kept between runs, so running this
# twice also checks that generating again changes nothing.
#
# Compare: diff -r <out dir of the baseline> <out dir>. Exit 0 when every Unity run exited 0.
# Unity needs to run outside the Claude Code sandbox (see AGENTS.md).
set -u
if [ $# -lt 3 ]; then
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
  exit 2
fi
project=$1
cases=$2
out=$3
here=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$out"
out=$(cd "$out" && pwd)
case "$cases" in /*) ;; *) cases=$(cd "$(dirname "$cases")" && pwd)/$(basename "$cases") ;; esac
ipc=()
[ -n "${UNITY_LICENSING_IPC:-}" ] && ipc=(-licensingIpc "$UNITY_LICENSING_IPC")
status=0

run() {
  local log
  log=$("$here/unity-batch.sh" "$@" 2>&1)
  local code=$?
  echo "$log" | grep -E '^unity-batch: (exit|stopped)|^\[FTG\] (characterize|scenario: wrote|error)'
  [ $code -eq 0 ] || { echo "$log" | sed -n '/^--- errors/,/^--- git/p'; status=1; }
}

run "$project" Kmnk.FaceTrackingGenerator.Characterize.Run ${ipc[@]+"${ipc[@]}"} -ftCases "$cases" -ftOut "$out"

while read -r id table scenario avatar mesh ndmf; do
  [ -z "${id:-}" ] || [ -z "${scenario:-}" ] && continue
  run --no-quit "$project" Kmnk.FaceTrackingGenerator.ScenarioRunner.Run ${ipc[@]+"${ipc[@]}"} \
    -ftScenario "$scenario" -ftOut "$out/$id/scenario.jsonl" -ftAvatar "$avatar" -ftMesh "$mesh" -ftNdmf "${ndmf:-false}"
  if [ "${ndmf:-false}" = "true" ]; then
    folder=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["outputFolder"])' "$out/$id/load.json" 2>/dev/null)
    [ -n "$folder" ] || folder="Assets/FaceTrackingGeneratorOutput/$(basename "$(dirname "$table")")"
    mkdir -p "$out/$id/build"
    for d in Build Menus; do
      [ -d "$project/$folder/$d" ] && cp -R "$project/$folder/$d" "$out/$id/build/"
    done
  fi
done < <(grep -v '^[[:space:]]*#' "$cases")
exit $status
