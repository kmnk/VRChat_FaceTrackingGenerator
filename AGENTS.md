# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables. This repository is public (MIT). The code lives in `Assets/Kmnk/FaceTrackingGenerator/` and is distributed as a `.unitypackage`.

## Layout

- `Assets/Kmnk/FaceTrackingGenerator/Editor/` - generator code (namespace `Kmnk.FaceTrackingGenerator`)
- `Assets/Kmnk/FaceTrackingGenerator/Templates/` - simple templates of the three tables
- `Assets/Kmnk/FaceTrackingGenerator/Settings/` - personal settings copied from the templates. Ignored here; tracked in each private avatar project
- `scripts/` - commands for development (not in the package)

## Rules for this public repository

- Do not commit blendshape names, mesh names or asset names of purchased avatars. Not in code, comments, templates or commit messages. Avatar-specific values go to `Settings/` in the avatar project
- Do not bundle other people's code. Users install Modular Avatar from VPM; the code itself uses only NDMF, which comes with it
- Keep the `.meta` GUID of every file. Move files inside Unity, not with `git mv`, so `.unitypackage` imports do not leave old files

## Development flow

The working copy of the code is an avatar project used for development (`<dev project>/Assets/Kmnk/FaceTrackingGenerator/`, on a work branch). Edit and check there, then copy to this repository with `scripts/build-package.sh`. Give the project path as an argument or in the environment variable `FTG_DEV_PROJECT`; do not write it in this repository.

## Verification

Use these commands to check changes to the generator by running it the same way a user does (Editor methods in the Unity project), on macOS.

### Before running

- Unity must run outside the Claude Code sandbox (license files under `~/Library/Unity`; inside the sandbox it stops after 60 s with exit 199)
- Close the project in the Unity editor. `scripts/unity-batch.sh` stops when `Temp/UnityLockfile` exists
- The avatar project must be on a work branch. `scripts/unity-batch.sh` stops on `main` / `master`, because those branches are uploaded from Windows
- Nothing here uploads to VRChat. The Mac has no VRChat client

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
| `Cli.Check` | Static checks of the tables and the generated FX (`-ftFx` to check another controller) | `[FTG] check: findings=0` |
| `Cli.InstallMenu` | Adds the FT toggle and the AFK flag to the avatar's own menu and Expression Parameters (changes original assets) | `[FTG] menu: ...` per parameter |
| `Cli.Curve` | Response curves without Unity's Animator (`-ftInput <name>` or `-ftQueries <jsonl>`, `-ftOut <jsonl>`) | `[FTG] curve: wrote ...` |
| `Cli.SetAfkClips`, `Cli.CreateSettings`, `Cli.FillFromAvatar`, `Cli.LeftoverFiles` | The window's AFK clip saving, settings from templates, filling prefab/mesh/menu from an avatar prefab (`-ftAvatar <prefab path>`), and leftover file cleanup | `[FTG] ...` |
| `Cli.ExportPackage` | Used by `scripts/build-package.sh` | `[FTG] package: exported ...` |

The editor window `Tools/Kmnk/Face Tracking Generator` calls the same functions. It cannot be drawn in batchmode (`-nographics`); check its drawing in the editor.

### Building the package

```
scripts/build-package.sh <version> [<dev project>]   # or set FTG_DEV_PROJECT
```

Writes `VERSION` and `FILES.txt` into the development copy, copies it here (without `Settings/`), stages it, and exports `git ls-files Assets` plus parent folders to `build/VRChat_FaceTrackingGenerator_<version>.unitypackage`. Before pushing, search the tracked files, `git log -p` and the package for the avatars' blendshape names (none may be found).

### Evidence

Paste the `unity-batch:` lines, the `[FTG]` lines and the `git status --short` part of the output into plans and reports.

### Cleanup

Records written with `-ftOut` stay where they were written. Delete `Assets/XR/` and `Assets/XR.meta` after Play Mode runs.
