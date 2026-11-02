# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables.

- Reads three JSON tables (input / expression / avatar) and builds FX layers, clips, BlendTrees, smoothing and the FT part of Expression Parameters.
- Adds an AFK animation layer driven by a menu flag, and a button to add the FT toggle and AFK flag to the Expressions Menu.
- Works on a copy of the avatar's controllers. The original controllers are not changed.

Status: under development. Not ready for use.

## Install

1. Install Modular Avatar with VCC / ALCOM / vrc-get (add the repository `https://vpm.nadena.dev/vpm.json`).
   NDMF, which this tool uses for build-time generation, comes with it.
2. Import `VRChat_FaceTrackingGenerator_<version>.unitypackage`.
3. Open `Tools/Kmnk/Face Tracking Generator`.

## Use

The `Tools/Kmnk/Face Tracking Generator` window lists the steps in order. **Help** at the top switches the descriptions between English and Japanese, or hides them.

1. **Create the FT settings**: type the avatar name and press "Create Settings/<name>". `Assets/Kmnk/FaceTrackingGenerator/Settings/<name>/` gets `avatar.jsonc`, `expression.jsonc` and `input.jsonc` copied from the templates.
2. **Choose your avatar**: select your avatar in the Hierarchy and press "Fill prefab, mesh and menu from it".
3. **Assign blendshapes to the face movements**: press "Open Settings Editor" and choose a blendshape for each face movement (see [Settings editor](#settings-editor)). You can also edit `avatar.jsonc` in a text editor. Edit `expression.jsonc` and `input.jsonc` in a text editor when you want a different look or device; the comments in the templates explain each item.
4. **Check the settings**: press "Check" and fix the settings until it reports no findings. "Response curves" shows the weights for each input without generating.
5. **Add the menu toggles**: set the toggle names (and a submenu if the menu is full), press "Save names", then "Add toggles to menu".
6. **AFK animation** (optional): pick the clips and press "Save AFK clips". Only the two Loop clips are required; empty Intro and Outro are skipped.
7. **Attach to the avatar**: with your avatar selected, press "Add settings component (build-time FT)". Face tracking is generated when you enter Play Mode and when you upload. The avatar itself (its controllers, Blueprint ID and other changes) is not changed.

Under "Advanced", "Generate" writes the controllers to `Assets/FaceTrackingGeneratorOutput/<name>/` and makes a Prefab Variant that uses them, for checking the result without changing your avatar.

Build-time generation writes to `Assets/FaceTrackingGeneratorOutput/<name>/Build/` on every Play Mode and upload. You can add it to `.gitignore`:

```
/Assets/FaceTrackingGeneratorOutput/*/Build/
/Assets/FaceTrackingGeneratorOutput/*/Build.meta
```

## Settings editor

`Tools/Kmnk/Face Tracking Settings Editor` edits the avatar settings (`avatar.jsonc`) and shows the face the settings give on your avatar, without a text editor. It opens the same settings file as the generator window.

- **Avatar** tab: choose the prefab, the menu and the face mesh; pick the blendshape of each face movement from a searchable list (each row shows which values move it); check **Missing** for movements your avatar has no shape for; choose what happens to the avatar's own layers while FT is on. Press **Save** to write the table.
- **Preview** tab: select your avatar in the scene, press **Start preview**, and move the sliders. No Play Mode is needed, but face movements need blendshapes first. **Device** lists the values VIVE Focus Vision or Meta Quest Pro is said to send. **Try expressions** sets the sliders to expressions that break easily, and a warning shows face movements that add up on the same part of the face, with buttons to choose again or not use one. The avatar is not changed; **Stop preview** puts it back.
- **Face view** (`Tools/Kmnk/Face Tracking Face View`): the face of the prefab, apart from the scene. While you move through the blendshape list, it shows the face with that blendshape at 100; select a row to see its blendshape; during the preview it shows the preview's face. Drag to turn the face.
- **Help** switches the descriptions between English and Japanese, or hides them.

Saving rewrites the settings file in its own layout, and `//` comments in it are removed. Keep a copy if you want your comments, or edit that file in a text editor only. The expression and input settings (`expression.jsonc`, `input.jsonc`) are edited in a text editor.

## Settings

`Assets/Kmnk/FaceTrackingGenerator/Templates/` has simple templates of the three tables.
Your copies in `Assets/Kmnk/FaceTrackingGenerator/Settings/` are not included in the package, so importing a new version does not overwrite them.

## License

MIT. See `LICENSE`.
