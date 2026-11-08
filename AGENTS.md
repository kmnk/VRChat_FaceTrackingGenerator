# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables. This repository is public (MIT). The code lives in `Packages/dev.kmnk.face-tracking-generator/` and is distributed as a VPM package (zip on the GitHub release, listed at `https://kmnk.github.io/vpm-repos/index.json` by the repository `kmnk/vpm-repos`).

## Layout

- `Packages/dev.kmnk.face-tracking-generator/package.json` - VPM manifest: version, dependencies (`com.vrchat.avatars`, `nadena.dev.ndmf`), and `legacyFolders` / `legacyFiles` that remove the `.unitypackage` layout (never list `Assets/Kmnk/FaceTrackingGenerator` itself: that removes `Settings/`)
- `Packages/dev.kmnk.face-tracking-generator/Editor/` - generator code (namespace `Kmnk.FaceTrackingGenerator`, assembly `Kmnk.FaceTrackingGenerator.Editor`). `Paths.cs` holds the package root and the settings root
- `Packages/dev.kmnk.face-tracking-generator/Runtime/` - `FtGeneratorSettings` (assembly `Kmnk.FaceTrackingGenerator.Runtime`)
- `Packages/dev.kmnk.face-tracking-generator/Templates/` - simple templates of the three tables and device lists
- `Assets/Kmnk/FaceTrackingGenerator/Settings/` - personal settings copied from the templates, outside the package so updates keep them. Not in this repository; tracked in each private avatar project
- `scripts/` - commands for development (not in the package)

## Rules for this public repository

- Do not commit blendshape names, mesh names or asset names of purchased avatars. Not in code, comments, templates or commit messages. Avatar-specific values go to `Settings/` in the avatar project
- Do not bundle other people's code. The tool depends only on NDMF, declared in `vpmDependencies` in `package.json`, so VPM installs it. Modular Avatar is not required
- Keep the `.meta` GUID of every file (the settings component on avatars refers to `FtGeneratorSettings.cs` by GUID). Move files inside Unity, or move each file together with its `.meta` while the project is closed

## Versioning

The version is `version` in `package.json`, `x.y.z`, following Semantic Versioning 2.0.0 (https://semver.org/). Whether a change breaks compatibility is judged against what users rely on:

1. The table format: keys, meanings and defaults of the three JSON layers
2. The serialized fields of `FtGeneratorSettings`
3. File paths and `.meta` GUIDs in the package, and the use of `Settings/`
4. Names in the generated output: parameters (`FaceTrack` / `AFKPlay` defaults, `FT/*`, `AFK/*`) and output paths
5. Command line entry points (`Cli.*`, `ScenarioRunner.*`) and their arguments

- **x**: existing tables or setups stop working, or users have to edit them. Renaming or removing a table key or a component field, changing a key's meaning, changing a GUID or a file location, renaming generated parameters already in use, or replacing how the tool generates (for example Animator layers to VRChat's Face Tracking section)
- **y**: existing tables keep working and the tool can do more. New optional table keys, new window buttons, commands or checks, changed defaults that need no table edits (tell users), and deprecating a table key before removing it in the next x
- **z**: fixes that make the tool behave as documented, and changes to documents, comments or internals that do not change the generated output

Rules:

- Raising x resets y and z to 0; raising y resets z to 0
- Until the public release the tool is a development version: `0.y.z`, x stays 0, and changes that break compatibility raise y. Packages built for trying the tool follow these rules too. The public release is `1.0.0`; after it, x, y and z are used as defined above
- If a release breaks compatibility under the wrong number, do not change the released version: release the next version that restores compatibility and tell users

## Development flow

The working copy of the code is an avatar project used for development (`<dev project>/Packages/dev.kmnk.face-tracking-generator/`, an embedded package, on a work branch or in a project marked with `.ftg-dev-project`; its tables are in `<dev project>/Assets/Kmnk/FaceTrackingGenerator/Settings/`). Edit and check there, then copy to this repository with `scripts/build-package.sh`. Give the project path as an argument or in the environment variable `FTG_DEV_PROJECT`; do not write it in this repository. If the development project has its own `AGENTS.md`, read it too: steps that depend on the local machines are kept there, not here.

