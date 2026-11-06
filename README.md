# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables.

- Reads three JSON tables (input / expression / avatar) and builds FX layers, clips, BlendTrees and smoothing.
- Makes the inputs in the avatar's Face Tracking section (Forward to Animator from the face tracking keys), so FT moves with the desktop Selfie Expression and with face tracking devices through VRCFaceTracking (2026-11-02 or later). The inputs are not synced: every client makes them from VRChat's face tracking data.
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

1. **Create the FT settings**: type the avatar name and press "Create Settings/<name>". The project remembers the name you typed last. `Assets/Kmnk/FaceTrackingGenerator/Settings/<name>/` gets `avatar.jsonc`, `expression.jsonc` and `input.jsonc` copied from the templates.
2. **Choose your avatar**: choose your avatar in **Avatar in scene** (selecting it in the Hierarchy also chooses it) and press "Fill prefab, mesh and menu from it". The avatar is remembered with the settings file and the scene, as in the settings editor, and step 7 uses it too.
3. **Assign blendshapes to the face movements**: press "Open Settings Editor" and choose a blendshape for each face movement (see [Settings editor](#settings-editor)). You can also edit `avatar.jsonc` in a text editor. Edit `expression.jsonc` and `input.jsonc` in a text editor when you want a different look or device; the comments in the templates explain each item.
4. **Check the settings**: press "Check" and fix the settings until it reports no findings. "Response curves" shows the weights for each input without generating.
5. **Add the menu toggles**: set the toggle names (and a submenu if the menu is full), press "Save names", then "Add toggles to menu".
6. **AFK animation** (optional): pick the clips and press "Save AFK clips". Only the two Loop clips are required; empty Intro and Outro are skipped.
7. **Attach to the avatar**: with your avatar selected, press "Add settings component (build-time FT)". Face tracking is generated when you enter Play Mode and when you upload. The avatar itself (its controllers, Blueprint ID and other changes) is not changed. The generated avatar has Face Tracking and Use All Tracking Keys turned on in its Avatar Descriptor, with one Forward to Animator behavior per input; the avatar's own Face Tracking settings are kept. FT runs while VRChat has a face tracking source (`UserFaceTracking`); without one, the avatar's own expressions come back. With "Turn off Eye Look eyelids" in the Avatar tab (on in the templates), the generated avatar's Eye Look has no eyelids, so they do not close on top of FT's eyelids, and FT blinks while it is not running; Eye Look still moves the eyes.

Under "Advanced", "Generate" writes the controllers to `Assets/FaceTrackingGeneratorOutput/<name>/` and makes a Prefab Variant that uses them, for checking the result without changing your avatar.

Build-time generation writes to `Assets/FaceTrackingGeneratorOutput/<name>/Build/` on every Play Mode and upload. You can add it to `.gitignore`:

```
/Assets/FaceTrackingGeneratorOutput/*/Build/
/Assets/FaceTrackingGeneratorOutput/*/Build.meta
```

## Settings editor

`Tools/Kmnk/Face Tracking Settings Editor` edits the avatar settings (`avatar.jsonc`) and shows the face the settings give on your avatar, without a text editor. It opens the same settings file as the generator window. Each Unity project remembers the settings file you chose last there, so choosing one in another project does not change it (the choice is kept in the project's `UserSettings` folder, which git usually ignores).

- **Avatar** tab: choose the prefab, the menu and the face mesh; pick the blendshape of each face movement from a searchable list (each row shows which values move it); check **Missing** for movements your avatar has no shape for; **+ Add Face Movement** adds a movement (a name, and left/right shapes if you like) and opens its blendshape list, so you can list the shapes first and then use them in Tuning; the trash button removes a movement no expression uses; right-click a movement and choose **Rename…** to rename it in both tables (a left/right pair such as `eye.close_L` and `eye.close_R` can be renamed together, and `eye.close_{s}` in left/right expressions follows); choose what happens to the avatar's own layers while FT is on. Press **Save** to write the table.
  - The avatar's own layers: check **Stop while FT** for hand gesture layers (the ones that change the face with hand signs), and **Mute while FT** for layers that move the face in their default state without any input, such as a layer that plays a blink animation all the time. Otherwise that blinking keeps going on top of the FT eyelids. Layers moved only from the menu can be left as they are.
- **Preview** tab: select your avatar in the scene, press **Start preview**, and move the sliders. The avatar you choose is remembered for the settings file and the scene, and comes back when you open them again (the scene has to be saved once). In a scene where you have not chosen one, the only avatar made from the settings' prefab is chosen; with two or more, choose one yourself. The preview does not start by itself. No Play Mode is needed, but face movements need blendshapes first. **Device** lists the values VIVE Focus Vision or Meta Quest Pro is said to send: sliders for values the device does not send are disabled, and the sliders keep to the range the device sends, with the range in the name when some positions of the settings are outside it. **Try expressions** sets the sliders to expressions that break easily, and a warning shows face movements that add up on the same part of the face, with buttons to choose again or not use one. The avatar is not changed; **Stop preview** puts it back.
- **Tuning** tab: for each expression, change where its face movements start and end (for example, raise the closing position of the eyelid to 0.5 when blinks do not close fully), which face movements move at each position and how much, and add or remove positions (**+ Add a Position at Try** adds one at the **Try** value without changing the face). **+ Add face movement** adds an existing face movement or a new one (**New face movement…**); a new one shows up as *Not set* in the Avatar tab, where you pick its blendshape. A graph and the **Try** slider show the result in Face view. Expressions that move with two values (such as the mouth) have a square graph instead: click or drag in it to move **Try**, edit each position's **X** and **Y**, and add or remove positions the same way. **Smoothing** sets how fast the face catches up with the values (ms for your view and others' view). Press **Save** to write the expression and input tables. **Show: Selected** at the top of the tab shows only the node of the box selected in the expression graph, under the path to it (for example `Mouth › LipPucker 0 › MouthOpen`); **All** lists every node. Selecting a switch box shows its switch there: rename it and its states, add or remove states, and edit each transition (from, to, duration, and the conditions that must all hold). The split, insert and first-box lists also offer **Switch**: an existing switch, or **+ New Switch** (states `state1` and `state2`, to rename and edit in Tuning). At the bottom of **All**, **Switches** lists every switch with the expressions that use it: open one to edit it, add one with **+ New Switch**, and remove an unused one with its trash button (switches are kept when you remove the expressions that use them, and each one still adds a layer when generating).
- **Inputs** tab: choose which values from your device (chosen in **Device**, shared with the Preview tab) FT uses. Check a value to add it as an input (it goes where it falls in the device's list, whatever order you check values in), and uncheck an input no expression uses to remove it. Each row shows the expression that makes the value from the face tracking keys; an input without one is not used. Over 10 inputs, the avatar is no longer Excellent in Forwarded Parameters. Hover over an input's name (here, on the Preview sliders and on the Tuning headings) to see what the value is, from `Templates/parameters.jsonc`.
- **Expression graph** (`Tools/Kmnk/Face Tracking Expression Graph`, trial): the expressions of the open settings as boxes and lines, so you can see which branch a node is in. Pick an expression in the toolbar; **Frame All** fits it in the window, the wheel zooms, and the middle button moves the view. Select a box to show its node in the Tuning tab. The arrow in a box title opens the box to list each position's face movements one per line; hover over a position to read its face movements in full. Drag from the circle right of a position and release on empty space to split that position with a new 1D node on an input you choose, or a 2D node on two inputs (X, then Y); the face does not change until you edit the new node's positions. Right-click a box (or select it and press Delete) and pick **Remove Box > Keep …** to replace it with the content of one of its positions; the top box cannot be removed. **Insert Box Above…** in the same menu puts a new 1D or 2D box above any box (the top one too): the box goes to the new box's 0 position and the other positions start neutral. **+ New Expression** makes an expression from a name and its first box (check **Left and right** to make one pair, like the template's Eyelid, that moves the left side with the left inputs and the right side with the right ones; inputs written per side, such as `EyeLidLeft` and `EyeLidRight`, show as one pair in the list), and **Remove Expression** removes the chosen one (its face movements and inputs stay). With no expression left, FT can still be turned on, and nothing moves. Splits are unsaved changes of the settings editor: undo them, or press **Save** there to write them.
- While the VRChat SDK builds an avatar, the settings editor, Face view and the expression graph pause (the preview stops) and resume when the build is done; press **Resume** if they stay paused after a failed build.
- **Face view** (`Tools/Kmnk/Face Tracking Face View`): the face of the prefab, apart from the scene. While you move through the blendshape list, it shows the face with that blendshape at 100; select a row to see its blendshape; during the preview it shows the preview's face. Drag to turn the face.
- **Help** switches the descriptions between English and Japanese, or hides them.

Saving rewrites the settings file in its own layout, and `//` comments in it are removed. Keep a copy if you want your comments, or edit that file in a text editor only. The expression and input settings (`expression.jsonc`, `input.jsonc`) are edited in a text editor.

## Settings

`Packages/dev.kmnk.face-tracking-generator/Templates/` has simple templates of the three tables.
Your copies are in `Assets/Kmnk/FaceTrackingGenerator/Settings/`, outside the package. Updating the package replaces its folder in `Packages/` and does not touch them. Do not put your own files in `Packages/dev.kmnk.face-tracking-generator/`: they are removed on update.

## Inputs and expressions

Each input of `input.jsonc` names a value with `key` (`v2/<name>`, as VRCFaceTracking calls it). The tool knows how to make every value in `Templates/Devices` from the face tracking keys of the Avatar Descriptor's Face Tracking section, with the same meaning as before. To make a value of your own, or to change one, write `expression` with the key names, `Voice` and `Viseme_*`, numbers, `+ - * /`, `abs`, `min`, `max` and `clamp`:

```jsonc
"JawOpen": { "key": "v2/JawOpen", "smoothing": "default", "expression": "JawOpen * 1.2" }
```

`sync` in older settings is ignored (the generation log says so). VRCFaceTracking before 2026-11-02 sends its values as avatar parameters, which this tool no longer reads; update VRCFaceTracking.

## License

MIT. See `LICENSE`.
