# Perfect sync, VRChat and this generator

How the generator maps perfect sync (the 52 ARKit blend shapes) to VRChat's Face Tracking section, and why. The template
`Templates/expression.template.jsonc` (its `neutral`: region -> tracking key -> part) follows the table below; `scripts/check-perfect-sync.py` in
the generator repository checks that they match. When one of the three specifications changes, compare it with the
sources here and fix the rows and reasons it affects.

## The three specifications

- **Perfect sync**: an avatar has blend shapes with the 52 names of Apple ARKit's `ARFaceAnchor.BlendShapeLocation`
  (lowerCamelCase, e.g. `eyeBlinkLeft`). It comes from hinzka's reference models and article (2020-06-15); tools such as
  VMagicMirror read the names without regard to capitals.
  Sources: https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation (52 names counted from the
  page's JSON on 2026-11-12), https://hinzka.hatenablog.com/entry/2020/06/15/072929 (article of 2020-06-15, updated
  2021-12-02; read on 2026-11-12), https://malaybaku.github.io/VMagicMirror/en/tips/perfect_sync (the page has no
  version; read on 2026-11-12: 52 BlendShapeClips, capitals ignored by default, behavior not promised when names differ
  only in capitals)
- **VRChat**: the Avatar Descriptor's Face Tracking section maps 102 tracking keys (Unified Expressions names, e.g.
  `EyeClosedLeft`) to blend shapes in Blendshapes Sets (key -> blend shape and Multiplier; when several mappings move the
  same blend shape the highest value wins). VRChat does not document a perfect sync mapping; the SDK's Auto-Fill has
  candidate names per key, including ARKit names.
  Sources: https://github.com/vrchat-community/creator-docs/blob/af01805db07a0a95013601988e04f4b743a3bcd2/Docs/docs/avatars/face-tracking/index.md ,
  https://github.com/vrchat-community/creator-docs/blob/af01805db07a0a95013601988e04f4b743a3bcd2/Docs/docs/avatars/face-tracking/osc.md ,
  VRChat Avatars SDK 3.10.6 `Editor/VRCSDK/SDK3A/Components3/FaceTrackingAutoFill.cs` (read on 2026-11-12)
- **VRCFaceTracking (VRCFT)**: defines Unified Expressions and its groups (Eye Gaze, Eye, Eyebrow, Nose, Cheek, Jaw, Lip,
  Tongue, Throat/Neck), and its iPhone module writes each ARKit value into Unified Expressions keys.
  Sources: https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes ,
  https://github.com/benaclejames/VRCFaceTracking/blob/c8405b62b3176612be4dcaa8ff4f38eb8a62484f/VRCFaceTracking.Core/Params/Expressions/UnifiedExpressions.cs ,
  https://github.com/kusomaigo/VRCFaceTracking-LiveLink/blob/4326916b12b6bfd6821ae55ce85fef0fb573afdd/VRCFT-LiveLink/LiveLinkExtTrackingInterface.cs
  (read on 2026-11-12)

## The table

Part name in this generator: `<region>.<ARKit name>`. Keys: the tracking keys whose rows move the ARKit blend shape in the
Blendshapes Set `FTG/neutral/<region>` (the template's `neutral`; a state that overrides the region writes its own set
`FTG/<state machine>/<state>/<region>` from these rows). With several keys the largest value moves the blend shape.

| ARKit | Region | Tracking keys |
| --- | --- | --- |
| `eyeLookUpLeft` | `gaze` | `EyeLookUpLeft` |
| `eyeLookDownLeft` | `gaze` | `EyeLookDownLeft` |
| `eyeLookInLeft` | `gaze` | `EyeLookInLeft` |
| `eyeLookOutLeft` | `gaze` | `EyeLookOutLeft` |
| `eyeLookUpRight` | `gaze` | `EyeLookUpRight` |
| `eyeLookDownRight` | `gaze` | `EyeLookDownRight` |
| `eyeLookInRight` | `gaze` | `EyeLookInRight` |
| `eyeLookOutRight` | `gaze` | `EyeLookOutRight` |
| `eyeBlinkLeft` | `eye` | `EyeClosedLeft` |
| `eyeSquintLeft` | `eye` | `EyeSquintLeft` |
| `eyeWideLeft` | `eye` | `EyeWideLeft` |
| `eyeBlinkRight` | `eye` | `EyeClosedRight` |
| `eyeSquintRight` | `eye` | `EyeSquintRight` |
| `eyeWideRight` | `eye` | `EyeWideRight` |
| `browDownLeft` | `brow` | `BrowLowererLeft`, `BrowPinchLeft` |
| `browDownRight` | `brow` | `BrowLowererRight`, `BrowPinchRight` |
| `browInnerUp` | `brow` | `BrowInnerUpLeft`, `BrowInnerUpRight` |
| `browOuterUpLeft` | `brow` | `BrowOuterUpLeft` |
| `browOuterUpRight` | `brow` | `BrowOuterUpRight` |
| `noseSneerLeft` | `nose` | `NoseSneerLeft` |
| `noseSneerRight` | `nose` | `NoseSneerRight` |
| `cheekPuff` | `cheek` | `CheekPuffLeft`, `CheekPuffRight` |
| `cheekSquintLeft` | `cheek` | `CheekSquintLeft` |
| `cheekSquintRight` | `cheek` | `CheekSquintRight` |
| `jawOpen` | `jaw` | `JawOpen` |
| `jawForward` | `jaw` | `JawForward` |
| `jawLeft` | `jaw` | `JawLeft` |
| `jawRight` | `jaw` | `JawRight` |
| `mouthClose` | `jaw` | `MouthClosed` |
| `mouthFunnel` | `lip` | `LipFunnelUpperLeft`, `LipFunnelUpperRight`, `LipFunnelLowerLeft`, `LipFunnelLowerRight` |
| `mouthPucker` | `lip` | `LipPuckerUpperLeft`, `LipPuckerUpperRight`, `LipPuckerLowerLeft`, `LipPuckerLowerRight` |
| `mouthLeft` | `lip` | `MouthUpperLeft`, `MouthLowerLeft` |
| `mouthRight` | `lip` | `MouthUpperRight`, `MouthLowerRight` |
| `mouthSmileLeft` | `lip` | `MouthCornerPullLeft`, `MouthCornerSlantLeft` |
| `mouthFrownLeft` | `lip` | `MouthFrownLeft` |
| `mouthDimpleLeft` | `lip` | `MouthDimpleLeft` |
| `mouthStretchLeft` | `lip` | `MouthStretchLeft` |
| `mouthPressLeft` | `lip` | `MouthPressLeft` |
| `mouthLowerDownLeft` | `lip` | `MouthLowerDownLeft` |
| `mouthUpperUpLeft` | `lip` | `MouthUpperUpLeft` |
| `mouthSmileRight` | `lip` | `MouthCornerPullRight`, `MouthCornerSlantRight` |
| `mouthFrownRight` | `lip` | `MouthFrownRight` |
| `mouthDimpleRight` | `lip` | `MouthDimpleRight` |
| `mouthStretchRight` | `lip` | `MouthStretchRight` |
| `mouthPressRight` | `lip` | `MouthPressRight` |
| `mouthLowerDownRight` | `lip` | `MouthLowerDownRight` |
| `mouthUpperUpRight` | `lip` | `MouthUpperUpRight` |
| `mouthRollUpper` | `lip` | `LipSuckUpperLeft`, `LipSuckUpperRight` |
| `mouthRollLower` | `lip` | `LipSuckLowerLeft`, `LipSuckLowerRight` |
| `mouthShrugUpper` | `lip` | `MouthRaiserUpper` |
| `mouthShrugLower` | `lip` | `MouthRaiserLower` |
| `tongueOut` | `tongue` | `TongueOut` |

