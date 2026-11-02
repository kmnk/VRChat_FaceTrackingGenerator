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

All steps are in the `Tools/Kmnk/Face Tracking Generator` window.

1. **Create your tables**: type the avatar name under "New settings from templates" and press "Create Settings/<name>". `Assets/Kmnk/FaceTrackingGenerator/Settings/<name>/` gets `avatar.jsonc`, `expression.jsonc` and `input.jsonc` copied from the templates.
2. **Point the tables at your avatar**: select your avatar in the Hierarchy and press "Fill prefab, mesh and menu from it".
3. **Map the parts**: open `avatar.jsonc` in a text editor and write, for each part, the blendshape name of your avatar (`"eye.close_L": "<your blendshape>"`). List parts your avatar does not have in `missingParts`. Edit `expression.jsonc` and `input.jsonc` when you want a different look or device. The comments in the templates explain each item.
4. **Check**: press "Check". Fix the tables until it reports no findings. "Response curves" shows the weights for each input without generating.
5. **Add the menu toggles**: set the toggle names (and a submenu if the menu is full) under "Menu toggles", then press "Add toggles to menu". This adds the FT toggle and the AFK flag to your avatar's Expression Parameters and menu.
6. **Choose AFK clips** (optional): pick the clips under "AFK clips" and press "Save AFK clips to the table".
7. **Attach to the avatar**: with your avatar selected, press "Add settings component (build-time FT)". Face tracking is generated when you enter Play Mode and when you upload. The avatar itself (its controllers, Blueprint ID and other changes) is not changed.

Instead of 7, "Generate" writes the controllers to `Assets/FaceTrackingGeneratorOutput/<name>/` and makes a Prefab Variant that uses them, for checking the result without changing your avatar.

Build-time generation writes to `Assets/FaceTrackingGeneratorOutput/<name>/Build/` on every Play Mode and upload. You can add it to `.gitignore`:

```
/Assets/FaceTrackingGeneratorOutput/*/Build/
/Assets/FaceTrackingGeneratorOutput/*/Build.meta
```

## Settings

`Assets/Kmnk/FaceTrackingGenerator/Templates/` has simple templates of the three tables.
Your copies in `Assets/Kmnk/FaceTrackingGenerator/Settings/` are not included in the package, so importing a new version does not overwrite them.

## License

MIT. See `LICENSE`.
