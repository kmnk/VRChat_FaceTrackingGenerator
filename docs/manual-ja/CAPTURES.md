# Captures of the manual's images

How each image in `images/` was made, so it can be made again for a new version. This file is outside `images/`, so `scripts/manual-html.ts` does not copy it and it is not in the package's manual.

The window images come from `TableEditorChecks.Capture` (the checks package) run with `scripts/unity-gui.sh <project> Kmnk.FaceTrackingGenerator.TableEditorChecks.Capture ...` on a Unity project with an avatar and its settings file (`<table>` below). After each run, restore `ProjectSettings/EditorBuildSettings.asset` of the project with `git checkout`.

Images must not show the names of a bought avatar's blendshapes, meshes or assets. Where a window shows them, they are covered with a plain box after capturing (headless Chrome drawing boxes over the image), as listed below.

| Image | Arguments | Covered |
| --- | --- | --- |
| `generator-window.png` | `-ftWindow generator -ftTable <table> -ftHelp ja -ftSize 560x900`, top 560x335 kept | none |
| `avatar-tab.png` | `-ftTable <table> -ftTab avatar -ftHelp ja -ftSize 560x1000` | the Prefab, Face mesh and Menu fields, and every assigned Blendshape / Property cell |
| `preview-tab.png` | `-ftTable <table> -ftTab preview -ftHelp ja -ftSize 560x1000` | none (no avatar chosen, so Weights is empty) |
| `state-machine.png` | `-ftWindow statemachine -ftTable <table> -ftHelp ja -ftSmMachine Emotion -ftSmSelect joy -ftSize 1100x700` | none (face movement names of the template only) |
| `face-view-joy.png` | the Face view window that the `state-machine.png` run writes next to it (`<out>.faceview.png`) | none |
| `generator-check.png` | `-ftWindow generator -ftTable <table> -ftHelp ja -ftGenPress check -ftSize 560x900`, top 560x380 kept | none |
| `state-machine-transition.png` | `-ftWindow statemachine -ftTable <table> -ftHelp ja -ftSmMachine Emotion -ftSmSelect 'neutral->joy' -ftSize 1100x700` | none |
| `state-machine-desktop.png` | `-ftWindow statemachine -ftTable <table> -ftHelp ja -ftSmMachine Emotion -ftSmDevice desktop -ftSmSelect joy -ftSize 1100x700` | none |
| `devices-tab.png` | `-ftTable <table> -ftTab devices -ftHelp ja -ftSize 560x700`, top 560x200 kept | none |
| `overview.png` | an HTML drawing rendered with headless Chrome at 1200x520 | none |
