# VRChat_FaceTrackingGenerator

Face Tracking Generator (FTG) is a Unity Editor tool that builds face tracking (FT) for VRChat avatars from two settings files.

日本語のマニュアル (Japanese manual): [docs/manual-ja/README.md](docs/manual-ja/README.md). The package also has it as offline HTML (`Documentation~/manual-ja/index.html`), which the generator window's **Manual** opens. This README is the English reference: it covers everything, including the settings file format.

## What it does

- **Face**: the expression table maps face tracking keys (such as `EyeClosedLeft`) to face movements (such as `eye.eyeBlinkLeft`), and the avatar table maps face movements to the avatar's blendshapes. FTG writes them into the generated avatar's Face Tracking section as Blendshapes Sets, so VRChat moves the blendshapes directly. The default template moves the 52 perfect sync (ARKit) blendshapes.
- **States**: state machines switch between states such as joy, by face tracking key values or from the menu. A state can override regions of the face, turn them off, and hold fixed face movements.
- **FX layers**: FTG builds FX layers for the state machines, the fixed face movements, and the weights of the Blendshapes Sets (FT on/off, AFK, talking, states).
- **Input**: the keys the transitions compare are forwarded to the Animator (Forward to Animator). FT works with the desktop Selfie Expression and with face tracking devices through VRCFaceTracking (2026-11-02 or later). Values are not synced: every client computes them from VRChat's face tracking data.
- **Menu and AFK**: an AFK animation layer driven by a menu toggle. The FT toggle, the AFK toggle and the state submenus go into a copy of the avatar's menu.
- **Your avatar stays as it is**: FTG works on copies of the avatar's controllers and menu.

## Install

FTG is a VPM package (`dev.kmnk.face-tracking-generator`). Install it with VCC, ALCOM or vrc-get.

1. Add the repository of NDMF and Modular Avatar: `https://vpm.nadena.dev/vpm.json`.
   FTG needs NDMF, which is installed with it. Modular Avatar is not required.
