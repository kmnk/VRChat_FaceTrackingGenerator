# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables.

- Reads two JSON tables (expression / avatar). The expression table is a neutral face (tracking keys moving face movements) and state machines (states such as joy, with the regions they override or turn off and fixed face movements). The face goes into the generated avatar's Face Tracking section as Blendshapes Sets, so VRChat moves the blendshapes without Forward to Animator. The default template moves the 52 perfect sync (ARKit) blendshapes this way.
- Builds FX layers for the rest: the state machines (moved by face tracking key values or chosen from the menu), the states' fixed face movements, and the weights of the Blendshapes Sets (FT on/off, AFK, talking, states).
- Forwards the face tracking keys the transitions compare to the Animator in the Face Tracking section (Forward to Animator), so FT moves with the desktop Selfie Expression and with face tracking devices through VRCFaceTracking (2026-11-02 or later). The values are not synced: every client makes them from VRChat's face tracking data.
- Adds an AFK animation layer driven by a menu toggle, and puts the FT toggle, the AFK toggle and the state submenus into a copy of the avatar's menu (the avatar's own menu is not changed).
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

The `Tools/Kmnk/Face Tracking Generator` window lists the steps in order. **Help** at the top switches the descriptions between English and Japanese, or hides them. Each button's result shows right under it: a plain note when it is done, a yellow warning when there is something to look at, a red error when it failed. The window reads the settings files again when they change, so Check always sees what the settings editor saved.

