#!/usr/bin/env bash
# Builds VRChat_FaceTrackingGenerator_<version>.unitypackage from the development copy in an avatar project.
#
# Usage: scripts/build-package.sh <version> [<dev project>]
#   <dev project>  the avatar project with the development copy (default: $FTG_DEV_PROJECT)
#
# 1. Writes VERSION and FILES.txt (every file in the package) into the development copy
# 2. Opens the project so Unity makes .meta files for them
# 3. Copies Assets/Kmnk/FaceTrackingGenerator/ to this repository (Settings/ excluded; .meta GUIDs kept)
# 4. Stages the copy and exports exactly `git ls-files Assets` plus their parent folders, in the dev
#    project (the paths are the same there), to build/
#
# The dev project must be on a work branch and closed in the editor (see scripts/unity-batch.sh).
set -eu
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/.." && pwd)
version=${1:?version}
dev=${2:-${FTG_DEV_PROJECT:?give the dev project as the second argument or set FTG_DEV_PROJECT}}
rel=Assets/Kmnk/FaceTrackingGenerator
src="$dev/$rel"

# 1. VERSION and FILES.txt (files only, no .meta, Settings excluded, sorted)
printf '%s\n' "$version" > "$src/VERSION"
( cd "$dev" && { find "$rel" -type f ! -name '*.meta' ! -path "$rel/Settings/*" ! -name '.DS_Store'; echo "$rel/FILES.txt"; } | sort -u ) > "$src/FILES.txt.tmp"
mv "$src/FILES.txt.tmp" "$src/FILES.txt"

# 2. .meta files
"$here/unity-batch.sh" "$dev" >/dev/null

# 3. copy
mkdir -p "$repo/$rel"
rsync -a --delete --exclude '/Settings/' --exclude '/Settings.meta' --exclude '.DS_Store' "$src/" "$repo/$rel/"
cp "$dev/Assets/Kmnk.meta" "$repo/Assets/Kmnk.meta"
[ -f "$dev/Assets/Kmnk/FaceTrackingGenerator.meta" ] && cp "$dev/Assets/Kmnk/FaceTrackingGenerator.meta" "$repo/Assets/Kmnk/FaceTrackingGenerator.meta"

# 4. export
git -C "$repo" add -A Assets
list="$repo/build/package-paths.txt"
mkdir -p "$repo/build"
git -C "$repo" ls-files Assets | grep -v '\.meta$' | python3 -c '
import sys, os
files = [l.strip() for l in sys.stdin if l.strip()]
dirs = set()
for f in files:
    d = os.path.dirname(f)
    while d and d != "Assets":
        dirs.add(d); d = os.path.dirname(d)
print("\n".join(sorted(dirs) + files))' > "$list"
out="$repo/build/VRChat_FaceTrackingGenerator_$version.unitypackage"
rm -f "$out"
"$here/unity-batch.sh" "$dev" Kmnk.FaceTrackingGenerator.Cli.ExportPackage -ftList "$list" -ftOut "$out" | grep -E '^unity-batch: exit=|^\[FTG\]'
echo "build-package: $out"