## Verification

Use these commands to check changes to the generator by running it the same way a user does (Editor methods in the Unity project), on macOS.

### Before running

- Unity must run outside the Claude Code sandbox (license files under `~/Library/Unity`; inside the sandbox it stops after 60 s with exit 199)
- Close the project in the Unity editor. `scripts/unity-batch.sh` stops when `Temp/UnityLockfile` exists
- The avatar project must be on a work branch. `scripts/unity-batch.sh` and `scripts/unity-gui.sh` stop on `main` / `master`, because those branches are uploaded from Windows. A project used only for developing the generator (never uploaded as the user's avatar) may use `main`: commit an empty `.ftg-dev-project` at its root, and the scripts run there. `scripts/test-unity-batch.sh` checks these stops
- Nothing here uploads to VRChat. The Mac has no VRChat client
- When Unity started from the command line stops with `No valid Unity Editor license found`, Unity Hub's licensing client is listening only on its own channel. Pass it as `-licensingIpc LicenseClient-<id>` after the method (the `<id>` is in the name of `/tmp/Unity-LicenseClient-<id>.sock`, not the `-notifications` one), as the Hub does when it opens a project

### `scripts/unity-batch.sh`

```
scripts/unity-batch.sh [--no-quit] <project> [<executeMethod> [args...]]
```

Opens the project in batchmode, calls the method, and prints the exit code, lines starting with `[FTG]`, errors in the log, and `git status --short` of the project after the run. Set `UNITY_BATCH_LOG_DIR` to keep logs in a known place. `scripts/test-unity-batch.sh` checks the safety stops without starting Unity.

NDMF prints `Exception: mprotect returned EACCES` from its Harmony patch on Apple silicon. The project still opens with exit code 0; ignore it unless NDMF processing is being checked.

Entering Play Mode on the Mac re-creates `Assets/XR/` and `Assets/XR.meta` in the avatar project (it may have been removed on purpose). Delete them after Play Mode runs and do not commit them.

### Scenario check (`ScenarioRunner`)

Plays the avatar's Playable Layers in Play Mode and writes blendshape values, layer states and tracking per snapshot as JSON Lines.

```
scripts/unity-batch.sh --no-quit <project> Kmnk.FaceTrackingGenerator.ScenarioRunner.Run \
  -ftAvatar <prefab path> -ftMesh <mesh name> -ftScenario <scenario file> -ftOut <out .jsonl> [-ftFps <n>]
```

- Success: exit 0 and `[FTG] scenario: wrote <n> records to <out>`
- `-ftNdmf true` runs NDMF on the avatar first, as entering Play Mode does (for an avatar with the settings component), and prints its Face Tracking section (`[FTG] scenario: face tracking ...`)
- The generated layers read the inputs as `FT/In/<input name>` (Forward to Animator writes them in VRChat), and run FT only while VRChat's `UserFaceTracking` is true: a scenario sets both, e.g. `set UserFaceTracking 1` and `set FT/In/JawOpen 0.5`
- Scenario file format: see the comment at the top of `ScenarioRunner.cs`
- Paths relative to the project root work for `-ftScenario` and `-ftOut`

### Comparing records

```
scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.ScenarioRunner.Compare \
  -ftA <a.jsonl> -ftB <b.jsonl> [-ftTolerance 1.0] [-ftOut <diff.txt>]
```

- Records are matched by scenario and label. Blendshape values missing on one side count as 0
- Exit 0 and `differences=0` when every value is within the tolerance. Exit 1 lists `[FTG] diff <scenario> <label> <shape> <A> <B>`

### Generator commands (`Kmnk.FaceTrackingGenerator.Cli`)

All take `-ftTable <avatar table>` (a path relative to the project root works) and exit 0 on success, 1 when checks find problems, 2 on errors. Results are logged as `[FTG] ...` lines.

| Method | What it does | Success |
| --- | --- | --- |
| `Cli.Load` | Reads the three tables and writes the resolved model (`-ftOut <json>`) | `[FTG] load: inputs=...`; dropped rows and warnings are listed |
| `Cli.Generate` | Copies the avatar's controllers and Expression Parameters to the output folder, adds FT and AFK layers, updates the Prefab Variant | `[FTG] wrote ...`; running it twice leaves `git status` empty |
| `Cli.Check` | Static checks of the tables and the generated FX (`-ftFx` to check another controller), and of the Face Tracking section of the generated Prefab Variant: one Forward to Animator behavior per input with its expression, and no warnings from the SDK's `FaceTrackingExpressionValidator` | `[FTG] check: face tracking <variant>: enabled=True allKeys=True behaviors=<n>`, `[FTG] check: findings=0` |
| `Cli.InstallMenu` | Adds the FT toggle and the AFK flag to the avatar's own menu and Expression Parameters (changes original assets) | `[FTG] menu: ...` per parameter |
| `Cli.Curve` | Response curves without Unity's Animator (`-ftInput <name>` or `-ftQueries <jsonl>`, `-ftOut <jsonl>`) | `[FTG] curve: wrote ...` |
| `Cli.SetAfkClips`, `Cli.CreateSettings`, `Cli.FillFromAvatar` | The window's AFK clip saving, settings from templates (`-ftName <name>`), and filling prefab/mesh/menu and the empty AFK clips from an avatar prefab (`-ftAvatar <prefab path>`; AFK clips come from the shape of the avatar's own AFK layer in the descriptor's Action and FX, see `AfkFinder.cs`) | `[FTG] fill: ...`, `[FTG] fill: afk <key>: <clip>\|kept\|none` |
| `Cli.FindAfk` | Prints the AFK clips `AfkFinder` finds in controllers (`-ftController "<path>;..."`) | `[FTG] afk found <controller>/<layer>: intro=... loop=... outro=...` |
| `Cli.ExportPackage` | Exports a `.unitypackage` from a list of paths (`-ftList`, `-ftOut`). Not used by the release since 0.5.0 | `[FTG] package: exported ...` |

The editor window `Tools/Kmnk/Face Tracking Generator` calls the same functions. It cannot be drawn in batchmode (`-nographics`); check its drawing in the editor.

### Table editor checks (`Kmnk.FaceTrackingGenerator.TableEditorChecks`)

The settings editor (`TableEditorWindow`, menu `Tools/Kmnk/Face Tracking Settings Editor`) calls the same code as these methods. Exit 0 on success, 1 when a check fails, 2 on errors.

| Method | What it does | Success |
| --- | --- | --- |
| `TableEditorChecks.Edit` | Opens `-ftTable`, applies `-ftEdits <jsonl>` (operations of `TableEdits.Apply`) and saves; `-ftAll 1` saves all three tables | `[FTG] table-editor edit: ...` |
| `TableEditorChecks.Preview` | Shows `-ftQueries` (the `Cli.Curve` form) with the preview on the table's prefab in a new scene; checks values, pixels against weights set directly, that the head does not move, and that stopping restores the face. Run with `UNITY_BATCH_GRAPHICS=1`. Images and `weights.jsonl` go to `-ftOut` | `[FTG] table-editor preview: queries=<n> failed=0` |
| `TableEditorChecks.Switches` | Prints the switch states of the preview's Auto for `-ftSteps <jsonl>` | `[FTG] table-editor switches ...` lines |
| `TableEditorChecks.FaceView` | Draws the face of the table's prefab as Face View does: all parts at 0, and each of `-ftShapes "mesh/shape,..."` at 100; checks the weights on the preview copy, that each face differs from the neutral one, that drawing again gives the same image (under 0.1% of pixels differ, by at most 8/255), and that the prefab file and the open scene do not change. Run with `UNITY_BATCH_GRAPHICS=1` | `[FTG] table-editor face-view: shapes=<n> failed=0 ...` |
| `TableEditorChecks.Guidance` | Prints what moves each face movement, the "Try expressions" presets with the overlap warnings for each, and per device (`Templates/Devices/*.jsonc`) how many settings inputs it sends, and for each input whether the device sends it, its range with the device and the preview slider's range (`[FTG] device <id> input <name>: sent=... range=... slider=... clipped=...`; clipped: some thresholds are outside what the device sends). `-ftSet "Input=value,..."` adds the warnings for those values; `-ftHelp en` prints English | `[FTG] usage ...`, `[FTG] overlap ...`, `[FTG] device ...` |
| `TableEditorChecks.Tuning` | Prints what the Tuning tab shows (1D nodes per tree with their positions and face movements, trees with only 2D nodes, smoothing times in ms). Applies `-ftSetPosition "<tree>/<node>/<position>=<value>;..."` and `-ftSetSmoothing "<name>/local=<ms>;<name>/remote=<ms>"` as the tab does, prints the face weights for `-ftTry "<value>,..."` on the first node, `-ftAddPosition "<tree>/<node>=<x>"` (with the curve difference), `-ftRemovePosition "<tree>/<node>/<i>"`, `-ftAddPart "<tree>/<node>/<i>=<part>"`, `-ftSetAmount "<tree>/<node>/<i>/<part>=<v>"`, `-ftRemovePart2D "<tree>/<node2d>/<i>/<part>"`, `-ftAddPosition2D "<tree>/<node2d>=<x>,<y>"` (with the amount difference at the point), `-ftSetPosition2D "<tree>/<node2d>/<i>=<x>,<y>"`, `-ftRemovePosition2D "<tree>/<node2d>/<i>"`, switch nodes with face movements in their states (printed as `[FTG] tuning switch <tree>/sw/<k>: ...`): `-ftAddPartSw "<tree>/sw/<k>/<state>=<part>"`, `-ftSetAmountSw "<tree>/sw/<k>/<state>/<part>=<v>"`, `-ftRemovePartSw "<tree>/sw/<k>/<state>/<part>"`, `-ftTrySw "<tree>/sw/<k>/<state>"` (the Face view weights with the switch in that state), `-ftSetPart "<part>=<shape>;..."` (avatar table), `-ftNames "a,b"` (new name check); prints the enabled buttons (`tuning can`) and the face movement menu (`tuning menu`); with `-ftSave 1` saves and prints the reloaded positions, taus and the expanded model | `[FTG] tuning ...` lines |
| `TableEditorChecks.Parts` | Renames face movements (`-ftRenamePart "<from>=<to>[,both];..."`, with the weight difference), adds face movements with a blendshape (`-ftAddPart "<name>=<shape>;..."`) and removes unused ones (`-ftRemovePart "<name>;..."`, refused when an expression uses it) as the Avatar tab does, then prints each movement's shape and use and the Tuning choices; `-ftSave 1` saves | `[FTG] parts add ...`, `[FTG] parts remove ...: done=...` |
| `TableEditorChecks.Inputs` | Prints what the Inputs tab shows for `-ftTable` and `-ftDevice <id>` (a file name in `Templates/Devices`): the rows in and not in the settings, each with its expression and, in the settings, whether the device sends it. Applies `-ftAddInput "<name>;..."` (e.g. `BrowInnerUp{side}`) and `-ftRemoveInput "<name>;..."`; `-ftTips 1` prints the tooltips of the rows and inputs (`-ftHelp en\|ja\|off`) and how many have no description in `Templates/parameters.jsonc`; `-ftSave 1` saves | `[FTG] inputs in=<n> out=<n>`, `[FTG] inputs in <name> \| ... expression=... not-sent=...`, `[FTG] inputs saved ...` |
| `TableEditorChecks.Graph` | Prints the expression graph of `-ftTable` (`-ftTree <name>` for one tree): boxes with their path, title, place, Tuning fold key and rows, the lines, and the inputs offered for a split. `-ftOpen "<box path>;..."` lays out those boxes open (`h=` and the face movements per line), and `[FTG] graph tip` lines print the full text of cut rows. Applies `-ftSplit "<tree>\|<box path>\|<row>=<input>;..."`, `-ftSplit2D "<tree>\|<box path>\|<row>=<x>,<y>;..."` `-ftRemove "<tree>\|<box path>\|<keep row>;..."`, `-ftInsert "<tree>\|<box path>=<x>[,<y>];..."` (with the weight difference when the new inputs are 0), `-ftNewTree "<name>=<x>[,<y>];..."` (`<name>{side}` makes a left/right pair and prints the inputs it offers), `-ftSwitchBox "split\|insert\|tree\|<tree>\|<path>\|<row>\|<switch or new>;..."` (switch boxes), `-ftSwitchEdit "<op>\|<switch>\|...;..."` (rename, state, add-state, remove-state, add-tr, remove-tr, set-tr, add-cond, remove-cond, set-cond, new, remove; see the comment in `TableEditorChecks.Graph.cs`) and `-ftRemoveTree "<name>;..."` as the graph does, `-ftDevice <id>` (prints `[FTG] graph device <tree>: ... choices=...`, the device inputs not in the settings that the split list offers under "Add from <device>") with `-ftSplitAdd "<tree>\|<box path>\|<row>=<device input>\|<x>[,<y>];..."` (adds the input as the list does, then splits; `offered=false` when the list does not offer it), with the largest weight difference before and after (over fixed input values); `-ftSave 1` saves | `[FTG] graph box ...`, `[FTG] graph edge ...`, `[FTG] graph split ... can=true ... max-weight-diff=0` |
| `TableEditorChecks.InputParts` | Prints the parts under each slider of the preview and whether the preview shows the reason that the face does not move | `[FTG] input-parts ...`, `[FTG] preview-reason: show=...` |
| `TableEditorChecks.Picker` | Prints the blendshape list for `-ftPart` and `-ftSearch` | `[FTG] picker: ...` |
| `TableEditorChecks.Capture` | Opens the window and writes it to a PNG (`-ftOut`). Run with `scripts/unity-gui.sh`, not batchmode. Arguments: `-ftTable`, `-ftTab avatar\|preview\|tuning\|inputs`, `-ftTuneSet "<tree>/<node>/<position>=<value>"`, `-ftTuneTry <value>` (first node), `-ftTuneOpen "<tree>;!<tree>"` (open or close folds), `-ftTuneTry2D "<tree>/<node2d>=<x>,<y>"`, `-ftHelp en\|ja\|off`, `-ftState dirty\|picker\|preview\|notable`, `-ftSet "Input=value,..."`, `-ftScroll <y>`, `-ftSize 560x1000`, `-ftWindow generator`, `-ftWindow graph` with `-ftGraphTree <tree>`, `-ftGraphSelect <box path>`, `-ftGraphSplitList "<box path>#<row>"`, `-ftGraphListPage "<group>/<group>"` (opens pages of the split list and prints their rows), `-ftGraphListChoose <row>` (chooses a row of that page and prints the inputs and boxes; `-ftGraphListUndo 1` then undoes once), `-ftGraphOpen "<box path>;..."`, `-ftHelpAfter en\|ja\|off` (changes the language after the windows open), `-ftGraphNew <name>` (opens the + New Expression panel; written as `<out>.popup.png`; `-ftGraphNewSides 1` checks Left and right), `-ftTuneShow all\|selected`, `-ftTuneShowKey <key>` (shows a Tuning key in Face view, e.g. `<tree>/sw/<k>/<state>`), `-ftBuildBegin 1` / `-ftBuildEnd 1` (call the VRChat SDK's build-requested and postprocess-avatar dispatchers; the windows pause and resume, and `[FTG] capture: frame=<20|35|55> building=... preview-running=... preview-scene-avatars=<n> sdk-avatars=<n>` lines are printed before, during and after the build: avatar descriptors in preview scenes (Face View's copy has none) and the active avatars the SDK panel offers to build, counted by the SDK's own search), `-ftSwitchOpen "<switch>;..."` (open rows of Switches), `-ftAvatarAddPart <name>` (as + Add Face Movement; `<name>_{s}` for left/right), `-ftAvatarRename "<part>=<new name>"` (opens the rename panel; written as `<out>.popup.png`) (also writes `<out>.tuning.png`, `<out>.split.png` and a `[FTG] capture: tuning tab=... open=...` line), `-ftFaceView 1` (also writes `<out>.faceview.png`), `-ftSelectPart <part>`, `-ftPickerPart <part> -ftHoverShape <shape>`, `-ftOpenTableEditor 1` (with `-ftWindow generator`), `-ftDevice <id>`, `-ftInputAdd "<name>;..."` (check inputs in the Inputs tab), `-ftInputSearch <text>`, `-ftShowSwitches 1`, `-ftOpenScene <scene>` (opened before the window), `-ftChooseTable <avatar.jsonc>\|none` (settings editor) and `-ftChooseAvatar <Hierarchy path>\|none` (the **Avatar in scene** field of the settings editor, or of the generator window with `-ftWindow generator`), which choose as the fields do and are remembered for the project, and `-ftAvatarName <name>` (types in the generator window's **Avatar name**, also remembered). Each run also prints what the windows show of the project's remembered choices (`ProjectSelection`, kept in `UserSettings/Kmnk.FaceTrackingGenerator.asset`): `[FTG] capture: selection window=table table=<path\|none> avatar=<Hierarchy path\|none> source=<saved\|prefab\|selected\|chosen\|none> preview=<true\|false>`, and for the generator window `window=generator table=... avatar=... source=... fill=<true\|false> attach=<true\|false> name=<avatar name>` (whether its two avatar buttons can be pressed, and the Avatar name). Starting Unity again for each run checks what a user sees after reopening the project | `[FTG] capture: wrote <png>` |
| `TableEditorChecks.MakeTwoAvatarScene` | Saves a scene (`-ftOut`, under `Assets`) with two avatars made from the prefab of `-ftTable` (the second named `<prefab name> 2`), for checking which avatar the settings editor brings back. Delete it after the check | `[FTG] scene: wrote <path> avatars=<a>,<b>` |

