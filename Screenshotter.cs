using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_HDRP
using UnityEngine.Rendering.HighDefinition;
#elif UNITY_URP
using UnityEngine.Rendering.Universal;

#elif UNITY_BUILT_IN
using UnityEngine.Rendering.PostProcessing;
#endif
namespace SkatanicStudios
{
    [RequireComponent(typeof(Camera))]
    [RequireComponent(typeof(PlayerInput))]
    public class Screenshotter : MonoBehaviour
    {
        PlayerInput input;
        Camera camera;
#if UNITY_HDRP || UNITY_URP
        Volume volume;
        DepthOfField depthOfField;
#elif UNITY_BUILT_IN
    PostProcessLayer layer;
    PostProcessVolume volume;
    DepthOfField depthOfField;
#endif

        public bool invertLook;
        public bool gameViewScreenshot = true;
        public Vector2Int screenShotResolution = new Vector2Int(1920, 1080);
        [HideInInspector] public Color debugTextColor = Color.yellow;
        [HideInInspector] public int debugFontSize = 30;

        [SerializeField, Range(0.01f, 0.2f)] internal float mouseLookSensitivity = 0.05f;
        [SerializeField, Range(10f, 360f)] internal float gamepadLookSpeed = 90f;
        [SerializeField, Range(0.1f, 10f)] internal float dofAdjustmentSpeed = 1f;
        [SerializeField, Range(0.01f, 0.5f)] internal float scrollMovementSpeedSensitivity = 0.1f;
        [SerializeField, Range(0.5f, 10f)] internal float scrollZoomSensitivity = 3f;
        [SerializeField, Range(0.1f, 4f)] internal float scrollApertureSensitivity = 0.5f;
        [SerializeField] internal Vector2 controlPanelReferenceResolution = new Vector2(1920, 1080);

        float speed = 1f;
        float horizontal;
        float vertical;
        float height;
        float lookVertical;
        float lookHorizontal;
        float zoomIn;
        float zoomOut;
        bool showControlPanel = true;
        bool isDOFControl;
        float timeScale = 1;
        bool cursorCaptured;
        bool captureBlockedUntilRightButtonRelease;
        CursorLockMode previousCursorLockMode;
        bool previousCursorVisible;
        Rect controlPanelRect = new Rect(20, 20, 380, 560);
        GUISkin controlPanelSkin;
        string screenshotWidthText;
        string screenshotHeightText;

        const string KeyboardMouseControlScheme = "Keyboard&Mouse";

        private void Awake()
        {
            camera = GetComponent<Camera>();
            input = GetComponent<PlayerInput>();
            screenshotWidthText = screenShotResolution.x.ToString();
            screenshotHeightText = screenShotResolution.y.ToString();


            //Make this the camera in focus
            camera.depth = float.MaxValue;
#if UNITY_HDRP || UNITY_URP
            volume = gameObject.AddComponent<Volume>();
            volume.priority = 100f;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

            depthOfField = volume.profile.Add<DepthOfField>(true);
#if UNITY_HDRP
            depthOfField.focusMode.Override(DepthOfFieldMode.Manual);
#elif UNITY_URP

            depthOfField.mode.Override(DepthOfFieldMode.Bokeh);
#endif
#elif UNITY_BUILT_IN
            gameObject.layer = LayerMask.NameToLayer("PostProcessing");

        layer = gameObject.AddComponent<PostProcessLayer>();
        layer.volumeLayer.value = LayerMask.GetMask("PostProcessing");

        volume = gameObject.AddComponent<PostProcessVolume>();
        volume.priority = 100;
        volume.profile = new PostProcessProfile();
        volume.isGlobal = true;

        camera.allowHDR = true;

        depthOfField = volume.profile.AddSettings<DepthOfField>();
#endif

#if UNITY_HDRP
            nearStart = depthOfField.nearFocusStart.GetValue<float>();
            nearEnd = depthOfField.nearFocusEnd.GetValue<float>();
            farStart = depthOfField.farFocusStart.GetValue<float>();
            farEnd = depthOfField.farFocusEnd.GetValue<float>();
            aperture = camera.aperture;
#elif UNITY_URP || UNITY_BUILT_IN
            focusDistance = depthOfField.focusDistance.GetValue<float>();
            focalLength = depthOfField.focalLength.GetValue<float>();
            aperture = depthOfField.aperture.GetValue<float>();
#endif
        }

