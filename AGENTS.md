# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables. This repository is public (MIT). The code lives in `Packages/dev.kmnk.face-tracking-generator/` and is distributed as a VPM package (zip on the GitHub release, listed at `https://kmnk.github.io/vpm-repos/index.json` by the repository `kmnk/vpm-repos`).

## Layout

- `Packages/dev.kmnk.face-tracking-generator/package.json` - VPM manifest: version, dependencies (`com.vrchat.avatars`, `nadena.dev.ndmf`), and `legacyFolders` / `legacyFiles` that remove the `.unitypackage` layout (never list `Assets/Kmnk/FaceTrackingGenerator` itself: that removes `Settings/`)
- `Packages/dev.kmnk.face-tracking-generator/Editor/` - generator code (namespace `Kmnk.FaceTrackingGenerator`, assembly `Kmnk.FaceTrackingGenerator.Editor`). `Paths.cs` holds the package root and the settings root
- `Packages/dev.kmnk.face-tracking-generator/Runtime/` - `FtGeneratorSettings` (assembly `Kmnk.FaceTrackingGenerator.Runtime`)
- `Packages/dev.kmnk.face-tracking-generator/Templates/` - simple templates of the two tables (avatar, expression) and device lists
- `Assets/Kmnk/FaceTrackingGenerator/Settings/` - personal settings copied from the templates, outside the package so updates keep them. Not in this repository; tracked in each private avatar project
- `Packages/dev.kmnk.face-tracking-generator.checks/` - development checks (assembly `Kmnk.FaceTrackingGenerator.Checks`: `TableEditorChecks`, the characterization cases, window repros). Copied by `scripts/build-package.sh` but not in the zip; the generator gives it access with `InternalsVisibleTo`
- `scripts/` - commands for development (not in the package)
- `tests/pure/` - cases and expected results of `scripts/test-pure.sh`

## Rules for this public repository

- Do not commit blendshape names, mesh names or asset names of purchased avatars. Not in code, comments, templates or commit messages. Avatar-specific values go to `Settings/` in the avatar project
- Do not bundle other people's code. The tool depends only on NDMF, declared in `vpmDependencies` in `package.json`, so VPM installs it. Modular Avatar is not required
- Keep the `.meta` GUID of every file (the settings component on avatars refers to `FtGeneratorSettings.cs` by GUID, and `UserSettings/Kmnk.FaceTrackingGenerator.asset` refers to `ProjectSelection.cs` by GUID). Move files inside Unity, or move each file together with its `.meta` while the project is closed

## Versioning