2. Add the FTG repository: `https://kmnk.github.io/vpm-repos/index.json`
   ([Add to VCC](vcc://vpm/addRepo?url=https://kmnk.github.io/vpm-repos/index.json)).
3. Close the project in Unity, then add **Face Tracking Generator** to the project from the package list.
4. Open `Tools/Kmnk/Face Tracking Generator`.

To update, choose a newer version in the package list. Your settings are kept (see [Settings files](#settings-files)).

Coming from an older version? See [Upgrading](#upgrading).

## Getting started

The `Tools/Kmnk/Face Tracking Generator` window lists the steps in order.

- **Help** at the top switches the descriptions between English and Japanese, or hides them.
- The result of the last button you pressed shows under its step, in an info, warning or error box. Pressing another button replaces it.
- The window reads the settings files again when they change, so Check always sees what the settings editor saved.

### 1. Create the FT settings

Type the avatar name in **Avatar name** and press **Create**.

- `Assets/Kmnk/FaceTrackingGenerator/Settings/<name>/` gets `avatar.jsonc` and `expression.jsonc`, copied from the templates.
- **Create from Folder…** takes `expression.jsonc` from a folder exported with **Share** in the settings editor instead (see [Share](#share)).
- Either way, `avatar.jsonc` starts with no face movements assigned. The movements the expressions use show as *Not set* in the Avatar tab.
- When settings of that name already exist, nothing is made and the window says so. Choose them in **Settings file** instead.
- The project remembers the name you typed last.

### 2. Edit the settings

Press **Open Settings Editor**. In its Avatar tab:

1. Choose the avatar prefab and press **Fill from Prefab**. It fills the face mesh, the menu, the face movements whose blendshape has the same name, and the empty AFK clips from the avatar's own AFK layers. Undo puts them back.
2. Choose a blendshape for each face movement that is still *Not set*.
3. Under **Menu**, check the names of the FT and AFK toggles and of the submenu that holds them. New settings use `Face Tracking`. An empty submenu name puts the toggles and state submenus directly in the menu, next to the avatar's own controls.
4. Under **AFK animation** (optional), check the clips played while AFK. Only the two Loop clips are required; empty Intro and Outro clips are skipped.
5. Check the face in the Preview tab and press **Save**.

Change each state in the State Machine window, or edit `expression.jsonc` in a text editor. The comments in the templates explain each item.

### 3. Check the settings

Press **Check**. It looks at the saved settings, and at the generated FX controller when there is one. When it reports findings, fix them in the settings editor. Movements left **Not set** are not findings, but Check reports when no movement has a blendshape at all, because then nothing moves.

### 4. Add the settings component to the avatar

Choose your avatar in **Avatar in scene** (selecting it in the Hierarchy also works) and press **Add Settings Component**. The avatar is remembered with the settings file and the scene.

FT is generated when you enter Play Mode and when you upload:

- The avatar itself is not changed: its controllers, menu, Expression Parameters, Blueprint ID and other edits stay as they are.
- The generated avatar has Face Tracking and Use All Tracking Keys turned on in its Avatar Descriptor, with one Forward to Animator behavior for each key the transitions compare. The avatar's own Face Tracking settings are kept.
- FT runs while VRChat has a face tracking source (`UserFaceTracking`). Without one, the avatar's own expressions come back.
- When neutral moves the eyelids (`EyeClosedLeft` or `EyeClosedRight`), the generated avatar's Eye Look gets Eyelids Type Face Tracking, so Eye Look's own eyelids do not close on top of FT's. Eye Look still moves the eyes.

The component's Inspector shows its settings file, warns when it has none, and opens it in the settings editor.

### The generated menu

Generating puts the FT toggle, the AFK toggle and the state submenus into a copy of the avatar's menu (the menu chosen in the Avatar tab, or the root menu), and adds their parameters to a copy of the Expression Parameters.

- A control goes in only together with its parameter, and a parameter only for a control the menu has. When the menu is full or the parameters would go over 256 bits, the log says what was left out.
- A toggle or submenu the avatar's menu already has is not added again.
- The AFK toggle is added only when AFK is built (both Loop clips are needed).

### Advanced

- **Generate** writes the controllers to `Assets/FaceTrackingGeneratorOutput/<name>/` and makes a Prefab Variant that uses them, for checking the result without changing your avatar.
- **Scenario check** plays a scenario file (`.txt`) on the generated Prefab Variant in Play Mode and records the face. It is for checking FTG itself.

### The output folder

The output folder (`outputFolder`, `Assets/FaceTrackingGeneratorOutput/<name>/` by default) belongs to FTG.

- Generating stops when the folder holds the avatar's own prefab, controllers, menus or Expression Parameters.
- Only menus FTG wrote are removed from it.
- Build-time generation writes to `Assets/FaceTrackingGeneratorOutput/<name>/Build/` on every Play Mode and upload (the state submenus under `Build/Menus/`). You can add it to `.gitignore`:

```
/Assets/FaceTrackingGeneratorOutput/*/Build/
/Assets/FaceTrackingGeneratorOutput/*/Build.meta
```

## Windows

### Settings editor

`Tools/Kmnk/Face Tracking Settings Editor` edits the settings and shows the face they give on your avatar, without a text editor. It opens the same settings file as the generator window.

- Each Unity project remembers the settings file you chose last there, in the project's `UserSettings` folder (which git usually ignores).
- The top row has **Settings file**, **Reload**, **Save** and **Share**; the row below has **Face view** and **State Machine**.
- **Help** switches the descriptions between English and Japanese, or hides them.

#### Avatar tab

- **Prefab** and **Face mesh**: the avatar and its face. **Fill from Prefab** fills them, the menu, the face movements whose blendshape has the same name, and the empty AFK clips.
- **Menu**: the menu that gets the toggles, the names of the FT and AFK toggles, and an optional submenu name.
- **AFK animation**: the six clips (Action and FX, each Intro, Loop and Outro).
- **Face movements**: pick the blendshape of each face movement from a searchable list, grouped by region.
  - A movement with a state in its name, such as `eye.joy.tear`, shows the state as a tag. Each row shows what moves it.
  - A movement left **Not set** is not used. The **Face movements** heading shows how many there are.
  - **Scale** (0 to 1, default 1) corrects a blendshape that is too strong on this avatar, without changing the shared expression table. It is written as `{ "shape": ..., "scale": 0.7 }`.
  - The list also has the Float and Range properties of the face mesh's materials, under a heading per material. They are for movements used only as fixed face movements (see [Material properties](#material-properties)).
  - **+ Add Face Movement** adds a movement (`region.movement` or `region.state.movement`) and opens its blendshape list. The **+** at the right of a region's heading does the same with `<region>.` typed.
  - The trash button removes a movement no state uses. Right-click a movement and choose **Rename…** to rename it in both tables.
- **Layers**: what happens to the avatar's own layers while FT is on.
  - **Stop while FT**: for hand gesture layers (the ones that change the face with hand signs).
  - **Mute while FT**: for layers that move the face without any input, such as a layer that plays a blink animation all the time. Otherwise that blinking goes on top of the FT eyelids.
  - Layers moved only from the menu can be left as they are.

Press **Save** to write the table.

#### Preview tab

Select your avatar in the scene, press **Start preview**, and move the sliders. No Play Mode is needed, but face movements need blendshapes first. The avatar is not changed; **Stop preview** puts it back.

- **Avatar in scene**: remembered for the settings file and the scene (save the scene once). In a scene where you have not chosen one, the only avatar made from the settings' prefab is chosen; with two or more, choose one yourself. The preview does not start by itself.
- **Sliders**: the face tracking keys (`key:<name>`). They move the face and change the states. They always go from 0 to 1.
- **Device**: shows the range a device sends under each slider, in yellow when the slider is outside it and in orange for a key the device does not send. The list has VIVE Focus Vision, Meta Quest Pro and the desktop Selfie Expression (as they are said to send), **VR (records)** and **Desktop (records)** when the settings have ft-measure recordings, and **None** to use the settings as they are. A box above the sliders says whether the preview runs as VR or as desktop, which decides the transitions and curves it uses (the box says "the transitions and strengths").
- **Menu**: chooses a state as the menu does. The keys then do not change it; **None** goes back.
- **Try expressions**: sets the sliders to expressions that break easily. The line under it says how many face movements add up on the same part of the face, and the warnings under the sliders show them, with buttons to choose again or not use one.
- **Weights**: what moves each blendshape (a key in neutral or in a state, or a state's fixed amount).

#### Live input

On the Preview tab, **Live input** moves the sliders with your own face.

1. Run VRCFaceTracking with your device, without VRChat.
2. Set VRCFaceTracking's **Send Port** to the port here. The default, 9000, is VRCFaceTracking's default too.
3. Press **Start**.

FTG receives VRChat's native face tracking input (`/tracking/face/v1`) on `127.0.0.1` and puts the values into the sliders, the states, Weights, the avatar in the scene (while previewing) and Face view (also without the preview).

- While it runs, the sliders, Try expressions and Reset cannot be used; Menu and Device can.
- The line under the button says whether values arrive and how many messages could not be read.
- It stops with **Stop**, when the window closes, on Play Mode, and when the VRChat SDK builds.
- If you changed the Send Port, set it back before running VRChat.

#### Devices tab

The Devices tab shows the range of values your device sends, from ft-measure recordings (https://github.com/kmnk/ft-measure) of your face: one for VR and one for desktop. The recordings are only for looking; the output does not use them.

- **Add VR recording** / **Add Desktop recording** copy a `.jsonl` file to `Measurements/` next to `avatar.jsonc`.
- **Recorded faces** lists, for a recorded face (**Face**), each key's smallest to largest value with the neutral face, while talking with a neutral face and with a smile (when the recording has those readings), and in that face. The keys that move most above the neutral face come first, and those that hardly move are faint. Use it to choose keys and thresholds.
- **VR** / **Desktop** is the same switch as the State Machine window's **Input**; pressing one switches both.

#### Share

**Share** copies the expression table between settings, so you can take the expressions to an avatar in another project, adjust them there, and bring them back.

- **Export expression…** copies `expression.jsonc` of the open settings to a folder you choose.
- **Import expression…** overwrites it with a folder's copy, after saying whether it would change.
- The avatar table (blendshapes, Scale, prefab) is never exported or overwritten. Movements the imported expressions use that the avatar table does not have show as *Not set* in the Avatar tab.
- Save or discard your changes before exporting or importing.

#### Saving

- Saving rewrites the settings files in their own layout, and `//` comments in them are removed. Keep a copy if you want your comments, or edit those files in a text editor only.
- Unsaved edits stay when you enter Play Mode or when scripts compile, unless the files changed on disk meanwhile (the Console says so).
- Undo goes back only to when the settings file was opened or read again.

### State Machine window

Open it with the button next to Face view, or `Tools/Kmnk/Face Tracking State Machine`. It shows the expression table's state machines as a graph, like Unity's Animator.

**State machines**

- The dropdown at the top left chooses the state machine to show, and has **New State Machine…**, **Rename "<name>"…** and **Delete "<name>"…**.
- Several state machines run side by side, each with its own menu. For example, `Emotion` for the expressions and `Cheek` with the menu states blush and pale (fixed face movements), so that blush can go with any expression.
- Two state machines should not override the same region (Check reports it).

**States**

- The first state (orange) is the default. **Menu** has a dashed line to each state selectable from the menu.
- Select a state to edit it on the right. Its face shows in Face view.
  - **Selectable from menu**.
  - **Stop visemes**: while in the state, speaking does not move the mouth with visemes, and the fixed face movements stay while talking (see [Stop visemes](#stop-visemes)).
  - For each region: **Neutral**, **Override** or **Off**. Override moves the region with the rows you write, one per key: a face movement and its **Curve**, or **Don't move**. Keys you do not write move as in neutral; **×** puts a key back to neutral. **Copy from** copies another state's rows. Off stops the region's neutral.
  - **Fixed face movements**: held while in the state (0 to 100). Dragging a slider changes the table when you release it.
- Selecting the default state shows neutral's rows by region. Right-click a region to add or remove a key.
- Right-click a state for **Make Transition** (then click the state to go to), **Set as Default** and **Delete State**.

**Transitions**

- Select a transition (a line) to edit its duration and conditions.
- A condition compares a face tracking key (such as `MouthCornerPullLeft`) with a value.
- All the conditions of a transition must hold. For "either of two" (the left or the right side), make two transitions between the same states; they are drawn side by side.

**Curves**

A row's **Curve** is how much the movement moves (up) for the key's value (right). Click it to edit it in Unity's curve editor; its presets at the bottom work too. Its highest point is the most the row moves (see [Response curves](#response-curves)).

**Devices**

**Input** at the top right chooses the device you edit (**VR** or **Desktop**, kept for your Unity), and the band under the toolbar says which. The graph shows that device's transitions, and a state's curves and Stop visemes are that device's. Editing a value both devices share changes it for the device you edit only (see [VR and desktop](#vr-and-desktop)).

**Cost**

**Face tracking cost** at the bottom shows the Parameter Behaviors, the expression instructions and the Face Tracking Operations the settings make, with the performance rank: red over VRChat's limits, yellow over Excellent.

**View**

- Drag states to move them. The mouse wheel zooms at the pointer; **Frame All** or **F** fits the view (zooming out if needed); Alt-drag or the middle button moves it.
- Drag the line between the graph and the settings on the right to widen the settings (kept for your Unity).
- Edits are unsaved changes of the settings editor: undo them, or press **Save** (shown here with **Unsaved changes**, or in the settings editor).
- When no settings are open, **Open Settings Editor** opens them.

### Face view

`Tools/Kmnk/Face Tracking Face View` shows the face of the prefab, apart from the scene.

- While you move through the blendshape list, it shows the face with that blendshape at 100. Select a row to see its blendshape.
- During the preview, it shows the preview's face.
- Drag to turn the face.

### While the VRChat SDK builds

The settings editor, Face view and the State Machine window pause (the preview stops) and resume when the build is done. Press **Resume** if they stay paused after a failed build.

NDMF's **Manual Bake** does not work with FTG: it stops with an error and the baked avatar has no face tracking. Use Play Mode or the upload to check the generated avatar.

## Settings files

`Packages/dev.kmnk.face-tracking-generator/Templates/` has templates of the two tables. Your copies are in `Assets/Kmnk/FaceTrackingGenerator/Settings/`, outside the package: updating the package replaces its folder in `Packages/` and does not touch them. Do not put your own files in `Packages/dev.kmnk.face-tracking-generator/`; they are removed on update.

### The templates

- **Neutral**: moves the 52 perfect sync (ARKit) blendshapes, as face movements named `<region>.<ARKit name>` (for example `eye.eyeBlinkLeft`, `jaw.jawOpen`). **Fill from Prefab** fills their blendshapes from the avatar's blendshapes with the same name (the last part of the name), ignoring capitals.
- **State machine `Emotion`**: the empty states joy, angry and sad, all selectable from the menu. Add overrides and fixed movements for your avatar in the State Machine window. The transitions show three ways to enter a state:

| State | Enters | Leaves |
| --- | --- | --- |
| joy | both `MouthCornerPull` keys over 0.8 | either under 0.6 |
| sad | both `MouthFrown` keys over 0.6 (VR) or 0.2 (desktop) | either under 0.4 (VR) or 0.1 (desktop) |
| angry | from the menu only | from the menu only |

The values are a start: read your own ft-measure recordings in the Devices tab and change them.

How perfect sync, VRChat's Face Tracking section and FTG correspond, with sources: `Packages/dev.kmnk.face-tracking-generator/Documentation~/perfect-sync.md`. `scripts/check-perfect-sync.py` checks the template against it.

### Expression table

```jsonc
"neutral": { "eye": { "EyeClosedLeft": "eye.eyeBlinkLeft", "EyeSquintLeft": { "part": "eye.eyeSquintLeft", "strength": 0.8 } } },
"stateMachines": {
  "Emotion": {
    "states": {
      "neutral": {},
      "joy": { "menu": true, "regions": { "eye": { "EyeClosedLeft": "eye.joy.eyeBlinkLeft", "EyeClosedRight": null } }, "fixed": { "eye.joy.tear": 60 } }
    },
    "transitions": [
      { "from": "neutral", "to": "joy", "when": [["MouthCornerPullLeft", ">", 0.8], ["MouthCornerPullRight", ">", 0.8]], "duration": 0.2 },
      { "from": "joy", "to": "neutral", "when": [["MouthCornerPullLeft", "<", 0.6]], "duration": 0.2 },
      { "from": "joy", "to": "neutral", "when": [["MouthCornerPullRight", "<", 0.6]], "duration": 0.2 }
    ]
  }
}
```

- `neutral` is region → tracking key → face movement.
- A state's `regions` override regions (`null` is Don't move) or turn them off (`"off"`). `fixed` holds face movements at an amount from 0 to 100. `"menu": true` makes the state selectable from the menu.
- A transition's `when` lists conditions that must all hold. Keys are those of the Avatar Descriptor's Face Tracking section (`v2/` may be written before the name).

What FTG builds from it:

- One Blendshapes Set per region (`FTG/neutral/<region>`), and one per region a state overrides (`FTG/<state machine>/<state>/<region>`: neutral's rows of the region, with the state's rows in place of the same keys).
- The multiplier of a row is its `strength` (expression table, 0 to 1) × the movement's `scale` (avatar table). A fixed amount is multiplied by the `scale` too.
- FX layers: `FT/Sets/*` switch the sets' weights (FT on/off, AFK, talking, states), `FT/SM/<state machine>` runs the transitions, and `FT/Face` holds the fixed amounts.
- A state machine with menu states gets an Int parameter `FT/Menu/<state machine>` and a submenu `Menus/<state machine>.asset` in the output folder, with one toggle per menu state. Generating puts the submenu into the generated menu, next to the FT toggles. While a state is chosen in the menu the transitions do not run; turning it off goes back to the default state.
- Each key a transition compares is forwarded to the Animator as `FT/In/<key>` (Forward to Animator). Over 10 compared keys, the avatar is no longer Excellent in Forwarded Parameters.

### Response curves

A row can have a response curve instead of a strength (from 0.20.0):

```jsonc
"EyeClosedLeft": { "part": "eye.eyeBlinkLeft", "curve": [{ "time": 0, "value": 0 }, { "time": 0.5, "value": 0.4, "inTangent": 1.6, "outTangent": 1.6 }, { "time": 1, "value": 0.8 }] }
```

- The keys are Unity AnimationCurve keys. `time` is the tracking key's value and `value` how much the movement moves, both from 0 to 1. The first key's `time` must be 0 and the last key's 1.
- `inTangent` / `outTangent`, `leftTangent` / `rightTangent` (`Auto`, `ClampedAuto`, `Free`, `Linear`, `Constant`), `inWeight` / `outWeight` and `weightedMode` work as in Unity's curve editor, which the State Machine window opens.
- The curve's highest point is the row's strength, so a row has `"curve"` or `"strength"`, not both. A straight curve from (0, 0) to (1, s) is written as `"strength": s`.
- `{"vr": [...], "desktop": [...]}` writes a curve per device.
- The movement's `scale` multiplies the curve.

How a curve is built:

- A blendshape moved by a curve is not a Blendshapes Set row. FTG moves it with a Parameter Behavior (Apply to Blendshape, a Custom expression that follows the curve within 0.01 of a blendshape's 0 to 1) for each Blendshapes Set it belongs to, weighted by that set. States and devices switch curves as they switch sets.
- The other rows on the same blendshape (other tracking keys) go into the same expression, and the largest moves it.
- One expression holds about 22 segments of a curve (VRChat allows 1024 characters, 256 tokens and 256 instructions). When a blendshape's curves do not fit, the generation log and Check name the rows and how far the expression is from the curve.
- Generating stops when the Face Tracking section would have over 128 Parameter Behaviors or 2048 expression instructions (VRChat skips the rest), naming the sets and blendshapes that take the most. Over 2,000 Face Tracking Operations (no longer Excellent), it only says so.
- Parts of two regions on one blendshape with a curve are an error, because a behavior follows one set.

### Material properties

A face movement can move a Float or Range property of the face mesh's materials instead of a blendshape. Write `"property"` in place of `"shape"` in `avatar.jsonc`: `{ "property": "_MyWeight" }`, with `scale` if needed.

- Such a movement moves only as a state's fixed amount: 0 to 100 becomes the Range's minimum to maximum (0 to 1 for a Float), written as an animation of the material property.
- Used in neutral or in an override, the row is dropped, and the load log and Check say so (an override row becomes Don't move).
- This lets a shader draw the face from the state, for example by choosing the eyes and mouth from the properties' values.

### Stop visemes

A state with `"stopVisemes": true` stops the avatar's visemes while FT is on and the state is active. Fixed movements of `stopWhileTalking` regions stay while talking in that state.

- FTG switches Lip Sync from Viseme Blend Shape to Viseme Blend Shape Face Tracking, with the same Face Mesh and viseme blendshapes. It puts them in the Blendshapes Set `FTG/visemes` (weight 1), and the FX layer `FT/Visemes` sets its weight to 0 in those states. In other states, and with FT off, the mouth moves as before.
- An avatar already in Viseme Blend Shape Face Tracking keeps its own viseme sets, and those are stopped instead.
- With another Lip Sync mode, or when one of the avatar's viseme sets also has tracking rows, `stopVisemes` is ignored and the generation log says why (`notice: stopVisemes ...`).

### Avatar table options

- **`overwriteFaceTracking`**: when the avatar's Face Tracking section already has its own values, they are kept and FTG's sets are not written, unless `avatar.jsonc` has `"overwriteFaceTracking": true`. Viseme rows and the avatar's Forward to Animator are kept even then.
- **`eyeGazeMode`** (default `"FaceTracking"`): sets Eye Look's Eye Gaze Mode. Choose `"Both"` or `"BoneRotation"` for avatars that look with eye bones.

### Limits

- In one region, a key moves one blendshape.
- Values from several keys on one blendshape are not added; the largest wins.
- There is no smoothing of the key values.

## VR and desktop

The expression table can write some values per device (from 0.18.0). Values not written per device are the same on VR and desktop. Edits in the State Machine window write to the device chosen in **Input**.

- **Transitions**: a transition with `"device": "vr"` or `"device": "desktop"` runs on that device only (VRChat's built-in `VRMode`). Editing or removing a transition of both devices in the State Machine window splits it: the other device keeps it as it was.
- **Strength**: a row's `"strength"` can be `{"vr": 1, "desktop": 0.5}`. So can its `"curve"`. When even one row differs between the devices, FTG makes every Blendshapes Set twice, for VR (the same name) and for desktop (`FTG/desktop/...`), switched by `VRMode`: the number of Blendshapes Sets doubles and Face Tracking Operations grow. Check reports when they go over the limits.
- **Curves**: `{"vr": [...], "desktop": [...]}` (see [Response curves](#response-curves)).
- **Stop visemes**: a state's `"stopVisemes"` can be `"vr"` or `"desktop"`. The visemes stop, and the fixed face movements stay while talking, on that device only.
- **Shared**: states, menus, the rows' keys and movements, and the fixed face movements.

## Upgrading

- **From the `.unitypackage` (0.4.x and earlier)**: close the project in Unity and add the package as in [Install](#install). VCC, ALCOM and vrc-get remove the old `Editor/`, `Runtime/` and `Templates/` folders and the `VERSION`, `FILES.txt` and `LICENSE.txt` files in `Assets/Kmnk/FaceTrackingGenerator/`. `Settings/` is kept, and the settings component on your avatars keeps working.
- **0.12.0** changed the expression table: tables with `trees`, `switches` or `stops` (1D and 2D expressions, curves, stops), inputs with `smoothing` or `average`, and avatar tables with `stopAutoEyes` or `gaze` do not load. Make the tables again from the templates.
- **0.12.1** removed the input table: `input.jsonc` is not read (an avatar table's `"tables": { "input": ... }` is ignored, and the log says so), the Inputs tab and the device's missing keys are gone, and a condition naming an input such as `Smile` stops with an error. Write the keys in the conditions, or make the tables again from the templates.
- **Before 0.17.0**, an "Add toggles to menu" button wrote the toggles into the avatar's own menu. Remove those controls by hand if you no longer want them there.
- **0.15.0 to 0.17.0** computed the other device's thresholds and strengths from the recordings and `"measurementsMain"`. 0.18.0 does not (the log warns about `"measurementsMain"`), so write the desktop values yourself. A table that writes nothing per device makes the same output as 0.17.0, with or without recordings. Versions before 0.18.0 ignore `"device"` and run such a transition on both devices.
- **Before 0.20.0**, versions ignore `"curve"` and move such a row as `"strength": 1`.
- `sync` in older settings is ignored.
- VRCFaceTracking before 2026-11-02 sends its values as avatar parameters, which FTG no longer reads. Update VRCFaceTracking.

## License

MIT. See `LICENSE`.
