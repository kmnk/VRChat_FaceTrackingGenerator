# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables.

- Reads two JSON tables (expression / avatar). The expression table is a neutral face (tracking keys moving face movements) and state machines (states such as joy, with the regions they override or turn off and fixed face movements). The face goes into the generated avatar's Face Tracking section as Blendshapes Sets, so VRChat moves the blendshapes without Forward to Animator. The default template moves the 52 perfect sync (ARKit) blendshapes this way.
- Builds FX layers for the rest: the state machines (moved by face tracking key values or chosen from the menu), the states' fixed face movements, and the weights of the Blendshapes Sets (FT on/off, AFK, talking, states).
- Forwards the face tracking keys the transitions compare to the Animator in the Face Tracking section (Forward to Animator), so FT moves with the desktop Selfie Expression and with face tracking devices through VRCFaceTracking (2026-11-02 or later). The values are not synced: every client makes them from VRChat's face tracking data.
- Adds an AFK animation layer driven by a menu flag, and a button to add the FT toggle and AFK flag to the Expressions Menu.
- Works on a copy of the avatar's controllers. The original controllers are not changed.

Status: under development. Not ready for use.

## Install

The tool is a VPM package (`dev.kmnk.face-tracking-generator`). Install it with VCC, ALCOM or vrc-get.

1. Add the repository of NDMF and Modular Avatar: `https://vpm.nadena.dev/vpm.json`.
   This tool needs NDMF (it is installed with the tool). Modular Avatar is not required by the tool.