The version is `version` in `package.json`, `x.y.z`, following Semantic Versioning 2.0.0 (https://semver.org/). Whether a change breaks compatibility is judged against what users rely on:

1. The table format: keys, meanings and defaults of the two JSON layers
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
- The generated layers read the tracking keys the transitions compare as `FT/In/<key>` (Forward to Animator writes them in VRChat; the tool does not compute them from the keys), and run FT only while VRChat's `UserFaceTracking` is true: a scenario sets both, e.g. `set UserFaceTracking 1` and `set FT/In/MouthCornerPullLeft 0.8` (with `set MouthCornerPullLeft 0.8` for the Blendshapes Sets)
- The face goes into the Face Tracking section's Blendshapes Sets, which Unity does not run. The tool runs them: `set <tracking key> <value>` (e.g. `set EyeClosedLeft 1`) gives a key's value, and the snaps list the sets' weights under `sets`. A state machine's menu is `set FT/Menu/<state machine> <value>` (1, 2, ... in the order of its menu states; 0 for none)
- Scenario file format: see the comment at the top of `ScenarioRunner.cs`

### Perfect sync template (`scripts/check-perfect-sync.py`)

```
scripts/check-perfect-sync.py <package dir> [--log <Unity log of Cli.Check>]
```

Checks that `neutral` of `Templates/expression.template.jsonc` matches the table in `Documentation~/perfect-sync.md`, and with `--log` that the Blendshapes Set rows `Cli.Check` printed for a generated avatar with the 52 ARKit blendshapes match it too. Success: exit 0 and `52 names, 68 rows match`. When perfect sync, VRChat's Face Tracking section or the SDK's Auto-Fill changes, update `perfect-sync.md` (its sources and reasons) first, then the template.

`Kmnk.FaceTrackingGenerator.TableEditorChecks.MakePerfectSyncAvatar -ftPrefab <avatar prefab> -ftOut <folder>` makes check avatars: Prefab Variants with a generated mesh `PerfectSyncFace` that has the 52 ARKit blendshapes (every second name with a capital first letter), one without and one with its own Blendshapes Set. They use copies of the prefab's Expression Parameters and menu (`PerfectSyncParameters.asset`, `PerfectSyncMenu.asset`), so generating for them does not change the originals (generation never changes the original menu and Expression Parameters anyway).
- Paths relative to the project root work for `-ftScenario` and `-ftOut`

### Comparing records

```
scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.ScenarioRunner.Compare \
  -ftA <a.jsonl> -ftB <b.jsonl> [-ftTolerance 1.0] [-ftOut <diff.txt>]
```

- Records are matched by scenario and label. Blendshape values missing on one side count as 0
- Exit 0 and `differences=0` when every value is within the tolerance. Exit 1 lists `[FTG] diff <scenario> <label> <shape> <A> <B>`

### Behaviour baseline (`scripts/characterize.sh`)

```
scripts/characterize.sh <project> <cases file> <out dir>
```

Records what the generator does for a set of cases so that a refactoring can be compared with the record taken before it: for each case, the `Cli.Load` JSON, the output folder after `Cli.Generate`, the `Cli.Check` lines, `Cli.Curve` for VR and desktop (rest, every key at 1, and each state), and a `ScenarioRunner` record (with `-ftNdmf true`, also `Build/` and `Menus/`). The project needs the checks package `dev.kmnk.face-tracking-generator.checks` (development only, not in the released zip); it runs all cases in one Unity run. A cases file has one case per line: `<id> <avatar table> [<scenario> <avatar prefab> <mesh> <ndmf true|false>]`. Set `UNITY_LICENSING_IPC=LicenseClient-<id>` when Unity needs it (see "Before running").

- Success: exit 0, and `diff -r <baseline out dir> <out dir>` prints nothing for a change that should not change behaviour. Running it twice on the same code gives no difference (the output folders are kept, so this also checks that generating again changes nothing)
- Keep baselines whose cases use purchased avatars in the private development project, not here

### Tests outside Unity (`scripts/test-pure.sh`)

```
scripts/test-pure.sh <project> [--write]
```

Compiles the table loading and face value code of the project's package (`FtNames`, `TableModel`, `TableLoader`, `NativeSets`, `NativeInputs`, `FaceEval`, `Measurements`, `PartNames`, `FtExpression`, `ModelJson`) with the mono and csc that come with Unity, loads each `tests/pure/cases/<case>/avatar.jsonc`, and compares the model and the faces with `tests/pure/expected/<case>.json`. It takes about a second, so it can run after each small step. `--write` writes the expected files from the current code (only when a change of behaviour is intended). Write the cases with ARKit names and made-up names only.

- Success: exit 0 and `pure: cases=<n> failed=0`. A difference prints `pure: <case> differs` and leaves the actual JSON in `$TMPDIR/pure-<case>.json`

### Generator commands (`Kmnk.FaceTrackingGenerator.Cli`)

All take `-ftTable <avatar table>` (a path relative to the project root works) and exit 0 on success, 1 when checks find problems, 2 on errors. Results are logged as `[FTG] ...` lines.

| Method | What it does | Success |
| --- | --- | --- |
| `Cli.Load` | Reads the two tables and writes the resolved model (`-ftOut <json>`); `-ftFull 1` adds the avatar table as resolved (`avatarLayer`: parts with mesh, scale and property, menu, layers, AFK, Face Tracking options, output paths, recordings) | `[FTG] load: inputs=...`; dropped rows and warnings are listed |
| `Cli.Generate` | Copies the avatar's controllers and Expression Parameters to the output folder, adds FT and AFK layers, updates the Prefab Variant | `[FTG] wrote ...`; running it twice leaves `git status` empty (in a project that ignores the output folder, copy the folder after each run and compare with `diff -r`) |
| `Cli.Check` | Static checks of the tables and the generated FX (`-ftFx` to check another controller), and of the Face Tracking section of the generated Prefab Variant: one Forward to Animator behavior per input with its expression, and no warnings from the SDK's `FaceTrackingExpressionValidator`; that every menu control of the Variant and of the original prefab uses parameters in its Expression Parameters (`menu: ... which is not in the Expression Parameters`, as the SDK's build panel reports) | `[FTG] check: face tracking <variant>: enabled=True allKeys=True behaviors=<n>`, `[FTG] check: menu <variant>: <menu asset>: <controls>` per menu, `[FTG] check: findings=0` |
| `Cli.Curve` | The face as `FaceEval` computes it, without Unity's Animator: `-ftQueries <jsonl>` (`{"label", "set": {key: value}, "state": {state machine: state}}`) or one query with `-ftSet "Key=value,..."` and `-ftStates "StateMachine=state,..."`; `-ftDesktop 1` for the desktop side's Blendshapes Sets; `-ftOut <jsonl>` to write them | `[FTG] curve: {"label", "states", "shapes"}` per query |
| `Cli.SetAfkClips`, `Cli.CreateSettings`, `Cli.FillFromAvatar` | The window's AFK clip saving, settings from templates (`-ftName <name>`), and filling prefab/mesh/menu and the empty AFK clips from an avatar prefab (`-ftAvatar <prefab path>`; AFK clips come from the shape of the avatar's own AFK layer in the descriptor's Action and FX, see `AfkFinder.cs`) | `[FTG] fill: ...`, `[FTG] fill: afk <key>: <clip>\|kept\|none` |
| `Cli.ExportTables`, `Cli.ImportTables` | The settings editor's Share menu: copy the expression table of `-ftTable` byte for byte to the folder `-ftDir`, or list the tables the folder's would change and, with `-ftApply 1`, overwrite them (the avatar table is never copied). A missing or unreadable table in the folder stops with exit 2 before anything is written. `Cli.CreateSettings -ftFrom <folder>` makes the settings from the templates with the folder's expression table | `[FTG] share: exported ... (overwrote: <names>\|none)`, `[FTG] share: changes=<names>\|none`, `[FTG] share: imported <names>` |
| `Cli.BuildVariant` | Writes the Prefab Variant of the build-time output (`-ftOut <prefab path>`): the avatar with `FtGeneratorSettings` | `[FTG] build variant: wrote <path>` |
| `Cli.FindAfk` | Prints the AFK clips `AfkFinder` finds in controllers (`-ftController "<path>;..."`) | `[FTG] afk found <controller>/<layer>: intro=... loop=... outro=...` |