`scripts/test-table-save.sh <project> <avatar.jsonc> <work dir> [<edits.jsonl>]` copies a set of tables to `<work dir>` and checks that saving them unchanged keeps the `Cli.Load` output and that a second save changes no byte. Use a work dir outside the project: Unity deletes the project's `Temp/` when it opens.

`scripts/unity-gui.sh <project> <method> [args...]` starts the editor with graphics (not batchmode) and has the same stops as `unity-batch.sh`. The editor opens on the screen for about a minute; do not type while it runs (keys go to Unity). It rewrites `ProjectSettings/EditorBuildSettings.asset` (the XR loader GUID); restore it with `git checkout` after the run.

### Building the package

```
scripts/build-package.sh <version> [<dev project>]   # or set FTG_DEV_PROJECT
```

Writes the version into `package.json` of the development copy, copies `Packages/dev.kmnk.face-tracking-generator/` here, stages it, and zips the staged files to `build/dev.kmnk.face-tracking-generator-<version>.zip` (`package.json` at the root of the zip). Before pushing, search the tracked files, `git log -p` and the zip for the avatars' blendshape names (none may be found).

### Releasing

1. Commit and push, then the user runs `gh release create v<version> build/dev.kmnk.face-tracking-generator-<version>.zip` (development versions: `--prerelease --title "<version> (development version)"`). Creating a release publishes it, so the user runs it
2. The user rebuilds the listing: `gh workflow run build-listing.yml -R kmnk/vpm-repos`. It reads every release of this repository and adds zips that have `package.json` at their root
3. Check: `curl -s https://kmnk.github.io/vpm-repos/index.json` lists the version

### Evidence

Paste the `unity-batch:` lines, the `[FTG]` lines and the `git status --short` part of the output into plans and reports.

### Cleanup

Records written with `-ftOut` stay where they were written. Delete `Assets/XR/` and `Assets/XR.meta` after Play Mode runs.
