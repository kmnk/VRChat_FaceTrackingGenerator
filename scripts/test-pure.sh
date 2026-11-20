#!/usr/bin/env bash
# Tests of the generator's table loading and face values outside Unity (about a second), for small refactoring steps.
#
# Usage:
#   scripts/test-pure.sh <project> [--write]
#
#   <project>  Unity project with the generator (its Packages/dev.kmnk.face-tracking-generator/Editor is compiled)
#   --write    write tests/pure/expected/ from the current code instead of comparing
#
# Compiles TableModel, TableLoader, NativeSets, NativeInputs, FaceEval, Measurements, PartNames, FtExpression, ModelJson
# and tests/pure/*.cs with the mono and csc that come with Unity, then loads each tests/pure/cases/<case>/avatar.jsonc
# and compares the model and the faces with tests/pure/expected/<case>.json.
# Success: exit 0 and "pure: cases=<n> failed=0". A difference leaves the actual JSON in $TMPDIR/pure-<case>.json.
#
# Environment: UNITY_APP (default: Unity 2022.3.22f1 from Unity Hub on macOS).
set -eu
if [ $# -lt 1 ]; then
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
  exit 2
fi
project=$1
shift
here=$(cd "$(dirname "$0")/.." && pwd)
app=${UNITY_APP:-/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents}
mono="$app/MonoBleedingEdge/bin/mono"
csc="$app/MonoBleedingEdge/lib/mono/4.5/csc.exe"
facades="$app/MonoBleedingEdge/lib/mono/4.5/Facades"
src="$project/Packages/dev.kmnk.face-tracking-generator/Editor"
newtonsoft=$(ls "$project"/Library/PackageCache/com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll | head -1)
sdk="$project/Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll"
sdkbase="$project/Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRCSDKBase.dll"
core="$app/Managed/UnityEngine/UnityEngine.CoreModule.dll"
out=${TMPDIR:-/tmp}/ftg-pure
mkdir -p "$out"
cp "$newtonsoft" "$sdk" "$sdkbase" "$core" "$out/"
sources=()
for f in TableModel TableLoader NativeSets NativeInputs FaceEval Measurements PartNames FtExpression ModelJson; do
  sources+=("$src/$f.cs")
done
# The names NativeSets takes from FtBuilder: from the project's FtNames.cs once it exists, else from the stand-in.
if [ -f "$src/FtNames.cs" ]; then sources+=("$src/FtNames.cs"); else sources+=("$here/tests/pure/stubs/FtBuilderNames.cs"); fi
"$mono" "$csc" -nologo -langversion:9.0 -nowarn:1701,1702 -out:"$out/pure.exe" \
  -r:"$facades/netstandard.dll" -r:"$out/Newtonsoft.Json.dll" -r:"$out/VRCSDK3A.dll" -r:"$out/VRCSDKBase.dll" -r:"$out/UnityEngine.CoreModule.dll" \
  "${sources[@]}" "$here"/tests/pure/*.cs
# Relative paths, so that the messages in the expected files do not depend on where the repository is.
cd "$here"
"$mono" "$out/pure.exe" tests/pure/cases tests/pure/expected "$@"