The editor window `Tools/Kmnk/Face Tracking Generator` calls the same functions. It cannot be drawn in batchmode (`-nographics`); check its drawing in the editor.

### Table editor checks (`Kmnk.FaceTrackingGenerator.TableEditorChecks`)

These are in the checks package (`Packages/dev.kmnk.face-tracking-generator.checks/`); the project needs it next to the generator. The settings editor (`TableEditorWindow`, menu `Tools/Kmnk/Face Tracking Settings Editor`) calls the same code as these methods. Exit 0 on success, 1 when a check fails, 2 on errors.

| Method | What it does | Success |
| --- | --- | --- |
| `TableEditorChecks.Edit` | Opens `-ftTable`, applies `-ftEdits <jsonl>` (operations of `TableEdits.Apply` on the avatar table, or with `"table": "expression"` of `StateMachineEdit.Apply` on the expression table, as the State Machine window makes them) and saves; `-ftAll 1` saves both tables | `[FTG] table-editor edit: ... stateMachines=<n> states=<n> transitions=<n>` |
| `TableEditorChecks.Preview` | Shows `-ftQueries` (the `Cli.Curve` form) with the preview on the table's prefab in a new scene; checks values, pixels against weights set directly, that the head does not move, and that stopping restores the face. Run with `UNITY_BATCH_GRAPHICS=1`. Images and `weights.jsonl` go to `-ftOut` | `[FTG] table-editor preview: queries=<n> failed=0` |
| `TableEditorChecks.Switches` | Prints the states the preview follows for `-ftSteps <jsonl>` (`{"label", "set": {...}, "menu": {state machine: state or null}}`); `-ftDevice <id>` runs as the Preview does with that device (`measured-vr`, `measured-desktop` from the recordings, or a `Templates/Devices` id), which decides VR or desktop | `[FTG] table-editor switches device=... side=<vr\|desktop>` and `[FTG] table-editor switches ...` lines |
| `TableEditorChecks.Measurements` | The table's ft-measure recordings without the windows: files and sides, per key of `-ftKeys "K1,K2"` its neutral, largest and usable range, and the sets and transitions the generator makes per device (the recordings do not change them) | `[FTG] measurements side=...`, `[FTG] measurements key ...`, `[FTG] measurements sets=... desktopSets=... rows=... transitions=... vrTransitions=... desktopTransitions=...` |
| `TableEditorChecks.FaceView` | Draws the face of the table's prefab as Face View does: all parts at 0, and each of `-ftShapes "mesh/shape,..."` at 100; checks the weights on the preview copy, that each face differs from the neutral one, that drawing again gives the same image (under 0.1% of pixels differ, by at most 8/255), and that the prefab file and the open scene do not change. Run with `UNITY_BATCH_GRAPHICS=1` | `[FTG] table-editor face-view: shapes=<n> failed=0 ...` |
| `TableEditorChecks.Guidance` | Prints what moves each face movement, the "Try expressions" presets with the overlap warnings for each, and per device (`Templates/Devices/*.jsonc`) how many settings inputs it sends, and for each input whether the device sends it, its range with the device and the preview slider's range (`[FTG] device <id> input <name>: sent=... range=... slider=... clipped=...`; clipped: some thresholds are outside what the device sends). `-ftSet "Input=value,..."` adds the warnings for those values; `-ftHelp en` prints English | `[FTG] usage ...`, `[FTG] overlap ...`, `[FTG] device ...` |
| `TableEditorChecks.Parts` | Renames face movements (`-ftRenamePart "<from>=<to>;..."`, with the weight difference), checks new names (`-ftNames "<name>,..."` prints `[FTG] parts name <name>: none\|form\|used`), adds face movements with a blendshape (`-ftAddPart "<name>=<shape>;..."`) and removes unused ones (`-ftRemovePart "<name>;..."`, refused when an expression uses it) as the Avatar tab does, then prints each movement's shape and use and all names; `-ftSave 1` saves | `[FTG] parts add ...`, `[FTG] parts remove ...: done=...` |
| `TableEditorChecks.InputParts` | Prints the parts under each slider of the preview and whether the preview shows the reason that the face does not move | `[FTG] input-parts ...`, `[FTG] preview-reason: show=...` |
| `TableEditorChecks.ShareMenu` | Prints the items of the settings editor's Share menu for `-ftTable` and whether each can be pressed; `-ftDirty 1` makes an unsaved change first (both items are off then) | `[FTG] share-menu: <item>=on\|off \| ...` |
| `TableEditorChecks.Picker` | Prints the blendshape list for `-ftPart` and `-ftSearch` | `[FTG] picker: ...` |
| `TableEditorChecks.Capture` | Opens the window and writes it to a PNG (`-ftOut`). Run with `scripts/unity-gui.sh`, not batchmode. Arguments: `-ftTable`, `-ftTab avatar\|preview\|devices` (devices prints `[FTG] capture devices recordings=... faces=<vr\|desktop>:<face id> rows=... faint=... first=<key> talk=<reading ids> bar=<true\|false>`; `-ftDevicesSide vr\|desktop` and `-ftDevicesFace <id>` choose the Recorded faces list's device and face; preview prints `[FTG] capture side "..."` and `[FTG] capture input <key> value=... range=... mark=<none\|over\|under\|notsent>` for the sliders that are not 0 or are marked), `-ftHelp en\|ja\|off`, `-ftState dirty\|picker\|preview\|notable`, `-ftSet "Input=value,..."`, `-ftScroll <y>`, `-ftSize 560x1000`, `-ftWindow generator`, `-ftWindow statemachine` (the State Machine window, 1100x700 with `-ftSize 1100x700`) with `-ftSmMachine <name>`, `-ftSmSelect <state>\|<from>-><to>`, `-ftSmOpen "<region>;..."` (neutral's folds) and `-ftSmEdits <jsonl>` (StateMachineEdit operations made before drawing), `-ftSmInspector <width>` (the inspector's width, as dragging the line; 260 to the window's width minus 200), `-ftSmZoom <factor>` (as the mouse wheel at the middle of the graph, after Frame All; prints `[FTG] capture: state machine machine=... device=... states=... transitions=<n> <shown transitions> menu=... selected=... of=<vr\|desktop\|both> stopVisemes=... strengths=... scroll=... inspector=... zoom=...`), `-ftSmDevice vr\|desktop` (the input mode; a capture never reads or writes the user's input mode), `-ftSmSwitch vr\|desktop` (presses the input mode's button after selecting), `-ftSmScroll <y>` (the inspector's scroll), `-ftSmDuration <s>` (types the selected transition's Duration) with `-ftSmUndo 0\|1\|2` (then nothing, Undo, or Undo and Redo), `-ftSmContextAt <x>,<y>` (as right-clicking that point of the graph area, in pixels from its top-left after Frame All and `-ftSmZoom`, and choosing **Create State**; prints `[FTG] capture: context click at <x>,<y> empty=<true|false>`, and a point on a state or the Menu box adds nothing), `-ftPreviewMenu "<state machine>=<state>"` (the Preview's Menu), `-ftHelpAfter en\|ja\|off` (changes the language after the windows open), `-ftBuildBegin 1` / `-ftBuildEnd 1` (call the VRChat SDK's build-requested and postprocess-avatar dispatchers; the windows pause and resume, and `[FTG] capture: frame=<20|35|55> building=... preview-running=... preview-scene-avatars=<n> sdk-avatars=<n>` lines are printed before, during and after the build: avatar descriptors in preview scenes (Face View's copy has none) and the active avatars the SDK panel offers to build, counted by the SDK's own search), `-ftAvatarAddPart <name>` (as + Add Face Movement), `-ftAvatarRename "<part>=<new name>"` (opens the rename panel; written as `<out>.popup.png`), `-ftAvatarNewPart <name>` (opens the + Add Face Movement panel with the name typed; written as `<out>.popup.png`), `-ftAvatarNewPartIn <region>` (presses the **+** of the region's heading; prints `[FTG] capture: new part text=<region>. cursor=<n> select=<n>` while it puts the caret at the end; written as `<out>.popup.png`), `-ftAvatarFold "<region>;..."` (clicks the arrow of each region's band in Face movements; the folded regions are kept in `EditorPrefs` per project and settings file, so they stay after Unity restarts; clicking the same regions again unfolds them and removes the key), `-ftFaceView 1` (also writes `<out>.faceview.png`), `-ftSelectPart <part>`, `-ftPickerPart <part> -ftHoverShape <shape>`, `-ftOpenTableEditor 1` (with `-ftWindow generator`), `-ftDevice <id>` (the Preview's Device), `-ftShowSwitches 1`, `-ftOpenScene <scene>` (opened before the window), `-ftChooseTable <avatar.jsonc>\|none` (settings editor) and `-ftChooseAvatar <Hierarchy path>\|none` (the **Avatar in scene** field of the settings editor, or of the generator window with `-ftWindow generator`), which choose as the fields do and are remembered for the project, and `-ftAvatarName <name>` (types in the generator window's **Avatar name**, also remembered). Each run also prints what the windows show of the project's remembered choices (`ProjectSelection`, kept in `UserSettings/Kmnk.FaceTrackingGenerator.asset`): `[FTG] capture: selection window=table table=<path\|none> avatar=<Hierarchy path\|none> source=<saved\|prefab\|selected\|chosen\|none> preview=<true\|false> folded=<region;...\|none>`, and for the generator window `window=generator table=... avatar=... source=... attach=<true\|false> name=<avatar name>` (whether Add Settings Component can be pressed, and the Avatar name). `-ftGenPress check` presses Check in the generator window and prints `[FTG] capture: generator message type=<info\|warning\|error\|none> step=<n>`; `-ftAvatarFill 1` presses Fill from Prefab in the Avatar tab (and `-ftAvatarFillUndo 1` then Undo); every run prints `[FTG] capture: avatar fill=<bool> afk=<n>/6 dirty=<bool> subMenu=<name\|null>` and `[FTG] capture: preview overlaps=<n> label-width=<px>`; the State Machine line ends with `dirty=<bool>`; `-ftWindow inspector -ftInspectorTable <avatar.jsonc>\|missing\|none` draws the Inspector of a new object with `FtGeneratorSettings` and prints `[FTG] capture: inspector table=<found\|missing\|none>`. Starting Unity again for each run checks what a user sees after reopening the project | `[FTG] capture: wrote <png>` |
| `TableEditorChecks.Live` | Checks the Preview's Live input (`LiveInput.cs`) without the window: parses packets made in the check (a bundle as VRCFaceTracking sends, a plain message, a nested bundle, a bundle with other messages, a 101-byte blob, broken packets); with `-ftKeys <file>` (`<index> <name>` per line, the key order of VRChat's OSC face tracking docs) compares `NativeKeys` with it; with `-ftPort <port> -ftExpect "Key=value,..." [-ftSeconds 20]` receives until those values arrive (send them with `scripts/send-face-blob.py`), prints the face `FaceEval` computes for `-ftTable` from them, and checks that a port another socket holds is refused and that the port opens again after Stop. Exit 1 when one fails | `[FTG] live: parse cases=9 failed=0`, `[FTG] live: keys=102 docs=102 mismatched=0`, `[FTG] live: received ... failed=0`, `[FTG] live: busy-port refused=true reopen=true`, `[FTG] live: failed=0` |
| `TableEditorChecks.MakeTwoAvatarScene` | Saves a scene (`-ftOut`, under `Assets`) with two avatars made from the prefab of `-ftTable` (the second named `<prefab name> 2`), for checking which avatar the settings editor brings back. Delete it after the check | `[FTG] scene: wrote <path> avatars=<a>,<b>` |

