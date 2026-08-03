using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkatanicStudios
{
    internal sealed class ScreenshotCatalogWindow : EditorWindow
    {
        [SerializeField] private ScreenshotCatalog catalog;
        [SerializeField] private Camera captureCamera;
        [SerializeField] private bool useScreenshotter = true;
        [SerializeField] private InputActionReference captureActionReference;
        [SerializeField] private string armedCategoryId;
        [SerializeField] private string armedRequirementId;
        [SerializeField] private string armedSlotId;
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private bool showObsolete;
        [NonSerialized] private Screenshotter runtimeScreenshotter;
        [NonSerialized] private string cameraSetupWarning;
        [NonSerialized] private InputAction boundCaptureAction;
        [NonSerialized] private bool enabledCaptureAction;
        [NonSerialized] private int lastCaptureFrame = -1;

        [MenuItem("Window/Screenshotter/Requirement Catalog")]
        internal static void Open()
        {
            ScreenshotCatalogWindow window = GetWindow<ScreenshotCatalogWindow>();
            window.titleContent = new GUIContent("Screenshot Catalog");
            window.minSize = new Vector2(520, 420);
            window.Show();
        }

        internal static void Open(ScreenshotCatalog targetCatalog)
        {
            Open();
            ScreenshotCatalogWindow window = GetWindow<ScreenshotCatalogWindow>();
            window.catalog = targetCatalog;
            window.ClearArmedSlot();
            window.Repaint();
        }

        private void OnEnable()
        {
            ScreenshotterCaptureBridge.ManagedCaptureRequested = HandleManagedCapture;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AutoSelectCamera();
            if (EditorApplication.isPlaying)
            {
                ConfigurePlayModeCapture();
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnbindCaptureAction();
            if (ScreenshotterCaptureBridge.ManagedCaptureRequested != null &&
                ReferenceEquals(ScreenshotterCaptureBridge.ManagedCaptureRequested.Target, this))
            {
                ScreenshotterCaptureBridge.ManagedCaptureRequested = null;
            }
        }

        private void OnGUI()
        {
            DrawAssetSelection();
            EditorGUILayout.Space();

            if (catalog == null)
            {
                EditorGUILayout.HelpBox("Select or create a catalog to begin tracking required images.", MessageType.Info);
                return;
            }

            DrawCatalogConfiguration();
            EditorGUILayout.Space();
            DrawCaptureToolbar();
            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawCatalogContents();
            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetSelection()
        {
            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);
            ScreenshotCatalog newCatalog = (ScreenshotCatalog)EditorGUILayout.ObjectField("Catalog", catalog, typeof(ScreenshotCatalog), false);
            if (newCatalog != catalog)
            {
                catalog = newCatalog;
                ClearArmedSlot();
            }

            if (catalog != null)
            {
                DrawTemplateDropdown();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Create catalogs from Assets > Create > Screenshotter > Catalog, then select one here.",
                    MessageType.Info);
            }
        }

        private void DrawTemplateDropdown()
        {
            ScreenshotTemplate[] templates = AssetDatabase.FindAssets("t:ScreenshotTemplate")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ScreenshotTemplate>)
                .Where(item => item != null)
                .OrderBy(item => item.name)
                .ToArray();
            string[] labels = new[] { "None" }
                .Concat(templates.Select(item => item.name + " — " + AssetDatabase.GetAssetPath(item)))
                .ToArray();
            int currentIndex = catalog.template == null ? 0 : Array.IndexOf(templates, catalog.template) + 1;
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            int newIndex = EditorGUILayout.Popup("Master Template", currentIndex, labels);
            if (newIndex == currentIndex)
            {
                return;
            }

            Undo.RecordObject(catalog, "Change Screenshot Master Template");
            ScreenshotTemplate selectedTemplate = newIndex == 0 ? null : templates[newIndex - 1];
            catalog.template = selectedTemplate;
            catalog.includedCategoryIds.Clear();
            if (selectedTemplate != null)
            {
                ScreenshotCatalogUtility.EnsureTemplateIds(selectedTemplate);
                catalog.includedCategoryIds.AddRange(selectedTemplate.categories
                    .Where(category => category.includedByDefault)
                    .Select(category => category.id));
                ScreenshotCatalogUtility.Synchronize(catalog);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            ClearArmedSlot();
        }

        private void DrawCatalogConfiguration()
        {
            EditorGUILayout.LabelField("Catalog Configuration", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string outputRoot = EditorGUILayout.TextField("Output Root", catalog.outputRoot);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(catalog, "Change Screenshot Output Root");
                catalog.outputRoot = outputRoot.Replace('\\', '/');
                EditorUtility.SetDirty(catalog);
            }

            if (!IsValidOutputRoot(catalog.outputRoot))
            {
                EditorGUILayout.HelpBox("Output Root must be a project-relative folder beneath Assets.", MessageType.Error);
            }

            if (catalog.template == null)
            {
                EditorGUILayout.HelpBox("This catalog has no master template.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Included Categories", EditorStyles.miniBoldLabel);
            foreach (ScreenshotCategoryDefinition category in catalog.template.categories)
            {
                bool included = catalog.includedCategoryIds.Contains(category.id);
                bool newIncluded = EditorGUILayout.ToggleLeft(category.name, included);
                if (newIncluded == included)
                {
                    continue;
                }

                Undo.RecordObject(catalog, "Change Included Screenshot Categories");
                if (newIncluded)
                {
                    catalog.includedCategoryIds.Add(category.id);
                }
                else
                {
                    catalog.includedCategoryIds.Remove(category.id);
                }
                EditorUtility.SetDirty(catalog);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Synchronize From Template"))
            {
                Undo.RecordObject(catalog, "Synchronize Screenshot Catalog");
                ScreenshotCatalogUtility.Synchronize(catalog);
                AssetDatabase.SaveAssets();
            }
            showObsolete = GUILayout.Toggle(showObsolete, "Show Obsolete", "Button", GUILayout.Width(110));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCaptureToolbar()
        {
            EditorGUILayout.LabelField("Capture", EditorStyles.boldLabel);
            Camera newCamera = (Camera)EditorGUILayout.ObjectField("Camera", captureCamera, typeof(Camera), true);
            if (newCamera != captureCamera)
            {
                captureCamera = newCamera;
                runtimeScreenshotter = null;
                cameraSetupWarning = null;
                if (EditorApplication.isPlaying)
                {
                    EnsureRuntimeScreenshotter();
                }
            }
            if (captureCamera == null)
            {
                EditorGUILayout.HelpBox("Select the scene Camera that should take catalog screenshots.", MessageType.Warning);
            }
            else if (!string.IsNullOrEmpty(cameraSetupWarning))
            {
                EditorGUILayout.HelpBox(cameraSetupWarning, MessageType.Warning);
            }

            bool newUseScreenshotter = EditorGUILayout.Toggle("Use Screenshotter", useScreenshotter);
            if (newUseScreenshotter != useScreenshotter)
            {
                useScreenshotter = newUseScreenshotter;
                runtimeScreenshotter = null;
                cameraSetupWarning = null;
                if (EditorApplication.isPlaying)
                {
                    ConfigurePlayModeCapture();
                }
            }

            InputActionReference newCaptureAction = (InputActionReference)EditorGUILayout.ObjectField(
                "Capture Input Action", captureActionReference, typeof(InputActionReference), false);
            if (newCaptureAction != captureActionReference)
            {
                UnbindCaptureAction();
                captureActionReference = newCaptureAction;
                if (EditorApplication.isPlaying)
                {
                    BindCaptureAction();
                }
            }

            ScreenshotCatalogRequirement requirement;
            ScreenshotCatalogSlot slot;
            ScreenshotCatalogCategory category;
            bool armed = TryGetArmedSlot(out category, out requirement, out slot);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Armed Slot", armed ? category.name + " / " + requirement.name + " / " + slot.name : "None");
            EditorGUI.BeginDisabledGroup(!armed);
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                ClearArmedSlot();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(!armed || captureCamera == null || !EditorApplication.isPlaying || !IsValidOutputRoot(catalog.outputRoot));
            string captureButtonLabel = "Capture Armed Slot";
            if (captureActionReference != null && captureActionReference.action != null)
            {
                captureButtonLabel += " (" + captureActionReference.action.name + ")";
            }
            else if (useScreenshotter)
            {
                captureButtonLabel += " (F12)";
            }
            if (GUILayout.Button(captureButtonLabel, GUILayout.Height(30)))
            {
                CaptureArmedSlot();
            }
            EditorGUI.EndDisabledGroup();

            if (armed && !EditorApplication.isPlaying)
            {
                string playModeMessage = useScreenshotter
                    ? "Enter Play Mode to add Screenshotter to the selected Camera and apply the armed resolution."
                    : "Enter Play Mode to capture directly from the selected Camera without adding Screenshotter components.";
                EditorGUILayout.HelpBox(playModeMessage, MessageType.Info);
            }
        }

        private void DrawCatalogContents()
        {
            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                if (category.obsolete && !showObsolete)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(category.name + (category.obsolete ? " (Obsolete)" : string.Empty), EditorStyles.boldLabel);
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
                {
                    if (requirement.obsolete && !showObsolete)
                    {
                        continue;
                    }
                    DrawRequirement(category, requirement);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }

        private void DrawRequirement(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement)
        {
            EditorGUILayout.BeginVertical("box");
            string specification = string.Format("{0} — {1}x{2} ({3}) — {4}", requirement.workflow, requirement.width, requirement.height, requirement.dimensionRule, requirement.imageFormat);
            EditorGUILayout.LabelField(requirement.name + (requirement.obsolete ? " (Obsolete)" : string.Empty), EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(specification, EditorStyles.miniLabel);
            if (!string.IsNullOrWhiteSpace(requirement.guidance))
            {
                EditorGUILayout.HelpBox(requirement.guidance, MessageType.None);
            }
            if (!string.IsNullOrWhiteSpace(requirement.sourceUrl) && GUILayout.Button("Open Source Guidelines", EditorStyles.miniButton))
            {
                Application.OpenURL(requirement.sourceUrl);
            }

            for (int index = 0; index < requirement.slots.Count; index++)
            {
                ScreenshotCatalogSlot slot = requirement.slots[index];
                if (slot.obsolete && !showObsolete)
                {
                    continue;
                }
                DrawSlot(category, requirement, slot, index);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSlot(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot, int slotIndex)
        {
            ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(slot.name + (slot.required ? " *" : string.Empty), GUILayout.MinWidth(150));
            Color previousColor = GUI.color;
            GUI.color = GetStatusColor(status);
            GUILayout.Label(status.ToString(), EditorStyles.miniBoldLabel, GUILayout.Width(85));
            GUI.color = previousColor;

            bool canCapture = !requirement.obsolete && !slot.obsolete && requirement.workflow != ScreenshotAssetWorkflow.External;
            EditorGUI.BeginDisabledGroup(!canCapture);
            bool isArmed = IsArmed(category, requirement, slot);
            if (GUILayout.Button(isArmed ? "Armed" : "Arm", GUILayout.Width(60)))
            {
                Arm(category, requirement, slot);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (requirement.workflow != ScreenshotAssetWorkflow.External)
            {
                DrawVersionField("Source", slot.sourceVersions, slot.activeSource, texture =>
                {
                    Undo.RecordObject(catalog, "Change Active Screenshot Source");
                    ScreenshotCatalogUtility.AddSourceVersion(slot, texture);
                });
            }

            if (requirement.workflow != ScreenshotAssetWorkflow.Capture)
            {
                DrawVersionField("Final", slot.finalVersions, slot.activeFinal, texture =>
                {
                    Undo.RecordObject(catalog, "Change Final Screenshot Asset");
                    ScreenshotCatalogUtility.AddFinalVersion(slot, texture);
                });
            }

            Texture2D active = requirement.workflow == ScreenshotAssetWorkflow.Capture ? slot.activeSource : slot.activeFinal;
            if (active != null && !ScreenshotCatalogUtility.ValidateTexture(active, requirement, out string validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawVersionField(string label, System.Collections.Generic.List<Texture2D> versions, Texture2D active, Action<Texture2D> assign)
        {
            EditorGUI.BeginChangeCheck();
            Texture2D selected = (Texture2D)EditorGUILayout.ObjectField(label, active, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                assign(selected);
                EditorUtility.SetDirty(catalog);
            }

            if (versions.Count <= 1)
            {
                return;
            }

            string[] labels = versions.Select((texture, index) => texture == null ? "Missing v" + (index + 1) : texture.name).ToArray();
            int currentIndex = Mathf.Max(0, versions.IndexOf(active));
            int newIndex = EditorGUILayout.Popup(label + " Version", currentIndex, labels);
            if (newIndex != currentIndex)
            {
                assign(versions[newIndex]);
                EditorUtility.SetDirty(catalog);
            }
        }

        private void Arm(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            armedCategoryId = category.definitionId;
            armedRequirementId = requirement.definitionId;
            armedSlotId = slot.definitionId;
            AutoSelectCamera();
            if (EditorApplication.isPlaying && useScreenshotter)
            {
                ApplyRequirement(EnsureRuntimeScreenshotter(), requirement);
            }
        }

        private bool HandleManagedCapture(Screenshotter source)
        {
            ScreenshotCatalogCategory category;
            ScreenshotCatalogRequirement requirement;
            ScreenshotCatalogSlot slot;
            if (!useScreenshotter || !TryGetArmedSlot(out category, out requirement, out slot) ||
                captureCamera == null || source.GetComponent<Camera>() != captureCamera)
            {
                return false;
            }

            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Enter Play Mode before capturing a catalog slot.");
                return true;
            }

            if (!IsValidOutputRoot(catalog.outputRoot))
            {
                Debug.LogError("Screenshot catalog output root must be beneath Assets.");
                return true;
            }

            try
            {
                if (lastCaptureFrame != Time.frameCount)
                {
                    Capture(category, requirement, slot, captureCamera);
                    lastCaptureFrame = Time.frameCount;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            return true;
        }

        private void Capture(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot, Camera sourceCamera)
        {
            string assetFolder = ScreenshotCatalogUtility.GetCatalogFolder(catalog, category);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absoluteFolder = Path.Combine(projectRoot, assetFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(absoluteFolder);

            int version = ScreenshotCatalogUtility.GetNextVersion(slot);
            int slotIndex = requirement.slots.IndexOf(slot);
            string filename;
            string assetPath;
            string absolutePath;
            do
            {
                filename = ScreenshotCatalogUtility.GetVersionedFilename(requirement.name, slotIndex, version);
                assetPath = assetFolder + "/" + filename;
                absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                version++;
            }
            while (File.Exists(absolutePath));

            ScreenshotterCameraSetup.Capture(sourceCamera, absolutePath, requirement.width, requirement.height);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(requirement.width, requirement.height)), 32, 16384);
                importer.SaveAndReimport();
            }
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                throw new InvalidOperationException("Screenshot was written but could not be imported at " + assetPath);
            }

            Undo.RecordObject(catalog, "Capture Catalog Screenshot");
            ScreenshotCatalogUtility.AddSourceVersion(slot, texture);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Repaint();
            Debug.Log("Captured catalog image: " + assetPath, texture);
        }

        private bool TryGetArmedSlot(out ScreenshotCatalogCategory category, out ScreenshotCatalogRequirement requirement, out ScreenshotCatalogSlot slot)
        {
            category = null;
            requirement = null;
            slot = null;
            if (catalog == null || string.IsNullOrEmpty(armedSlotId))
            {
                return false;
            }

            category = catalog.categories.FirstOrDefault(item => item.definitionId == armedCategoryId);
            requirement = category != null ? category.requirements.FirstOrDefault(item => item.definitionId == armedRequirementId) : null;
            slot = requirement != null ? requirement.slots.FirstOrDefault(item => item.definitionId == armedSlotId) : null;
            return category != null && requirement != null && slot != null && !category.obsolete && !requirement.obsolete && !slot.obsolete;
        }

        private bool IsArmed(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            return category.definitionId == armedCategoryId && requirement.definitionId == armedRequirementId && slot.definitionId == armedSlotId;
        }

        private void ClearArmedSlot()
        {
            armedCategoryId = null;
            armedRequirementId = null;
            armedSlotId = null;
        }

        private void AutoSelectCamera()
        {
            if (captureCamera != null)
            {
                return;
            }

            Camera[] candidates = FindObjectsOfType<Camera>();
            Camera[] screenshotterCameras = candidates.Where(candidate => candidate.GetComponent<Screenshotter>() != null).ToArray();
            if (screenshotterCameras.Length == 1)
            {
                captureCamera = screenshotterCameras[0];
            }
            else if (Camera.main != null)
            {
                captureCamera = Camera.main;
            }
            else if (candidates.Length == 1)
            {
                captureCamera = candidates[0];
            }
        }

        private Screenshotter EnsureRuntimeScreenshotter()
        {
            if (!EditorApplication.isPlaying || !useScreenshotter || captureCamera == null)
            {
                return null;
            }

            if (runtimeScreenshotter == null)
            {
                runtimeScreenshotter = ScreenshotterCameraSetup.Ensure(captureCamera, out cameraSetupWarning);
            }

            ScreenshotCatalogCategory category;
            ScreenshotCatalogRequirement requirement;
            ScreenshotCatalogSlot slot;
            if (TryGetArmedSlot(out category, out requirement, out slot))
            {
                ApplyRequirement(runtimeScreenshotter, requirement);
            }
            return runtimeScreenshotter;
        }

        private static void ApplyRequirement(Screenshotter target, ScreenshotCatalogRequirement requirement)
        {
            if (target == null || requirement == null)
            {
                return;
            }
            target.gameViewScreenshot = false;
            target.screenShotResolution = new Vector2Int(requirement.width, requirement.height);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                runtimeScreenshotter = null;
                AutoSelectCamera();
                ConfigurePlayModeCapture();
                Repaint();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                runtimeScreenshotter = null;
                cameraSetupWarning = null;
                UnbindCaptureAction();
                Repaint();
            }
        }

        private void ConfigurePlayModeCapture()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            if (useScreenshotter)
            {
                EnsureRuntimeScreenshotter();
            }
            BindCaptureAction();
        }

        private void BindCaptureAction()
        {
            if (!EditorApplication.isPlaying || captureActionReference == null || captureActionReference.action == null)
            {
                return;
            }

            InputAction action = captureActionReference.action;
            if (boundCaptureAction == action)
            {
                return;
            }

            UnbindCaptureAction();
            boundCaptureAction = action;
            boundCaptureAction.performed += OnCaptureActionPerformed;
            if (!boundCaptureAction.enabled)
            {
                boundCaptureAction.Enable();
                enabledCaptureAction = true;
            }
        }

        private void UnbindCaptureAction()
        {
            if (boundCaptureAction == null)
            {
                return;
            }
            boundCaptureAction.performed -= OnCaptureActionPerformed;
            if (enabledCaptureAction)
            {
                boundCaptureAction.Disable();
            }
            boundCaptureAction = null;
            enabledCaptureAction = false;
        }

        private void OnCaptureActionPerformed(InputAction.CallbackContext context)
        {
            CaptureArmedSlot();
        }

        private void CaptureArmedSlot()
        {
            if (!EditorApplication.isPlaying || captureCamera == null || lastCaptureFrame == Time.frameCount)
            {
                return;
            }

            ScreenshotCatalogCategory category;
            ScreenshotCatalogRequirement requirement;
            ScreenshotCatalogSlot slot;
            if (!TryGetArmedSlot(out category, out requirement, out slot) || !IsValidOutputRoot(catalog.outputRoot))
            {
                return;
            }

            if (useScreenshotter)
            {
                ApplyRequirement(EnsureRuntimeScreenshotter(), requirement);
            }

            try
            {
                Capture(category, requirement, slot, captureCamera);
                lastCaptureFrame = Time.frameCount;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool IsValidOutputRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string normalized = value.Replace('\\', '/').TrimEnd('/');
            if (normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string resolved = Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                return resolved == assetsRoot || resolved.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Color GetStatusColor(ScreenshotCatalogStatus status)
        {
            switch (status)
            {
                case ScreenshotCatalogStatus.Complete:
                    return new Color(0.35f, 0.8f, 0.4f);
                case ScreenshotCatalogStatus.SourceReady:
                    return new Color(0.95f, 0.75f, 0.25f);
                case ScreenshotCatalogStatus.Invalid:
                    return new Color(1f, 0.35f, 0.35f);
                case ScreenshotCatalogStatus.Obsolete:
                    return Color.gray;
                default:
                    return Color.white;
            }
        }
    }
}
