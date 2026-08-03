# Screenshotter

![Screenshotter Logo](http://www.skatanicstudios.co.uk/wp-content/uploads/2020/04/logo.jpg)

#### Package for taking gameplay screenshots in the Unity Editor across all three pipeline types.

Screenshotter is a simple Camera controller plug-in for Unity that is designed to make the process of gameplay screenshots fast, easy and flexible! It allows you to fly around your scene with a gamepad or keyboard and mouse to line up a shot and adjust a Depth of Field post-process on the camera without interacting with controls in the editor.

**Features**:
  * Camera controller using a gamepad or keyboard and mouse
  * Depth of Field Controls
  * One Click Screenshot Feature
  * Cross-Compatible with Built-In/URP/HDRP Renderers

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