        public void OnMove(InputValue value)
        {
            horizontal = value.Get<Vector2>().x;
            vertical = value.Get<Vector2>().y;
        }

        public void OnLook(InputValue value)
        {
            lookHorizontal = value.Get<Vector2>().x;
            lookVertical = value.Get<Vector2>().y;
        }

        public void OnZoomIn(InputValue value)
        {
            zoomIn = value.Get<float>();
        }

        public void OnZoomOut(InputValue value)
        {
            zoomOut = value.Get<float>();
        }

        public void OnCameraUp(InputValue value)
        {
            height = value.Get<float>();
        }

        public void OnCameraDown(InputValue value)
        {
            height = value.Get<float>() * -1;
        }

        public void OnToggleDebug(InputValue value)
        {
            if (value.Get<float>() == 1)
            {
                showControlPanel = !showControlPanel;
                if (showControlPanel)
                {
                    ReleaseCursor();
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        public void OnToggleControls(InputValue value)
        {
            if (value.Get<float>() == 1)
            {
                isDOFControl = !isDOFControl;
                if (isDOFControl)
                {
                    ReleaseCursor();
                }
            }
        }

        public void OnToggleSpeed(InputValue value)
        {
            if (value.Get<float>() == 1)
            {
                if (speed == 0.1f)
                {
                    speed = 2;
                }
                else if (speed == 2)
                {
                    speed = 1;
                }
                else if (speed == 1)
                {
                    speed = 0.5f;
                }
                else if (speed == 0.5f)
                {
                    speed = 0.1f;
                }
            }
        }

        public void OnScreenshot(InputValue value)
        {
            if (value.Get<float>() == 1)
            {
                TakeScreenshot();
            }
        }

        public void OnPause(InputValue value)
        {
            if (value.Get<float>() == 1)
            {
                timeScale = Mathf.Approximately(Time.timeScale, 0) ? 1 : 0;
                Time.timeScale = timeScale;
            }
        }

        public void OnNext(InputValue value)
        {
            //TODO Profile Switching
        }

        public void OnPrevious(InputValue value)
        {
            //TODO Profile Switching
        }

#if UNITY_HDRP
        float nearStart;
        float nearEnd;
        float farStart;
        float farEnd;
        float aperture;
#elif UNITY_URP || UNITY_BUILT_IN
        float focusDistance;
        float focalLength;
        float aperture;
#endif

        private void Update()
        {
            UpdateCursorCapture();

            float moveHorizontal = horizontal;
            float moveVertical = vertical;
            float activeLookHorizontal = lookHorizontal;
            float activeLookVertical = lookVertical;
            float scroll = zoomIn - zoomOut;
            bool usingKeyboardMouse = IsUsingKeyboardMouse();
            float unscaledFrameRate = Time.unscaledDeltaTime * 60f;

            if (usingKeyboardMouse)
            {
                if (cursorCaptured)
                {
                    activeLookHorizontal *= mouseLookSensitivity;
                    activeLookVertical *= mouseLookSensitivity;
                }
                else
                {
                    activeLookHorizontal = 0;
                    activeLookVertical = 0;
                }

                if (showControlPanel && !cursorCaptured && IsPointerOverControlPanel())
                {
                    scroll = 0;
                }
            }

            if (!isDOFControl && usingKeyboardMouse && cursorCaptured && !Mathf.Approximately(scroll, 0))
            {
                speed = Mathf.Clamp(speed + scroll * scrollMovementSpeedSensitivity, 0.1f, 2f);
                scroll = 0;
            }

            if (isDOFControl)
            {
                if (Mathf.Abs(moveVertical) < 0.25f)
                {
                    moveVertical = 0;
                }

                if (Mathf.Abs(moveHorizontal) < 0.25f)
                {
                    moveHorizontal = 0;
                }

                if (Mathf.Abs(activeLookHorizontal) < 0.25f)
                {
                    activeLookHorizontal = 0;
                }

                if (Mathf.Abs(activeLookVertical) < 0.25f)
                {
                    activeLookVertical = 0;
                }
#if UNITY_HDRP
                float dofStep = dofAdjustmentSpeed * unscaledFrameRate;
                float nearFocusStart = moveVertical * dofStep;
                float nearFocusEnd = moveHorizontal * dofStep;

                float farFocusStart = usingKeyboardMouse ? 0 : activeLookVertical * dofStep;
                float farFocusEnd = usingKeyboardMouse ? 0 : activeLookHorizontal * dofStep;

                nearStart += nearFocusStart;
                nearEnd += nearFocusEnd;
                farStart += farFocusStart;
                farEnd += farFocusEnd;
            
                if (nearStart > nearEnd)
                {
                    nearEnd = nearStart;
                }
                if (farStart > farEnd)
                {
                    farEnd = farStart;
                }

                if (farStart < 0)
                {
                    farStart = 0;
                }
                if (nearStart < 0)
                {
                    nearStart = 0;
                }
                if (farEnd < 0)
                {
                    farEnd = 0;
                }
                if (nearEnd < 0)
                {
                    nearEnd = 0;
                }

                depthOfField.nearFocusStart.Override(nearStart);
                depthOfField.nearFocusEnd.Override(nearEnd);
                depthOfField.farFocusStart.Override(farStart);
                depthOfField.farFocusEnd.Override(farEnd);

                if (usingKeyboardMouse && !Mathf.Approximately(scroll, 0))
                {
                    aperture = Mathf.Clamp(aperture + scroll * scrollApertureSensitivity, 0.7f, 32f);
                    camera.aperture = aperture;
                }

#elif UNITY_URP || UNITY_BUILT_IN
                float dofStep = dofAdjustmentSpeed * unscaledFrameRate;
                focusDistance = Mathf.Clamp(focusDistance + moveVertical * dofStep, 0, float.MaxValue);
                focalLength = Mathf.Clamp(focalLength + moveHorizontal * dofStep, 0, float.MaxValue);

                if (usingKeyboardMouse)
                {
                    aperture = Mathf.Clamp(aperture + scroll * scrollApertureSensitivity, 0.1f, 32f);
                }
                else
                {
                    aperture = Mathf.Clamp(aperture + activeLookVertical * dofStep, 0.1f, 32f);
                }

                depthOfField.focalLength.Override(focalLength);
                depthOfField.focusDistance.Override(focusDistance);
                depthOfField.aperture.Override(aperture);
#endif
            }
            else
            {
                Vector3 movement = (transform.forward * moveVertical) + (transform.right * moveHorizontal) +
                                   (Vector3.up * height);
                transform.position += movement * speed * unscaledFrameRate;

                float lookMultiplier = usingKeyboardMouse ? 1 : gamepadLookSpeed * Time.unscaledDeltaTime;
                transform.Rotate(Vector3.up, activeLookHorizontal * lookMultiplier);

                if (invertLook)
                {
                    transform.Rotate(Vector3.right, activeLookVertical * lookMultiplier);
                }
                else
                {
                    transform.Rotate(Vector3.right, -activeLookVertical * lookMultiplier);
                }

                transform.rotation = Quaternion.Euler(transform.rotation.eulerAngles.x, transform.rotation.eulerAngles.y, 0);
            }

            if (!isDOFControl && !Mathf.Approximately(scroll, 0))
            {
                camera.fieldOfView = Mathf.Clamp(camera.fieldOfView - scroll * scrollZoomSensitivity, 1, 100);
            }
        }

        internal bool IsUsingKeyboardMouse()
        {
            return input != null && input.currentControlScheme == KeyboardMouseControlScheme;
        }

        internal void UpdateCursorCapture()
        {
            Mouse mouse = Mouse.current;
            if (!IsUsingKeyboardMouse() || mouse == null || isDOFControl)
            {
                ReleaseCursor();
                return;
            }

            if (!mouse.rightButton.isPressed)
            {
                captureBlockedUntilRightButtonRelease = false;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                captureBlockedUntilRightButtonRelease = true;
                ReleaseCursor();
                return;
            }

            bool pointerOverPanel = !cursorCaptured && IsPointerOverControlPanel();
            if (mouse.rightButton.isPressed && !captureBlockedUntilRightButtonRelease && !pointerOverPanel)
            {
                CaptureCursor();
            }
            else
            {
                ReleaseCursor();
            }
        }

        internal void CaptureCursor()
        {
            if (cursorCaptured)
            {
                return;
            }

            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cursorCaptured = true;
        }

        internal void ReleaseCursor()
        {
            if (!cursorCaptured)
            {
                return;
            }

            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisible;
            cursorCaptured = false;
        }

        internal bool IsPointerOverControlPanel()
        {
            if (!showControlPanel || Mouse.current == null)
            {
                return false;
            }

            float scale = GetControlPanelScale();
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            mousePosition.x /= scale;
            mousePosition.y = (Screen.height - mousePosition.y) / scale;
            return controlPanelRect.Contains(mousePosition);
        }

        internal float GetControlPanelScale()
        {
            float referenceWidth = Mathf.Max(1, controlPanelReferenceResolution.x);
            float referenceHeight = Mathf.Max(1, controlPanelReferenceResolution.y);
            return Mathf.Max(0.01f, Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight));
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                captureBlockedUntilRightButtonRelease = true;
                ReleaseCursor();
            }
        }

        private void OnDisable()
        {
            ReleaseCursor();

            if (controlPanelSkin != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(controlPanelSkin);
                }
                else
                {
                    DestroyImmediate(controlPanelSkin);
                }
            }
        }