2. Add this tool's repository: `https://kmnk.github.io/vpm-repos/index.json`
   ([Add to VCC](vcc://vpm/addRepo?url=https://kmnk.github.io/vpm-repos/index.json)).
3. Close the project in Unity, then add **Face Tracking Generator** to the project from the package list.
4. Open `Tools/Kmnk/Face Tracking Generator`.

To update, choose a newer version in the package list. Your settings are kept (see [Settings](#settings)).

### Moving from the `.unitypackage` (0.4.x and earlier)

Close the project in Unity and add the package as above. VCC / ALCOM / vrc-get remove the old `Editor/`, `Runtime/` and `Templates/` folders and the `VERSION`, `FILES.txt` and `LICENSE.txt` files in `Assets/Kmnk/FaceTrackingGenerator/`. `Settings/` is kept, and the settings component on your avatars keeps working.

## Use

The `Tools/Kmnk/Face Tracking Generator` window lists the steps in order. **Help** at the top switches the descriptions between English and Japanese, or hides them.

1. **Create the FT settings**: type the avatar name and press "Create Settings/<name>". The project remembers the name you typed last. `Assets/Kmnk/FaceTrackingGenerator/Settings/<name>/` gets `avatar.jsonc` and `expression.jsonc` copied from the templates. "Create Settings/<name> from a folder…" copies `expression.jsonc` from a folder exported with **Share** in the settings editor (see [Settings editor](#settings-editor)) instead. Either way, `avatar.jsonc` starts with no face movements assigned: the movements the expressions use show as *Not set* in the Avatar tab.
2. **Choose your avatar**: choose your avatar in **Avatar in scene** (selecting it in the Hierarchy also chooses it) and press "Fill prefab, mesh and menu from it". The avatar is remembered with the settings file and the scene, as in the settings editor, and step 7 uses it too.
3. **Assign blendshapes to the face movements**: press "Open Settings Editor" and choose a blendshape for each face movement (see [Settings editor](#settings-editor)). You can also edit `avatar.jsonc` in a text editor. Edit `expression.jsonc` in a text editor (or in the State Machine window) when you want a different look; the comments in the templates explain each item.
4. **Check the settings**: press "Check" and fix the settings until it reports no findings. "Response curves" shows the weights for each tracking key without generating.
5. **Add the menu toggles**: set the toggle names (and a submenu if the menu is full), press "Save names", then "Add toggles to menu".
6. **AFK animation** (optional): pick the clips and press "Save AFK clips". Only the two Loop clips are required; empty Intro and Outro are skipped.
7. **Attach to the avatar**: with your avatar selected, press "Add settings component (build-time FT)". Face tracking is generated when you enter Play Mode and when you upload. The avatar itself (its controllers, Blueprint ID and other changes) is not changed. The generated avatar has Face Tracking and Use All Tracking Keys turned on in its Avatar Descriptor, with one Forward to Animator behavior per tracking key the transitions compare; the avatar's own Face Tracking settings are kept. FT runs while VRChat has a face tracking source (`UserFaceTracking`); without one, the avatar's own expressions come back. With "Turn off Eye Look eyelids" in the Avatar tab (on in the templates), the generated avatar's Eye Look has no eyelids, so they do not close on top of FT's eyelids, and FT blinks while it is not running; Eye Look still moves the eyes.

Under "Advanced", "Generate" writes the controllers to `Assets/FaceTrackingGeneratorOutput/<name>/` and makes a Prefab Variant that uses them, for checking the result without changing your avatar.

Build-time generation writes to `Assets/FaceTrackingGeneratorOutput/<name>/Build/` on every Play Mode and upload. You can add it to `.gitignore`:

```
/Assets/FaceTrackingGeneratorOutput/*/Build/
/Assets/FaceTrackingGeneratorOutput/*/Build.meta
```

## Settings editor

`Tools/Kmnk/Face Tracking Settings Editor` edits the avatar settings (`avatar.jsonc`) and shows the face the settings give on your avatar, without a text editor. It opens the same settings file as the generator window. Each Unity project remembers the settings file you chose last there, so choosing one in another project does not change it (the choice is kept in the project's `UserSettings` folder, which git usually ignores).

- **Avatar** tab: choose the prefab, the menu and the face mesh; pick the blendshape of each face movement from a searchable list, grouped by region (a movement with a state in its name, such as `eye.joy.tear`, shows the state as a tag; each row shows what moves it); check **Missing** for movements your avatar has no shape for; set **Scale** (0 to 1, default 1) to correct a blendshape that is too strong on this avatar without changing the shared expression table (written as `{ "shape": ..., "scale": 0.7 }`); **+ Add Face Movement** adds a movement (`region.movement` or `region.state.movement`) and opens its blendshape list, and the **+** at the right of a region's heading does the same with `<region>.` typed; the trash button removes a movement no state uses; right-click a movement and choose **Rename…** to rename it in both tables; choose what happens to the avatar's own layers while FT is on. Press **Save** to write the table.
  - The avatar's own layers: check **Stop while FT** for hand gesture layers (the ones that change the face with hand signs), and **Mute while FT** for layers that move the face in their default state without any input, such as a layer that plays a blink animation all the time. Otherwise that blinking keeps going on top of the FT eyelids. Layers moved only from the menu can be left as they are.
- **Preview** tab: select your avatar in the scene, press **Start preview**, and move the sliders. The avatar you choose is remembered for the settings file and the scene, and comes back when you open them again (the scene has to be saved once). In a scene where you have not chosen one, the only avatar made from the settings' prefab is chosen; with two or more, choose one yourself. The preview does not start by itself. No Play Mode is needed, but face movements need blendshapes first. The sliders are the tracking keys (`key:<name>`): they move the face and change the states. **Device** lists the values VIVE Focus Vision or Meta Quest Pro is said to send: sliders for values the device does not send are disabled. **Menu** chooses a state as the menu does (the keys then do not change it; **None** goes back). **Try expressions** sets the sliders to expressions that break easily, and a warning shows face movements that add up on the same part of the face, with buttons to choose again or not use one. **Weights** shows what moves each blendshape (a key in neutral or in a state, or a state's fixed amount). The avatar is not changed; **Stop preview** puts it back.
- **State Machine** (the button next to Face view, or `Tools/Kmnk/Face Tracking State Machine`): the expression table's state machines as a graph, like Unity's Animator. The dropdown at the top left chooses the state machine to show and has **New State Machine…**, **Rename…** and **Delete…**. Several state machines run side by side, each with its own menu: for example `Emotion` for the expressions and `Cheek` with menu states blush and pale (fixed blendshapes), so that blush can go with any expression. Two state machines should not override the same region (Check reports it). The first state (orange) is the default; **Menu** has a dashed line to each state selectable from the menu. Select a state to edit it on the right: **Selectable from menu**; for each region **Neutral**, **Override** (the region moves with the rows you write, by tracking key: a part and a strength 0 to 1, or **Don't move**; keys you do not write move as in neutral, and **×** puts a key back to neutral; **Copy from** copies another state's rows) or **Off** (the region's neutral stops); and **Fixed blendshapes** held while in the state (0 to 100). Selecting the default state shows neutral's rows by region (right-click a region to add or remove a key). Selecting a state shows its face in Face view. Right-click a state for **Make Transition** (then click the state to go to), **Set as Default** and **Delete State**; select a transition (a line) to edit its duration and conditions. A condition compares a face tracking key (such as `MouthCornerPullLeft`) with a value; all the conditions of a transition must hold, so for either of two (the left or the right side) make two transitions between the same states (drawn side by side). Drag states to move them; the mouse wheel zooms at the pointer, **Frame All** or **F** fits the view (zooming out if needed), and Alt-drag or the middle button moves it. Drag the line between the graph and the settings on the right to widen the settings (kept for your Unity). Edits are unsaved changes of the settings editor: undo them, or press **Save** there.
- While the VRChat SDK builds an avatar, the settings editor, Face view and the State Machine window pause (the preview stops) and resume when the build is done; press **Resume** if they stay paused after a failed build.
- **Face view** (`Tools/Kmnk/Face Tracking Face View`): the face of the prefab, apart from the scene. While you move through the blendshape list, it shows the face with that blendshape at 100; select a row to see its blendshape; during the preview it shows the preview's face. Drag to turn the face.
- **Share** (next to Reload and Save, on the row of the Settings file; Face view and State Machine are on the row below): **Export expression…** copies `expression.jsonc` of the open settings to a folder you choose, and **Import expression…** overwrites it with a folder's copy, after saying whether it would change. The avatar table (blendshapes, Missing, Scale, prefab) is never exported or overwritten, so you can take the expressions to an avatar in another project, adjust them there, and bring them back. Movements the imported expressions use that the avatar table does not have show as *Not set* in the Avatar tab. Save or discard your changes before exporting or importing.
- **Help** switches the descriptions between English and Japanese, or hides them.

Saving rewrites the settings files in their own layout, and `//` comments in them are removed. Keep a copy if you want your comments, or edit those files in a text editor only.

## Settings

`Packages/dev.kmnk.face-tracking-generator/Templates/` has templates of the two tables. The default expression table moves the 52 perfect sync (ARKit) blendshapes in neutral, as face movements named `<region>.<ARKit name>` (e.g. `eye.eyeBlinkLeft`, `jaw.jawOpen`); "Fill prefab, mesh and menu" fills their blendshapes from the avatar's blendshapes with the same name (the last part of the name), without regard to capitals. It also has the state machine `Emotion` with the empty states joy, angry and sad, moved by the tracking keys `MouthCornerPull*`, `BrowLowerer*` and `MouthFrown*` (left or right over 0.5 enters, both under 0.4 leaves) and selectable from the menu: add overrides and fixed movements for your avatar in the State Machine window. How perfect sync, VRChat's Face Tracking section and the generator correspond, with sources: `Packages/dev.kmnk.face-tracking-generator/Documentation~/perfect-sync.md` (`scripts/check-perfect-sync.py` checks the template against it).
Your copies are in `Assets/Kmnk/FaceTrackingGenerator/Settings/`, outside the package. Updating the package replaces its folder in `Packages/` and does not touch them. Do not put your own files in `Packages/dev.kmnk.face-tracking-generator/`: they are removed on update.

## Expressions

```jsonc
"neutral": { "eye": { "EyeClosedLeft": "eye.eyeBlinkLeft", "EyeSquintLeft": { "part": "eye.eyeSquintLeft", "strength": 0.8 } } },
"stateMachines": {
  "Emotion": {
    "states": {
      "neutral": {},
      "joy": { "menu": true, "regions": { "eye": { "EyeClosedLeft": "eye.joy.eyeBlinkLeft", "EyeClosedRight": null }, "lip": "off" }, "fixed": { "eye.joy.tear": 60 } }
    },
    "transitions": [
      { "from": "neutral", "to": "joy", "when": [["MouthCornerPullLeft", ">", 0.5]], "duration": 0.2 },
      { "from": "neutral", "to": "joy", "when": [["MouthCornerPullRight", ">", 0.5]], "duration": 0.2 },
      { "from": "joy", "to": "neutral", "when": [["MouthCornerPullLeft", "<", 0.4], ["MouthCornerPullRight", "<", 0.4]], "duration": 0.2 }
    ]
  }
}
```

`neutral` is region -> tracking key -> face movement. The generator writes one Blendshapes Set per region (`FTG/neutral/<region>`) and one per region a state overrides (`FTG/<state machine>/<state>/<region>`: neutral's rows of the region, with the state's rows in place of the same keys; `null` is Don't move). The multiplier is the row's `strength` (expression table, 0 to 1) x the movement's `scale` (avatar table); a fixed amount is multiplied by the `scale` too. The FX layers `FT/Sets/*` switch the sets' weights (FT on/off, AFK, talking, states), `FT/SM/<state machine>` runs the transitions, and `FT/Face` holds the fixed amounts. A state machine with menu states gets an Int parameter `FT/Menu/<state machine>` and a submenu `Menus/<state machine>.asset` in the output folder (one toggle per menu state); **Add toggles to menu** in the generator window puts the submenu in the FT menu. While a state is chosen in the menu the transitions do not run; turning it off goes back to the default state.

When the avatar's Face Tracking section already has its own values, they are kept and the generator's sets are not written, unless `avatar.jsonc` has `"overwriteFaceTracking": true` (viseme rows and the avatar's Forward to Animator are kept even then). `eyeGazeMode` (default `"FaceTracking"`) sets Eye Look's Eye Gaze Mode; choose `"Both"` or `"BoneRotation"` for avatars that look with eye bones. Limits accepted: in one region a key moves one blendshape, values from several keys are not added (the largest wins), and the movement is proportional to the key (no curves, no smoothing).

A transition's conditions compare face tracking keys of the Avatar Descriptor's Face Tracking section (`v2/` may be written before the name). The generator forwards each compared key to the Animator as `FT/In/<key>` (Forward to Animator); over 10 compared keys, the avatar is no longer Excellent in Forwarded Parameters.

0.13.0 removed the input table: `input.jsonc` is not read (an avatar table's `"tables": { "input": ... }` is ignored, and the log says so), the Inputs tab and the device's missing keys are gone, and a condition naming an input such as `Smile` stops with an error. Write the keys in the conditions, or make the tables again from the templates.

0.12.0 changed the expression table: tables with `trees`, `switches` or `stops` (1D and 2D expressions, curves, stops), inputs with `smoothing` or `average`, and avatar tables with `stopAutoEyes` or `gaze` do not load. Make the tables again from the templates.

`sync` in older settings is ignored (the generation log says so). VRCFaceTracking before 2026-11-02 sends its values as avatar parameters, which this tool no longer reads; update VRCFaceTracking.

## License

MIT. See `LICENSE`.