`TableEditorChecks.Capture -ftLive <port>` starts the Preview's Live input after the window opens and prints `[FTG] capture: live state=<stopped|waiting|receiving|error> port=... messages=... per-second=... ignored=... applies=... seconds=... caption=... inputs=<key=value,...> faceview=<shape=weight,...>` before the grab (without `-ftLive` it prints the port the field shows). With `-ftLiveEnd close|reload|build|none` it closes the settings editor at frame 40 (`close`), reads the table again as Reload does (`reload`; `build` relies on `-ftBuildBegin 1`; `none` does nothing, so `port-free=false`), prints `[FTG] capture: live end=... window-open=... live-running=... port-free=<bool>` at frame 45 and exits without a grab.

`scripts/send-face-blob.py --port <port> --seconds <s> --set "Key=value,..." [--rate 60] [--form bundle|plain|nested]` sends `/tracking/face/v1` (102 bytes, `round(value x 255)`, other keys 0) to `127.0.0.1:<port>` in an OSC bundle as VRCFaceTracking does, for checking the Live input on a Mac without VRCFaceTracking. Run it in the background and stop it with `pkill -f send-face-blob.py`. It prints `send-face-blob: sent <n> packets to 127.0.0.1:<port>`.

`scripts/test-table-save.sh <project> <avatar.jsonc> <work dir> [<edits.jsonl>]` copies a set of tables to `<work dir>` and checks that saving them unchanged keeps the `Cli.Load` output and that a second save changes no byte. Use a work dir outside the project: Unity deletes the project's `Temp/` when it opens.