        public int screenshotCount;

        public void TakeScreenshot()
        {
#if UNITY_EDITOR
            string time = string.Format("{0}_{1}_{2}", System.DateTime.Now.Year, System.DateTime.Now.Month, System.DateTime.Now.Day);
            string name = string.Format("{0}_{1}_shot_{2}", time, Application.productName, screenshotCount).ToLower();
            string fullpathname = EditorUtility.SaveFilePanel("Save Screenshot", "", name, "png");
            if (string.IsNullOrEmpty(fullpathname))
            {
                return;
            }

            TakeNewScreenshot(fullpathname);
            screenshotCount++;
#endif
        }

        public void TakeNewScreenShot(string path, string filename)
        {
            string fname = string.Format("{0}/{1}.png", path, filename);
            TakeNewScreenshot(fname);
        }

        public void TakeNewScreenshot(string fullPath)
        {
            if (gameViewScreenshot)
            {
                ScreenCapture.CaptureScreenshot(fullPath);
            }
            else
            {
                if (camera == null)
                {
                    camera = GetComponent<Camera>();
                }

                RenderTexture previousRt = RenderTexture.active;
                RenderTexture cameraTargetTexture = camera.targetTexture;
                RenderTexture rt = new RenderTexture(screenShotResolution.x, screenShotResolution.y, 24);
                camera.targetTexture = rt;
                Texture2D screenShot = new Texture2D(screenShotResolution.x, screenShotResolution.y, TextureFormat.RGBA32, false);
                camera.Render();
                RenderTexture.active = rt;
                screenShot.ReadPixels(new Rect(0, 0, screenShotResolution.x, screenShotResolution.y), 0, 0);

                camera.targetTexture = cameraTargetTexture;
                RenderTexture.active = previousRt; //Reassign the active render texture back to the previous one

                if (Application.isEditor)
                {
                    DestroyImmediate(rt);
                }
                else
                {
                    Destroy(rt);
                }

                byte[] bytes = screenShot.EncodeToPNG();

                System.IO.File.WriteAllBytes(fullPath, bytes);
            }

            Debug.Log(string.Format("Took screenshot to: {0}", fullPath));
        }