1. **Create the FT settings**: type the avatar name and press **Create**. The project remembers the name you typed last. `Assets/Kmnk/FaceTrackingGenerator/Settings/<name>/` gets `avatar.jsonc` and `expression.jsonc` copied from the templates. **Create from Folder…** copies `expression.jsonc` from a folder exported with **Share** in the settings editor (see [Settings editor](#settings-editor)) instead. Either way, `avatar.jsonc` starts with no face movements assigned: the movements the expressions use show as *Not set* in the Avatar tab. When settings of that name already exist, both buttons make nothing and say so; choose them in **Settings file** instead.
2. **Edit the settings**: press **Open Settings Editor**. In its Avatar tab, choose the avatar prefab and press **Fill from Prefab** (it fills the face mesh, the menu, the face movements whose blendshape has the same name and the empty AFK animations; Undo puts them back), then choose a blendshape for each face movement that is still *Not set*. Under **Menu**, name the FT and AFK toggles and, if you want them in a submenu, type its name (empty: directly in the menu). **AFK animation** (optional) takes the clips played while AFK; only the two Loop clips are required, and empty Intro and Outro are skipped. Check the face in the Preview tab and press **Save**. Change each state's movements in the State Machine window, or edit `expression.jsonc` in a text editor; the comments in the templates explain each item.
3. **Check the settings**: press **Check**. When it reports findings, read them and fix the settings in the settings editor. It looks at the saved settings and at the generated FX controller when there is one.
4. **Add the settings component to the avatar**: choose your avatar in **Avatar in scene** (selecting it in the Hierarchy also chooses it) and press **Add Settings Component**. The avatar is remembered with the settings file and the scene, as in the settings editor. Face tracking is generated when you enter Play Mode and when you upload. The avatar itself (its controllers, menu, Expression Parameters, Blueprint ID and other changes) is not changed. The generated avatar has Face Tracking and Use All Tracking Keys turned on in its Avatar Descriptor, with one Forward to Animator behavior per tracking key the transitions compare; the avatar's own Face Tracking settings are kept. FT runs while VRChat has a face tracking source (`UserFaceTracking`); without one, the avatar's own expressions come back. When neutral moves the eyelids (`EyeClosedLeft` or `EyeClosedRight`), the generated avatar's Eye Look gets Eyelids Type Face Tracking, so Eye Look's own eyelids do not close on top of FT's; Eye Look still moves the eyes. The component's Inspector shows its settings file, warns when it has none, and opens it in the settings editor.

Generating puts the FT toggle, the AFK toggle and the state submenus into a copy of the avatar's menu (the menu chosen in the Avatar tab, or the root menu) and adds their parameters to the copied Expression Parameters. A control goes in only with its parameter, and a parameter only for a control the menu has: when the menu is full or the parameters would go over 256 bits, the log says what was left out. A toggle or submenu the avatar's menu already has is not added again, and the AFK toggle is not added when AFK is not built (both Loop clips are needed). Before 0.17.0 an "Add toggles to menu" button wrote them into the avatar's own menu: remove those controls by hand if you no longer want them there.

Under "Advanced", **Generate** writes the controllers to `Assets/FaceTrackingGeneratorOutput/<name>/` and makes a Prefab Variant that uses them, for checking the result without changing your avatar.

Under "Advanced", **Scenario check** plays a scenario file (`.txt`) on the generated Prefab Variant in Play Mode and records the face; it is for checking the generator.

The output folder (`outputFolder`, by default `Assets/FaceTrackingGeneratorOutput/<name>/`) is the generator's own: generating stops when it holds the avatar's own prefab, controllers, menus or Expression Parameters, and only menus the generator wrote are removed from it. Build-time generation writes to `Assets/FaceTrackingGeneratorOutput/<name>/Build/` on every Play Mode and upload (the state submenus too, under `Build/Menus/`). You can add it to `.gitignore`:

```
/Assets/FaceTrackingGeneratorOutput/*/Build/
/Assets/FaceTrackingGeneratorOutput/*/Build.meta
```

## Settings editor

`Tools/Kmnk/Face Tracking Settings Editor` edits the avatar settings (`avatar.jsonc`) and shows the face the settings give on your avatar, without a text editor. It opens the same settings file as the generator window. Each Unity project remembers the settings file you chose last there, so choosing one in another project does not change it (the choice is kept in the project's `UserSettings` folder, which git usually ignores).

- **Avatar** tab: choose the prefab and the face mesh (**Fill from Prefab** fills them, the menu, the face movements whose blendshape has the same name and the empty AFK animations); under **Menu**, the menu, the names of the FT and AFK toggles and an optional submenu name; under **AFK animation**, the six clips; pick the blendshape of each face movement from a searchable list, grouped by region (a movement with a state in its name, such as `eye.joy.tear`, shows the state as a tag; each row shows what moves it); a movement left **Not set** is not used (Check says how many there are); set **Scale** (0 to 1, default 1) to correct a blendshape that is too strong on this avatar without changing the shared expression table (written as `{ "shape": ..., "scale": 0.7 }`); the list also has the Float and Range properties of the face mesh's materials, under a heading per material, for movements used only as a state's fixed amount (see below); **+ Add Face Movement** adds a movement (`region.movement` or `region.state.movement`) and opens its blendshape list, and the **+** at the right of a region's heading does the same with `<region>.` typed; the trash button removes a movement no state uses; right-click a movement and choose **Rename…** to rename it in both tables; choose what happens to the avatar's own layers while FT is on. Press **Save** to write the table.
  - The avatar's own layers: check **Stop while FT** for hand gesture layers (the ones that change the face with hand signs), and **Mute while FT** for layers that move the face in their default state without any input, such as a layer that plays a blink animation all the time. Otherwise that blinking keeps going on top of the FT eyelids. Layers moved only from the menu can be left as they are.
- **Preview** tab: select your avatar in the scene, press **Start preview**, and move the sliders. The avatar you choose is remembered for the settings file and the scene, and comes back when you open them again (the scene has to be saved once). In a scene where you have not chosen one, the only avatar made from the settings' prefab is chosen; with two or more, choose one yourself. The preview does not start by itself. No Play Mode is needed, but face movements need blendshapes first. The sliders are the tracking keys (`key:<name>`): they move the face and change the states. **Device** (**None** to use the settings as they are) lists the values VIVE Focus Vision, Meta Quest Pro or the desktop Selfie Expression is said to send, and **VR (records)** / **Desktop (records)** when the settings have ft-measure recordings (see **Devices**): the sliders always go from 0 to 1, and the line under each slider shows the device's range, in yellow when the slider is outside it and in orange for a key the device does not send. A box above the sliders says whether the preview runs as VR or as desktop: it then uses that device's transitions and strengths. **Menu** (above the sliders) chooses a state as the menu does (the keys then do not change it; **None** goes back). **Try expressions** sets the sliders to expressions that break easily; the line under it says how many face movements add up on the same part of the face, and the warnings under the sliders show them, with buttons to choose again or not use one. **Weights** shows what moves each blendshape (a key in neutral or in a state, or a state's fixed amount). The avatar is not changed; **Stop preview** puts it back.
- **Live input** (on the Preview tab): moves the sliders with your own face. Run VRCFaceTracking with your device and do not run VRChat; set VRCFaceTracking's **Send Port** to the port here (9000, VRCFaceTracking's default, needs no change) and press **Start**. The tool receives VRChat's native face tracking input (`/tracking/face/v1`) on `127.0.0.1` and puts the values into the sliders, the states, Weights, the avatar in the scene (while previewing) and Face view (also without the preview). While it runs, the sliders, Try expressions and Reset cannot be used; Menu and Device can. The line under the button says whether values arrive and how many messages could not be read. It stops with **Stop**, when the window closes, on Play Mode and when the VRChat SDK builds. If you changed the Send Port, set it back before running VRChat.
- **Devices** tab (to see the range of values your device sends): ft-measure recordings (https://github.com/kmnk/ft-measure) of your face, one for VR and one for desktop. **Add VR recording** / **Add Desktop recording** copy a `.jsonl` file to `Measurements/` next to `avatar.jsonc`. **Recorded faces** lists, for a recorded face (**Face**), each key's smallest to largest value with a neutral face, while talking with a neutral face, while talking with a smile and in that face, the keys that move most above the neutral face first (those that hardly move are faint): use it to choose keys and thresholds. Its **VR** / **Desktop** is the State Machine window's input mode (pressing one switches both). The recordings are only for looking: the output does not use them.
- **State Machine** (the button next to Face view, or `Tools/Kmnk/Face Tracking State Machine`): the expression table's state machines as a graph, like Unity's Animator. The dropdown at the top left chooses the state machine to show and has **New State Machine…**, **Rename "<name>"…** and **Delete "<name>"…**. Several state machines run side by side, each with its own menu: for example `Emotion` for the expressions and `Cheek` with menu states blush and pale (fixed face movements), so that blush can go with any expression. Two state machines should not override the same region (Check reports it). The first state (orange) is the default; **Menu** has a dashed line to each state selectable from the menu. **Input** at the top right chooses the device you edit (**VR** or **Desktop**, kept for your Unity; the band under the toolbar says which): the graph shows that device's transitions, and a state's strengths and **Stop visemes** are that device's; the rows' keys and movements and the fixed face movements are shared. Editing a value both devices share changes it for the device you edit only (see VR and desktop). Select a state to edit it on the right: **Selectable from menu**; **Stop visemes** (while in the state, speaking does not move the mouth with visemes and the fixed face movements stay while talking; see below); for each region **Neutral**, **Override** (the region moves with the rows you write, by tracking key: a part and its **Curve**, or **Don't move**; keys you do not write move as in neutral, and **×** puts a key back to neutral; **Copy from** copies another state's rows) or **Off** (the region's neutral stops); and **Fixed face movements** held while in the state (0 to 100; dragging a slider changes the table when you release it). Selecting the default state shows neutral's rows by region (right-click a region to add or remove a key). A row's **Curve** is how much the movement moves (up) for the key's value (right): click it to edit it in Unity's curve editor (its presets at the bottom work too); its highest point is the strength (see Response curves). **Face tracking cost** at the bottom shows the Parameter Behaviors, the expression instructions and the Face Tracking Operations (with the performance rank) the settings make, red over VRChat's limits and yellow over Excellent. Selecting a state shows its face in Face view. Right-click a state for **Make Transition** (then click the state to go to), **Set as Default** and **Delete State**; select a transition (a line) to edit its duration and conditions. A condition compares a face tracking key (such as `MouthCornerPullLeft`) with a value; all the conditions of a transition must hold, so for either of two (the left or the right side) make two transitions between the same states (drawn side by side). Drag states to move them; the mouse wheel zooms at the pointer, **Frame All** or **F** fits the view (zooming out if needed), and Alt-drag or the middle button moves it. Drag the line between the graph and the settings on the right to widen the settings (kept for your Unity). Edits are unsaved changes of the settings editor: undo them, or press **Save** (here, shown with **Unsaved changes**, or in the settings editor). When no settings are open, **Open Settings Editor** opens them.
- While the VRChat SDK builds an avatar, the settings editor, Face view and the State Machine window pause (the preview stops) and resume when the build is done; press **Resume** if they stay paused after a failed build.
- **Face view** (`Tools/Kmnk/Face Tracking Face View`): the face of the prefab, apart from the scene. While you move through the blendshape list, it shows the face with that blendshape at 100; select a row to see its blendshape; during the preview it shows the preview's face. Drag to turn the face.
- **Share** (next to Reload and Save, on the row of the Settings file; Face view and State Machine are on the row below): **Export expression…** copies `expression.jsonc` of the open settings to a folder you choose, and **Import expression…** overwrites it with a folder's copy, after saying whether it would change. The avatar table (blendshapes, Scale, prefab) is never exported or overwritten, so you can take the expressions to an avatar in another project, adjust them there, and bring them back. Movements the imported expressions use that the avatar table does not have show as *Not set* in the Avatar tab. Save or discard your changes before exporting or importing.
- **Help** switches the descriptions between English and Japanese, or hides them.

Saving rewrites the settings files in their own layout, and `//` comments in them are removed. Keep a copy if you want your comments, or edit those files in a text editor only. Unsaved edits stay when you enter Play Mode or when scripts compile (unless the files changed on disk meanwhile; the Console says so). Undo goes back only to when the settings file was opened or read again.

NDMF's **Manual Bake** does not work with this tool: it stops with an error and the baked avatar has no face tracking. Use Play Mode or the upload to check the generated avatar.

## Settings

`Packages/dev.kmnk.face-tracking-generator/Templates/` has templates of the two tables. The default expression table moves the 52 perfect sync (ARKit) blendshapes in neutral, as face movements named `<region>.<ARKit name>` (e.g. `eye.eyeBlinkLeft`, `jaw.jawOpen`); **Fill from Prefab** fills their blendshapes from the avatar's blendshapes with the same name (the last part of the name), without regard to capitals. It also has the state machine `Emotion` with the empty states joy, angry and sad, moved by the tracking keys `MouthCornerPull*`, `BrowLowerer*` and `MouthFrown*` (left or right over 0.5 enters, both under 0.4 leaves) and selectable from the menu: add overrides and fixed movements for your avatar in the State Machine window. How perfect sync, VRChat's Face Tracking section and the generator correspond, with sources: `Packages/dev.kmnk.face-tracking-generator/Documentation~/perfect-sync.md` (`scripts/check-perfect-sync.py` checks the template against it).
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

`neutral` is region -> tracking key -> face movement. The generator writes one Blendshapes Set per region (`FTG/neutral/<region>`) and one per region a state overrides (`FTG/<state machine>/<state>/<region>`: neutral's rows of the region, with the state's rows in place of the same keys; `null` is Don't move). The multiplier is the row's `strength` (expression table, 0 to 1) x the movement's `scale` (avatar table); a fixed amount is multiplied by the `scale` too. The FX layers `FT/Sets/*` switch the sets' weights (FT on/off, AFK, talking, states), `FT/SM/<state machine>` runs the transitions, and `FT/Face` holds the fixed amounts. A state machine with menu states gets an Int parameter `FT/Menu/<state machine>` and a submenu `Menus/<state machine>.asset` in the output folder (one toggle per menu state); generating puts the submenu into the generated menu, next to the FT toggles. While a state is chosen in the menu the transitions do not run; turning it off goes back to the default state.

### Response curves

A row can have a response curve instead of a strength (from 0.20.0):

```jsonc
"EyeClosedLeft": { "part": "eye.eyeBlinkLeft", "curve": [{ "time": 0, "value": 0 }, { "time": 0.5, "value": 0.4, "inTangent": 1.6, "outTangent": 1.6 }, { "time": 1, "value": 0.8 }] }
```

The keys are Unity AnimationCurve keys: `time` is the tracking key's value and `value` how much the movement moves, both 0 to 1, from `time` 0 to `time` 1; `inTangent` / `outTangent`, `leftTangent` / `rightTangent` (`Auto`, `ClampedAuto`, `Free`, `Linear`, `Constant`), `inWeight` / `outWeight` and `weightedMode` as in Unity's curve editor, which the State Machine window opens. The curve's highest point is the row's strength, so a row has `"curve"` or `"strength"`, not both; a straight curve from (0, 0) to (1, s) is written as `"strength": s`. `{"vr": [...], "desktop": [...]}` writes a curve per device. The part's `scale` multiplies the curve.

A blendshape moved by a curve is not a Blendshapes Set row: the generator moves it with a Parameter Behavior (Apply to Blendshape, a Custom expression that follows the curve within 0.01 of a blendshape's 0 to 1) for each Blendshapes Set it belongs to, weighted by that set, so states and devices switch curves as they switch sets. The other rows on the same blendshape (other tracking keys) go into the same expression, and the largest moves it. One expression holds about 22 segments of a curve (VRChat allows 1024 characters, 256 tokens and 256 instructions); when a blendshape's curves do not fit, the generation log and Check name the rows and how far the expression is from the curve. Generating stops when the Face Tracking section would have over 128 Parameter Behaviors or 2048 expression instructions (VRChat skips the rest), naming the sets and blendshapes that take the most; over 2,000 Face Tracking Operations (no longer Excellent) it only says so. Parts of two regions on one blendshape with a curve are an error (a behavior follows one set). Versions before 0.20.0 ignore `"curve"` and move such a row as `"strength": 1`.

When the avatar's Face Tracking section already has its own values, they are kept and the generator's sets are not written, unless `avatar.jsonc` has `"overwriteFaceTracking": true` (viseme rows and the avatar's Forward to Animator are kept even then). `eyeGazeMode` (default `"FaceTracking"`) sets Eye Look's Eye Gaze Mode; choose `"Both"` or `"BoneRotation"` for avatars that look with eye bones. Limits accepted: in one region a key moves one blendshape, values from several keys are not added (the largest wins), and the movement is proportional to the key (no curves, no smoothing).

A face movement can move a Float or Range property of the face mesh's materials instead of a blendshape: write `"property"` in place of `"shape"` in `avatar.jsonc` (`{ "property": "_MyWeight" }`, with `scale` if needed). Such a movement moves only as a state's fixed amount: 0 to 100 becomes the Range's minimum to maximum (0 to 1 for a Float), written as an animation of the material property. Used in neutral or in an override, the row is dropped and the load log and Check say so (an override row becomes Don't move). This lets a shader draw the face from the state, for example by choosing the eyes and mouth from the properties' values.

A state with `"stopVisemes": true` stops the avatar's visemes while FT is on and the state is active (fixed movements of `stopWhileTalking` regions stay while talking in that state). The generator switches Lip Sync from Viseme Blend Shape to Viseme Blend Shape Face Tracking with the same Face Mesh and viseme blendshapes, puts them in the Blendshapes Set `FTG/visemes` (weight 1), and the FX layer `FT/Visemes` sets its weight to 0 in those states; in other states and with FT off the mouth moves as before. An avatar already in Viseme Blend Shape Face Tracking keeps its own viseme sets, and those are stopped instead. With another Lip Sync mode, or when one of the avatar's viseme sets also has tracking rows, `stopVisemes` is ignored and the generation log says why (`notice: stopVisemes ...`).

A transition's conditions compare face tracking keys of the Avatar Descriptor's Face Tracking section (`v2/` may be written before the name). The generator forwards each compared key to the Animator as `FT/In/<key>` (Forward to Animator); over 10 compared keys, the avatar is no longer Excellent in Forwarded Parameters.

0.12.1 removed the input table: `input.jsonc` is not read (an avatar table's `"tables": { "input": ... }` is ignored, and the log says so), the Inputs tab and the device's missing keys are gone, and a condition naming an input such as `Smile` stops with an error. Write the keys in the conditions, or make the tables again from the templates.

0.12.0 changed the expression table: tables with `trees`, `switches` or `stops` (1D and 2D expressions, curves, stops), inputs with `smoothing` or `average`, and avatar tables with `stopAutoEyes` or `gaze` do not load. Make the tables again from the templates.

`sync` in older settings is ignored. VRCFaceTracking before 2026-11-02 sends its values as avatar parameters, which this tool no longer reads; update VRCFaceTracking.

## VR and desktop

The expression table can write some values per device; values not written per device are the same on VR and desktop, and edits in the State Machine window's input mode write them (from 0.18.0).

- A transition with `"device": "vr"` or `"device": "desktop"` runs on that device only (VRChat's built-in `VRMode`). Editing or removing a transition of both devices in the State Machine window splits it: the other device keeps it as it was.
- A row's `"strength"` can be `{"vr": 1, "desktop": 0.5}`. Then the generator makes each Blendshapes Set twice, for VR (the same name) and for desktop (`FTG/desktop/...`), switched by `VRMode`. Face Tracking Operations and the number of Blendshapes Sets about double; Check reports when they go over the limits.
- A state's `"stopVisemes"` can be `"vr"` or `"desktop"`: the visemes stop, and the fixed face movements stay while talking, on that device only.
- States, menus, the rows' keys and movements and the fixed face movements are shared.

A table that writes nothing per device makes the same output as 0.17.0, with or without recordings. 0.15.0 to 0.17.0 computed the other device's thresholds and strengths from the recordings and `"measurementsMain"`; 0.18.0 does not (the log warns about `"measurementsMain"`), so write the desktop values yourself. Versions before 0.18.0 ignore `"device"` and run such a transition on both devices.

## Changes in 0.20.1

- The State Machine window: the selected **VR** / **Desktop** button of **Input** is brown inside its border, without the blue line on the left of **Desktop** and the darker top edge

## Changes in 0.20.0

- Response curves: a row of `neutral` or of a state's override can have `"curve"` (see Response curves), edited in the State Machine window's **Curve** column with Unity's curve editor. Tables without curves make the same output as 0.19.0
- The State Machine window shows **Face tracking cost** (Parameter Behaviors, expression instructions, Face Tracking Operations and the rank); its toolbar's buttons look like buttons, and **Unsaved changes** and **Input** sit on the buttons' middle line
- The generation log has a `face tracking cost:` line, and Check also reports over the limits, over Excellent and curves that one expression cannot follow

## Changes in 0.19.0

The windows:

- The generator window has four steps: create, edit (in the settings editor), check, attach. Fill, the toggle names and the AFK clips moved to the settings editor's Avatar tab (**Fill from Prefab**, **Menu**, **AFK animation**), where Undo takes them back and **Save** writes them; the generator window no longer writes the settings files and has no Reload
- The settings editor keeps unsaved edits when you enter Play Mode or scripts compile, and Undo no longer brings back another settings file's contents after you open it or read it again. After choosing a state in the State Machine window, a row you select stays in Face view after edits
- The State Machine window has **Save**, a hint when nothing is selected, and **Fixed face movements**; the component's Inspector shows the settings file and opens it
- The generator window no longer has **Response curves** (a straight line per blendshape for one tracking key; the Preview tab shows the same with the states and the device)
- The windows keep only the text you need where you are: the gray descriptions that repeated the labels are gone, the meaning of a field is in its tooltip (hover over it), and the details are here; the generator window has a **Manual** link to this page
- **Create** and **Create from Folder…** make nothing when settings of that name exist, and say so (before, they failed with an error)
- Preview: the line under **Try expressions** says how many face movements add up; **Menu** is above the sliders; **Device** starts with **None**; the slider names are not cut
- The settings editor's Avatar tab shows a gray hint in the empty **Submenu name**; the `Tools/Kmnk` menu lists the windows in the order you use them
- The windows say settings file, expression settings and State Machine for the same things everywhere; the State Machine window's **+ Add Blendshape** is **+ Add Face Movement**, and the Inspector's **Avatar table (avatar.jsonc)** is **Settings file**
- The generator window shows each result under the button you pressed, as a note, a warning or an error, in the Help language; the summary line at the top and the result box at the bottom are gone. When the settings file is missing or cannot be read, a warning under **Settings file** says so and steps 2 to 4 cannot be pressed
- The Devices tab says what recordings are for when there are none; the State Machine window's hint when nothing is selected and while choosing where a transition goes show also with Help Off, and **Open Settings Editor** opens the settings when none are open
- The settings component's **?** in the Inspector opens this page

Generating (the generated files can change for the tables named here):

- The FX controller no longer gets an unused `IsLocal` parameter
- The menu and Expression Parameters agree: a toggle or state submenu goes in only with its parameter, a parameter only for a control the menu has, `FT/Menu/*` counts toward the 256 bits, and there is no AFK toggle when AFK is not built (both Loop clips are needed). For an avatar whose menu is full, `FT/Menu/<state machine>` is no longer added to Expression Parameters
- The build-time output writes the state submenus under `Build/Menus/`, so Play Mode no longer rewrites `Menus/` of the files Generate writes
- AFK groups with the same wait (one without `"wait"` and the generator's own AFK group both wait 0) no longer stop generating
- A `{guid, path}` whose GUID is not in the project uses the path in Check, the menu and the AFK clips too, with a warning. The window's Generate and the build-time generation show the settings' warnings (in the Console for the build)
- A hand-written state machine name with characters other than letters, digits and `_`, or one that differs from another only in capitals, is an error when the settings are read
- Generating keeps menus in the output folder that it did not write, and stops when `outputFolder` holds the avatar's own files
- Saving the toggle names or the AFK clips from the command line never leaves a broken settings file
- Face movements left **Not set** are no longer a warning for each row that uses them: Check shows how many there are in one line and does not count them as findings, and the generation log no longer shows them (`Cli.Load` lists them as dropped)
- **Missing** is gone from the Avatar tab, and `missingParts` in `avatar.jsonc` is not read (it is not an error). Those movements stay unused, now as Not set; **Fill from Prefab** may now set them, so when a blendshape breaks an expression, fix it in that state instead (`null` for the key, another face movement, `strength`, or the region `"off"`)

## License

MIT. See `LICENSE`.