`scripts/unity-gui.sh <project> <method> [args...]` starts the editor with graphics (not batchmode) and has the same stops as `unity-batch.sh`. The editor opens on the screen for about a minute; do not type while it runs (keys go to Unity). It rewrites `ProjectSettings/EditorBuildSettings.asset` (the XR loader GUID); restore it with `git checkout` after the run.

### Other checks in the checks package

| Method | What it does | Success |
| --- | --- | --- |
| `WindowRepro.Run -ftRepro missing-undo\|reload\|switch-undo\|state-face -ftTable <avatar.jsonc> [-ftTable2 <avatar.jsonc>] [-ftPart <part>]` | Repeats settings editor operations (Missing and Undo; an unsaved edit and a domain reload; Undo after opening another table; a state's face then a row). Run with `scripts/unity-gui.sh` | `[FTG] repro <name>: ...` lines with the part's shape, dirty, the table name and the state shown |
| `WindowRepro.ManualBake -ftAvatar <prefab>` | NDMF's Manual Bake on an instance of the prefab | `[FTG] repro manual-bake: errors=<n> result=... fx=... faceTracking=<bool>` |
| `TableWriterRepro.CommentInValue -ftTable <avatar.jsonc>` | Saves AFK clips into a copy whose `afkClips` has a comment with `{` | `[FTG] repro comment-in-value: wrote=true valid=true changed=true` |
| `FillCompare.Run -ftAvatar <prefab>` | Fills two copies of the templates, as `Cli.FillFromAvatar` and as the Avatar tab's Fill from Prefab and Save, and compares them as JSON | `[FTG] fill-compare: same=true` |
| `CharacterizationSetup.Make` / `MakeMore` | Makes the avatars and tables of the behaviour baseline's cases (once: running it again makes new GUIDs) | `[FTG] characterization setup: <id> table=... avatar=...` |

### Building the package

```
scripts/build-package.sh <version> [<dev project>]   # or set FTG_DEV_PROJECT
```

Writes the version into `package.json` of the development copy, copies `Packages/dev.kmnk.face-tracking-generator/` (and the checks package, which is not zipped) here, stages them, and zips the staged files of the generator to `build/dev.kmnk.face-tracking-generator-<version>.zip` (`package.json` at the root of the zip). Set `FTG_DENYLIST` to a file of names that must not be published (avatar, blendshape and project names, one per line; keep it in the private development project): the script then stops with exit 3 and removes the zip when a tracked file or the zip contains one. Before pushing, also search `git log -p` for them.

### Releasing

1. Commit and push the work branch, then fast-forward `main` to it: `git push origin <work branch>:main`. `main` is the default branch and holds only released commits, so the repository page shows the README of the latest release; work between releases stays on the work branch
2. The user runs `gh release create v<version> build/dev.kmnk.face-tracking-generator-<version>.zip` (development versions: `--prerelease --title "<version> (development version)"`). The tag goes to `main` (the default target). Creating a release publishes it, so the user runs it
3. The user rebuilds the listing: `gh workflow run build-listing.yml -R kmnk/vpm-repos`. It reads every release of this repository and adds zips that have `package.json` at their root
4. Check: `curl -s https://kmnk.github.io/vpm-repos/index.json` lists the version

### Evidence

Paste the `unity-batch:` lines, the `[FTG]` lines and the `git status --short` part of the output into plans and reports.

### Cleanup

Records written with `-ftOut` stay where they were written. Delete `Assets/XR/` and `Assets/XR.meta` after Play Mode runs.