        private void OnGUI()
        {
            if (!showControlPanel)
            {
                return;
            }

            float scale = GetControlPanelScale();
            Matrix4x4 previousMatrix = GUI.matrix;
            GUISkin previousSkin = GUI.skin;

            EnsureControlPanelSkin();

            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                GUI.skin = controlPanelSkin;
                ClampControlPanelToGameView(scale);
                controlPanelRect = GUILayout.Window(GetInstanceID(), controlPanelRect, DrawControlPanel, "Screenshotter");
                ClampControlPanelToGameView(scale);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.skin = previousSkin;
            }
        }

        internal void EnsureControlPanelSkin()
        {
            if (controlPanelSkin != null)
            {
                return;
            }

            controlPanelSkin = Instantiate(GUI.skin);
            controlPanelSkin.window.fontSize = 13;
            controlPanelSkin.label.fontSize = 14;
            controlPanelSkin.toggle.fontSize = 13;
            controlPanelSkin.button.fontSize = 13;
            controlPanelSkin.textField.fontSize = 13;
        }

        internal void ClampControlPanelToGameView(float scale)
        {
            float logicalWidth = Screen.width / scale;
            float logicalHeight = Screen.height / scale;
            float maximumX = Mathf.Max(0, logicalWidth - controlPanelRect.width);
            float maximumY = Mathf.Max(0, logicalHeight - controlPanelRect.height);
            controlPanelRect.x = Mathf.Clamp(controlPanelRect.x, 0, maximumX);
            controlPanelRect.y = Mathf.Clamp(controlPanelRect.y, 0, maximumY);
        }

