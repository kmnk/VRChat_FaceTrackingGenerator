#!/usr/bin/env bash
# Builds the VPM package zip dev.kmnk.face-tracking-generator-<version>.zip from the development copy.
#
# Usage: scripts/build-package.sh <version> [<dev project>]
#   <dev project>  the avatar project with the development copy (default: $FTG_DEV_PROJECT)
#
# 1. Writes <version> into package.json of the development copy (Packages/dev.kmnk.face-tracking-generator/)
# 2. Opens the project so Unity makes .meta files for new files
# 3. Copies the package folder to this repository (.meta GUIDs kept) and stages it
# 4. Zips exactly the staged files of the package folder, with package.json at the root of the zip,
#    to build/. Attach the zip to the GitHub release v<version>; the VPM listing picks it up from there.
#
# The dev project must be on a work branch (or have .ftg-dev-project) and be closed in the editor
# (see scripts/unity-batch.sh).
# The user's tables stay in the dev project's Assets/Kmnk/FaceTrackingGenerator/Settings/ and are not copied.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/.." && pwd)
version=${1:?version}
dev=${2:-${FTG_DEV_PROJECT:?give the dev project as the second argument or set FTG_DEV_PROJECT}}
name=dev.kmnk.face-tracking-generator
rel=Packages/$name
src="$dev/$rel"
[ -f "$src/package.json" ] || { echo "build-package: no $src/package.json" >&2; exit 2; }

# 1. version
python3 - "$src/package.json" "$version" <<'PY'
import json, sys, collections
path, version = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8") as f:
    d = json.load(f, object_pairs_hook=collections.OrderedDict)
d["version"] = version
with open(path, "w", encoding="utf-8") as f:
    f.write(json.dumps(d, indent=2, ensure_ascii=False) + "\n")
PY

# 2. .meta files
"$here/unity-batch.sh" "$dev" >/dev/null

# 3. copy
mkdir -p "$repo/$rel"
rsync -a --delete --exclude '.DS_Store' "$src/" "$repo/$rel/"
git -C "$repo" add -A "$rel"

# 4. zip (paths inside the zip are relative to the package folder)
out="$repo/build/$name-$version.zip"
mkdir -p "$repo/build"
rm -f "$out"
( cd "$repo/$rel" && git -C "$repo" ls-files "$rel" | sed "s|^$rel/||" | zip -q -X "$out" -@ )
echo "build-package: $out ($(unzip -l "$out" | tail -1 | awk '{print $2}') files)"
