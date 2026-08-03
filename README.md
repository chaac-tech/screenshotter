# Screenshotter

![Screenshotter Logo](http://www.skatanicstudios.co.uk/wp-content/uploads/2020/04/logo.jpg)

#### Package for taking gameplay screenshots in the Unity Editor across all three pipeline types.

Screenshotter is a simple Camera controller plug-in for Unity that is designed to make the process of gameplay screenshots fast, easy and flexible! It allows you to fly around your scene with a gamepad or keyboard and mouse to line up a shot and adjust a Depth of Field post-process on the camera without interacting with controls in the editor.

**Features**:
  * Camera controller using a gamepad or keyboard and mouse
  * Depth of Field Controls
  * One Click Screenshot Feature
  * Reusable screenshot requirement templates and per-project catalogs
  * Exact-resolution, versioned captures linked back to catalog slots
  * Cross-Compatible with Built-In/URP/HDRP Renderers

## Requirement catalogs

Open **Window > Screenshotter > Requirement Catalog** to manage promotional image requirements.

1. In the Project window, use **Assets > Create > Screenshotter > Meta Horizon Master Template** for the pre-populated Meta category, or **Requirement Template** for a blank master.
2. Use **Assets > Create > Screenshotter > Catalog** once for each project, campaign, or release.
3. Select the catalog in the Screenshot Catalog window, choose its master from the dropdown, choose the included categories, and synchronize it.
4. Select the scene Camera that should take the image and arm a capture slot.
5. Choose whether to use Screenshotter's fly-camera controls. When enabled, Play Mode adds and configures the missing Screenshotter/PlayerInput components; when disabled, captures render directly from the selected gameplay Camera without adding components.
6. Optionally assign an `InputActionReference` for capture, then use that action or the window button. With Screenshotter enabled and no custom action, its normal F12 binding remains available.

Managed captures are saved below `Assets/Screenshots/{Catalog}/{Category}/` using names such as `Hero-Cover-01-v001.png`. Recapturing creates a new version and keeps earlier versions available. Capture-then-final requirements retain the raw source separately from the externally edited final PNG. If no catalog slot is armed, F12 continues to use the normal save dialog.

Catalog synchronization is non-destructive. New and changed template requirements are copied into the catalog, existing image references are retained, and removed requirements are marked obsolete for review.

Selecting a catalog in the Project window shows a compact completion summary and requirement review instead of its raw serialized IDs. Use **Open Screenshot Catalog** in that Inspector to continue working with the selected catalog.

## Installation

**Walkthrough Video**

[![Walkthrough Video](https://img.youtube.com/vi/frMOMNGxbN0/0.jpg)](https://www.youtube.com/watch?v=frMOMNGxbN0)

Simply open the package manager in Unity, choose the (+) sign and choose "Add Package From Git URL" and enter the url https://github.com/neon8100/screenshotter.git

Alternatively, download the Clone the project and import using the "Add Package From Disk" option. 

**Once the Package is Downloaded** right click in the hierarchy and choose "Screenshotter Camera". This will add a new Screenshotter Camera GameObject to scene and Screenshotter should detect your current render pipeline and adapt its settings/components accordingly. 

Hit play and use either a gamepad or keyboard and mouse. The active control scheme switches automatically when you use a different device.

## Controls
The screenshotter camera supports keyboard and mouse as well as any Unity-supported gamepad. The system uses the Input System `PlayerInput` component to map its controls and send messages, so you can adjust or remap them in `ScreenshotterActions.inputactions`.

**Default Controls**

### Keyboard and mouse

*General controls*
 - F12: Take screenshot
 - P: Pause/resume time scale
 - Tab: Change mode
 - F1: Show/hide the control panel
 - Left Shift: Toggle speed
 - E: Camera up
 - Q: Camera down
 - Page Down/Page Up: Next/previous profile (reserved)

*Move mode*
 - W/A/S/D: Move
 - Hold right mouse button: Capture the cursor and fly/look
 - Mouse wheel: Zoom
 - Mouse wheel while holding right mouse button: Adjust movement speed

*DOF mode*
 - W/S: Adjust focal point
 - A/D: Adjust focal narrowness
 - Mouse wheel: Increase/decrease aperture

### Game View control panel

The runtime control panel provides independent sliders for movement speed, mouse sensitivity, field of view, scroll sensitivity, and the controls supported by the active render pipeline. Movement speed affects translation only; mouse sensitivity controls captured mouse look independently. The panel also provides Camera/DOF mode, invert-look, pause, and screenshot toggles. It scales from a 1920x1080 reference resolution so that it remains a consistent relative size across Game View resolutions. Hold the right mouse button outside the panel to capture the cursor for mouse look; clicks and scrolling over the panel are reserved for UI interaction.

### Gamepad

*General Controls*
 - View/Back : Take Screenshot 
 - Options/Start : Pause/Resume TimeScale
 - A: Change Mode
 - Y: Toggle Debug Text
 - L3 (Click Left Stick In): Toggle Speed 
 - Right Trigger: Zoom In
 - Left Trigger: Zoom Out
 - Right Bumper: Camera Up
 - Left Bumper: Camera Down
 
 
*Move Mode*
 - Left Stick  - Move
 - Right Stick - Fly

*DOF Mode*
- Left Stick Vertical - Adjust Focal Point
- Left Stick Horiztonal  - Adjust Narrownes
- Right Stick Vertical - Increase/Decrease Aperture

## TODO
* Support for cycling through different "Image Effect" post-processes 