        internal void DrawControlPanel(int windowId)
        {
            GUILayout.Label("Input: " + (input != null ? input.currentControlScheme : "Unavailable"));
            GUILayout.Label("Hold RMB to look; scroll while looking changes movement speed.");
            GUILayout.Label("F1 shows/hides the panel.");

            bool newDOFControl = GUILayout.Toggle(isDOFControl, "Depth of Field mode");
            if (newDOFControl != isDOFControl)
            {
                isDOFControl = newDOFControl;
                ReleaseCursor();
            }

            GUILayout.Space(6);
            GUILayout.Label("Camera");
            speed = DrawSlider("Movement speed", speed, 0.1f, 2f);
            mouseLookSensitivity = DrawSlider("Mouse sensitivity", mouseLookSensitivity, 0.01f, 0.2f, "0.000");
            scrollZoomSensitivity = DrawSlider("Scroll zoom", scrollZoomSensitivity, 0.5f, 10f);
            camera.fieldOfView = DrawSlider("Field of view", camera.fieldOfView, 1f, 100f);
            invertLook = GUILayout.Toggle(invertLook, "Invert vertical look");

            GUILayout.Space(6);
            GUILayout.Label("Depth of Field");
            scrollApertureSensitivity = DrawSlider("Scroll aperture", scrollApertureSensitivity, 0.1f, 4f);
#if UNITY_HDRP
            nearStart = DrawSlider("Near start", nearStart, 0, 100);
            nearEnd = DrawSlider("Near end", Mathf.Max(nearStart, nearEnd), nearStart, 100);
            farStart = DrawSlider("Far start", farStart, 0, 100);
            farEnd = DrawSlider("Far end", Mathf.Max(farStart, farEnd), farStart, 100);
            aperture = DrawSlider("Aperture", aperture, 0.7f, 32f);

            depthOfField.nearFocusStart.Override(nearStart);
            depthOfField.nearFocusEnd.Override(nearEnd);
            depthOfField.farFocusStart.Override(farStart);
            depthOfField.farFocusEnd.Override(farEnd);
            camera.aperture = aperture;
#elif UNITY_URP || UNITY_BUILT_IN
            focusDistance = DrawSlider("Focus distance", focusDistance, 0.1f, 100f);
            focalLength = DrawSlider("Focal length", focalLength, 1f, 300f);
            aperture = DrawSlider("Aperture", aperture, 0.1f, 32f);

            depthOfField.focusDistance.Override(focusDistance);
            depthOfField.focalLength.Override(focalLength);
            depthOfField.aperture.Override(aperture);
#else
            GUILayout.Label("No supported render pipeline is active.");
#endif

            GUILayout.Space(6);
            GUILayout.Label("Capture");
            bool paused = Mathf.Approximately(Time.timeScale, 0);
            bool newPaused = GUILayout.Toggle(paused, "Pause time");
            if (newPaused != paused)
            {
                timeScale = newPaused ? 0 : 1;
                Time.timeScale = timeScale;
            }

            gameViewScreenshot = GUILayout.Toggle(gameViewScreenshot, "Use Game View resolution");
            if (!gameViewScreenshot)
            {
                screenShotResolution.x = DrawResolutionField("Width", ref screenshotWidthText, screenShotResolution.x);
                screenShotResolution.y = DrawResolutionField("Height", ref screenshotHeightText, screenShotResolution.y);
            }

            if (GUILayout.Button("Take Screenshot (F12)"))
            {
                TakeScreenshot();
            }

            GUI.DragWindow(new Rect(0, 0, controlPanelRect.width, 24));
        }

        internal float DrawSlider(string label, float value, float minimum, float maximum, string format = "0.00")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(145));
            float result = GUILayout.HorizontalSlider(value, minimum, maximum);
            GUILayout.Label(result.ToString(format), GUILayout.Width(55));
            GUILayout.EndHorizontal();
            return result;
        }

        internal int DrawResolutionField(string label, ref string text, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(145));
            text = GUILayout.TextField(text, GUILayout.Width(100));
            int parsedValue;
            if (int.TryParse(text, out parsedValue) && parsedValue > 0)
            {
                value = parsedValue;
            }
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
