# VRChat_FaceTrackingGenerator

Unity Editor tool that generates face tracking (FT) layers for VRChat avatars from text tables.

- Reads three JSON tables (input / expression / avatar) and builds FX layers, clips, BlendTrees, smoothing and the FT part of Expression Parameters.
- Adds an AFK animation layer driven by a menu flag, and a button to add the FT toggle and AFK flag to the Expressions Menu.
- Works on a copy of the avatar's controllers. The original controllers are not changed.

Status: under development. Not ready for use.

## Install

1. Install the dependencies with VCC / ALCOM / vrc-get:
   - Animator As Code
   - NDMF
2. Import `VRChat_FaceTrackingGenerator_<version>.unitypackage`.
3. Open `Tools/Kmnk/Face Tracking Generator`.

## Settings

`Assets/Kmnk/FaceTrackingGenerator/Templates/` has simple templates of the three tables.
Copy them to `Assets/Kmnk/FaceTrackingGenerator/Settings/` and edit the copies.
`Settings/` is not included in the package, so importing a new version does not overwrite it.

## License

MIT. See `LICENSE`.