52 ARKit names, 68 rows.

## Why

- **Keys**: the keys SDK 3.10.6's Auto-Fill gives an ARKit-named blend shape (its candidate names, compared without
  regard to capitals), so the template moves an avatar the same way as Auto-Fill would. Auto-Fill has no candidate for
  `browInnerUp`; the template uses `BrowInnerUpLeft` and `BrowInnerUpRight`, the keys VRCFT's iPhone module writes it to.
- **The largest of several keys**: VRChat uses the highest value when several mappings move one blend shape. The iPhone
  module writes one ARKit value into all of its keys, so the largest returns the ARKit value. Devices that fill the keys
  one by one (Quest Pro, VIVE, Selfie Expression) give the largest key, not VRCFT's averages or weighted sums
  (e.g. VRCFT's `MouthSmile` = `MouthCornerPull` x 0.8 + `MouthCornerSlant` x 0.2). The docs show the "highest wins"
  rule for mappings in different Blendshapes Sets; that it also holds inside one set has not been checked in VRChat.
- **Part names `<region>.<ARKit name>`**: the part after the dot is the blend shape name, so the generator can fill the
  mapping by name (the last part of a name, also in `<region>.<state>.<name>`); the region decides the Blendshapes Set
  and which parts stop while talking. Names that differ only in capitals are refused, as VMagicMirror
  does not promise how such names behave.
- **Regions**: VRCFT's Unified Expressions groups, by the group of the keys: gaze (Eye Gaze), eye, brow, nose, cheek,
  jaw, lip, tongue. `mouthClose` is in jaw because VRCFT puts `MouthClosed` in its Jaw group. `noseSneer*` is in nose,
  although VRCFT puts `NoseSneer*` in its Lip group: the name says nose, and a nose wrinkle should not stop while talking.
  Apple's five headings (left eye, right eye, mouth and jaw, eyebrows/cheeks/nose, tongue) are not used: brows could not
  stop alone.
- **Face Tracking section, not the Animator**: VRChat recommends Blendshapes Sets and Parameter Behaviors for moving
  blend shapes and Forward to Animator only for values the Animator needs; 52 forwarded values would make Forwarded
  Parameters Very Poor (over 40). The Animator only switches the sets' weights (FT on/off, AFK, talking, the state
  machines' states) and holds the states' fixed amounts.
- **52, not 102**: perfect sync is the 52 ARKit names. Keys only in Unified Expressions (tongue directions, pupils,
  `CheekSuck`, `MouthTightener`, ...) are not in the template.

## Where the three differ

- Auto-Fill does not map `browInnerUp` (see above).
- The iPhone module does not pass some ARKit values as they are: `EyeClosed` = clamp(`eyeBlink` + `eyeBlink` x
  `eyeSquint`); `LipSuckUpper` = min(1 - `mouthUpperUp`^(1/6), `mouthRollUpper`); `EyeLook*` come from Live Link Face's
  eye yaw and pitch, not from ARKit's `eyeLook*`; its code writes the left eye's squint into `EyeSquintRight`.
- Capitals: ARKit names are lowerCamelCase, avatars often use other capitals. The generator fills the mapping without
  regard to capitals; its own checks compare blend shape names with capitals, as Unity does (generator 0.12.0:
  `Editor/AvatarSetup.cs` `FillParts`, `Editor/StaticChecker.cs` `MissingShapes` with `GetBlendShapeIndex`; checked on
  2026-11-13).
- A Blendshapes Set holds one blend shape per key, and blend shapes are not added up: a key cannot move two blend shapes
  in one region, and values are not proportional curves. The generator accepts these limits for tracking parts.
