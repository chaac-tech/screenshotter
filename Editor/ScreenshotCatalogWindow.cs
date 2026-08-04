using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SkatanicStudios
{
    internal enum ScreenshotRequirementFilter
    {
        All,
        Missing,
        SourceReady,
        Complete,
        Invalid,
        Obsolete
    }

    internal sealed class ScreenshotCatalogWindow : EditorWindow
    {
        [SerializeField] private ScreenshotCatalog catalog;
        [SerializeField] private Camera captureCamera;
        [SerializeField] private bool useScreenshotter = true;
        [SerializeField] private bool matchGameViewResolution = true;
        [SerializeField] private InputActionReference captureActionReference;
        [SerializeField] private string armedCategoryId;
        [SerializeField] private string armedRequirementId;
        [SerializeField] private string armedSlotId;
        [SerializeField] private bool showCatalogConfiguration = true;
        [SerializeField] private bool showCaptureConfiguration = true;
        [SerializeField] private bool showGoogleDriveSync;
        [SerializeField] private string requirementSearch = string.Empty;
        [SerializeField] private ScreenshotRequirementFilter requirementFilter;
        [SerializeField] private string expandedRequirementId;
        [SerializeField] private bool initializedRequirementSelection;
        [SerializeField] private string selectedCategoryId;
        [SerializeField] private string selectedRequirementId;
        [SerializeField] private string selectedSlotId;
        [SerializeField] private bool showSettingsPage;
        [NonSerialized] private Screenshotter runtimeScreenshotter;
        [NonSerialized] private string cameraSetupWarning;
        [NonSerialized] private string gameViewResolutionWarning;
        [NonSerialized] private InputAction boundCaptureAction;
        [NonSerialized] private bool enabledCaptureAction;
        [NonSerialized] private int lastCaptureFrame = -1;
        [NonSerialized] private IMGUIContainer headerContainer;
        [NonSerialized] private IMGUIContainer catalogContainer;
        [NonSerialized] private ScrollView catalogScrollView;
        [NonSerialized] private bool googleDriveBusy;
        [NonSerialized] private string googleDriveMessage;
        [NonSerialized] private MessageType googleDriveMessageType;
        [NonSerialized] private string pendingScrollRequirementId;
        [NonSerialized] private ScreenshotCatalogWindowView windowView;

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
            window.expandedRequirementId = null;
            window.initializedRequirementSelection = false;
            window.ClearSelectedSlot();
            window.showSettingsPage = false;
            window.ClearArmedSlot();
            window.RepaintContainers();
        }

        private void OnEnable()
        {
            ScreenshotterCaptureBridge.ManagedCaptureRequested = HandleManagedCapture;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            BuildVisualTree();
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

        private void BuildVisualTree()
        {
            windowView = new ScreenshotCatalogWindowView(
                rootVisualElement,
                DrawTopBarGUI,
                DrawNavigatorToolbar,
                BuildSlotNavigator,
                DrawWorkspaceGUI,
                DrawStatusBarGUI);
            headerContainer = windowView.topBar;
            catalogContainer = windowView.workspaceContents;
            catalogScrollView = windowView.workspaceScrollView;
        }

        private void DrawTopBarGUI()
        {
            bool narrow = position.width < ScreenshotCatalogWindowView.NarrowLayoutThreshold;
            if (narrow)
            {
                EditorGUILayout.BeginHorizontal();
                DrawTopCatalogControl();
                DrawTopCameraControl();
                DrawSettingsToggle();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                DrawTopOutputControl();
                DrawTopProgressControl();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                DrawTopCatalogControl();
                DrawTopCameraControl();
                DrawTopOutputControl();
                DrawTopProgressControl();
                DrawSettingsToggle();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTopCatalogControl()
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(150f));
            EditorGUILayout.LabelField(Tip("Catalog", "Catalog that owns the requirements, versions, and synchronization state shown in this window."), EditorStyles.miniBoldLabel);
            ScreenshotCatalog newCatalog = (ScreenshotCatalog)EditorGUILayout.ObjectField(
                catalog, typeof(ScreenshotCatalog), false, GUILayout.MinWidth(145f));
            if (newCatalog != catalog)
            {
                SetCatalog(newCatalog);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTopCameraControl()
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(145f));
            EditorGUILayout.LabelField(Tip("Capture Camera", "Scene Camera used for managed captures. The sole active camera is selected automatically when possible."), EditorStyles.miniBoldLabel);
            Camera newCamera = (Camera)EditorGUILayout.ObjectField(
                captureCamera, typeof(Camera), true, GUILayout.MinWidth(140f));
            if (newCamera != captureCamera)
            {
                captureCamera = newCamera;
                runtimeScreenshotter = null;
                cameraSetupWarning = null;
                if (EditorApplication.isPlaying)
                {
                    ConfigurePlayModeCapture();
                }
                RepaintContainers();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTopOutputControl()
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(175f));
            EditorGUILayout.LabelField(Tip("Output Folder", "Project-relative root beneath Assets where managed screenshots are organized by catalog and category."), EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(catalog == null ? "No catalog selected" : catalog.outputRoot, GUILayout.MinWidth(130f));
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(catalog == null);
            if (GUILayout.Button(Tip("…", "Choose an output folder inside this project's Assets folder."), GUILayout.Width(28f)))
            {
                SelectOutputRoot();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawTopProgressControl()
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(145f), GUILayout.MaxWidth(250f));
            ScreenshotCatalogGUI.GetRequiredProgress(catalog, out int complete, out int total);
            EditorGUILayout.LabelField(
                Tip(complete + " / " + total + " Complete", "Completed required, non-obsolete slots. Optional and obsolete slots are excluded."),
                EditorStyles.miniBoldLabel);
            Rect progressRect = GUILayoutUtility.GetRect(130f, 17f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, total == 0 ? 0f : (float)complete / total, string.Empty);
            EditorGUILayout.EndVertical();
        }

        private void DrawSettingsToggle()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(74f));
            GUILayout.Space(EditorGUIUtility.singleLineHeight + 2f);
            string label = showSettingsPage ? "Capture" : "Settings";
            if (GUILayout.Button(Tip(label, showSettingsPage
                    ? "Return to the selected slot's capture workspace."
                    : "Open catalog, capture, and Google Drive settings."), GUILayout.Width(70f)))
            {
                showSettingsPage = !showSettingsPage;
                RepaintContainers();
            }
            EditorGUILayout.EndVertical();
        }

        internal void SetCatalog(ScreenshotCatalog newCatalog)
        {
            catalog = newCatalog;
            expandedRequirementId = null;
            initializedRequirementSelection = false;
            pendingScrollRequirementId = null;
            ClearSelectedSlot();
            ClearArmedSlot();
            showSettingsPage = false;
            EnsureSelectedSlot();
            RepaintContainers();
        }

        internal string SelectedCategoryId { get { return selectedCategoryId; } }
        internal string SelectedRequirementId { get { return selectedRequirementId; } }
        internal string SelectedSlotId { get { return selectedSlotId; } }
        internal bool SettingsPageVisible
        {
            get { return showSettingsPage; }
            set
            {
                showSettingsPage = value;
                RepaintContainers();
            }
        }

        private void BuildSlotNavigator(VisualElement root)
        {
            if (catalog == null)
            {
                Label empty = new Label("Select a Screenshot Catalog in the top bar.");
                empty.AddToClassList("screenshot-catalog-empty-message");
                root.Add(empty);
                return;
            }

            EnsureSelectedSlot();
            ScreenshotCatalogCategory[] categories = catalog.categories.ToArray();
            bool anyVisible = false;
            foreach (ScreenshotCatalogCategory category in categories)
            {
                var visibleSlots = category.requirements
                    .SelectMany(requirement => requirement.slots
                        .Where(slot => MatchesSlotFilter(category, requirement, slot))
                        .Select(slot => new { requirement, slot }))
                    .ToArray();
                if (visibleSlots.Length == 0)
                {
                    continue;
                }

                anyVisible = true;
                int completed = visibleSlots.Count(item =>
                    ScreenshotCatalogUtility.GetStatus(item.requirement, item.slot) == ScreenshotCatalogStatus.Complete);
                VisualElement categoryHeader = new VisualElement();
                categoryHeader.AddToClassList("screenshot-catalog-category-header");
                Label categoryName = new Label(category.name + (category.obsolete ? " (Obsolete)" : string.Empty));
                categoryName.tooltip = "Requirement category copied from the catalog's master template.";
                categoryName.AddToClassList("screenshot-catalog-category-name");
                Label categoryCount = new Label(completed + " / " + visibleSlots.Length);
                categoryCount.AddToClassList("screenshot-catalog-category-count");
                categoryHeader.Add(categoryName);
                categoryHeader.Add(categoryCount);
                root.Add(categoryHeader);

                foreach (var item in visibleSlots)
                {
                    root.Add(CreateNavigatorSlotRow(category, item.requirement, item.slot));
                }
            }

            if (!anyVisible)
            {
                Label empty = new Label("No slots match the current search and status filter.");
                empty.AddToClassList("screenshot-catalog-empty-message");
                root.Add(empty);
            }
        }

        private void DrawNavigatorToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ??
                                   GUI.skin.FindStyle("ToolbarSearchTextField") ??
                                   EditorStyles.toolbarTextField;
            string newSearch = GUILayout.TextField(requirementSearch ?? string.Empty, searchStyle, GUILayout.MinWidth(70f));
            if (newSearch != requirementSearch)
            {
                requirementSearch = newSearch;
                if (windowView != null)
                {
                    windowView.RefreshNavigator();
                }
            }
            ScreenshotRequirementFilter newFilter = (ScreenshotRequirementFilter)EditorGUILayout.EnumPopup(
                requirementFilter, EditorStyles.toolbarPopup, GUILayout.Width(82f));
            if (newFilter != requirementFilter)
            {
                requirementFilter = newFilter;
                if (windowView != null)
                {
                    windowView.RefreshNavigator();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button(Tip("Next Missing", "Select the next required slot whose workflow is missing an asset."), EditorStyles.miniButton))
            {
                SelectNextMissingSlot();
            }
            EditorGUILayout.Space(3f);
        }

        private VisualElement CreateNavigatorSlotRow(
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot)
        {
            ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
            bool selected = IsSelected(category, requirement, slot);
            bool armed = IsArmed(category, requirement, slot);
            VisualElement row = new VisualElement();
            row.AddToClassList("screenshot-catalog-slot-row");
            row.EnableInClassList("screenshot-catalog-slot-row--selected", selected);
            row.tooltip = string.IsNullOrWhiteSpace(requirement.guidance)
                ? ScreenshotCatalogGUI.GetStatusTooltip(status)
                : requirement.guidance;
            row.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SelectSlot(category, requirement, slot);
                    evt.StopPropagation();
                }
            });

            Label name = new Label(GetSlotDisplayName(requirement, slot) + (slot.required ? string.Empty : "  (Optional)"));
            name.AddToClassList("screenshot-catalog-slot-name");
            row.Add(name);
            if (armed)
            {
                Label armedIcon = new Label("▶");
                armedIcon.tooltip = "This slot is armed for capture.";
                armedIcon.AddToClassList("screenshot-catalog-slot-armed");
                row.Add(armedIcon);
            }
            Label statusIcon = new Label(ScreenshotCatalogGUI.GetStatusIcon(status));
            statusIcon.tooltip = ScreenshotCatalogGUI.GetStatusTooltip(status);
            statusIcon.AddToClassList("screenshot-catalog-slot-status");
            statusIcon.style.color = ScreenshotCatalogGUI.GetStatusColor(status);
            row.Add(statusIcon);
            return row;
        }

        private void DrawWorkspaceGUI()
        {
            if (catalog == null)
            {
                EditorGUILayout.HelpBox("Select a Screenshot Catalog to begin.", MessageType.Info);
                return;
            }
            EnsureSelectedSlot();
            if (showSettingsPage)
            {
                DrawSettingsWorkspace();
            }
            else
            {
                DrawSelectedSlotWorkspace();
            }
        }

        private void DrawSettingsWorkspace()
        {
            EditorGUILayout.LabelField("Catalog Settings", EditorStyles.largeLabel);
            EditorGUILayout.Space(5f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(Tip("Master Template", "Reusable requirement definitions used to synchronize this catalog."), EditorStyles.boldLabel);
            DrawTemplateDropdown();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
            DrawCatalogConfiguration();
            EditorGUILayout.Space(6f);
            DrawCaptureSettings();
            EditorGUILayout.Space(6f);
            DrawGoogleDriveSync();
        }

        private void DrawCaptureSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(Tip("Capture Settings", "Configure optional Screenshotter integration and capture input behavior."), EditorStyles.boldLabel);

            bool newUseScreenshotter = EditorGUILayout.Toggle(
                Tip("Use Screenshotter", "Add and configure Screenshotter and PlayerInput on the selected Camera during Play Mode. Disable this to capture directly from an ordinary gameplay Camera."),
                useScreenshotter);
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

            bool newMatchGameViewResolution = EditorGUILayout.Toggle(
                Tip("Match Game View", "Automatically select or create the armed slot's exact fixed resolution in the Unity Game View."),
                matchGameViewResolution);
            if (newMatchGameViewResolution != matchGameViewResolution)
            {
                matchGameViewResolution = newMatchGameViewResolution;
                gameViewResolutionWarning = null;
                if (matchGameViewResolution)
                {
                    ApplyArmedGameViewResolution();
                }
            }

            InputActionReference newCaptureAction = (InputActionReference)EditorGUILayout.ObjectField(
                Tip("Capture Input Action", "Optional Input System action that triggers the armed capture in Play Mode. Leave empty to use Screenshotter's normal F12 input when Screenshotter is enabled."),
                captureActionReference,
                typeof(InputActionReference),
                false);
            if (newCaptureAction != captureActionReference)
            {
                UnbindCaptureAction();
                captureActionReference = newCaptureAction;
                if (EditorApplication.isPlaying)
                {
                    BindCaptureAction();
                }
            }

            if (!string.IsNullOrEmpty(cameraSetupWarning))
            {
                EditorGUILayout.HelpBox(cameraSetupWarning, MessageType.Warning);
            }
            if (!string.IsNullOrEmpty(gameViewResolutionWarning))
            {
                EditorGUILayout.HelpBox(gameViewResolutionWarning, MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedSlotWorkspace()
        {
            if (!TryGetSelectedSlot(out ScreenshotCatalogCategory category,
                    out ScreenshotCatalogRequirement requirement,
                    out ScreenshotCatalogSlot slot))
            {
                EditorGUILayout.HelpBox("This catalog has no selectable slots.", MessageType.Info);
                return;
            }

            ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(GetSlotWorkspaceTitle(requirement, slot), EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            DrawStatusBadge(status, ScreenshotCatalogUtility.GetStatusLabel(status));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5f);

            EditorGUILayout.LabelField(
                Tip(ScreenshotCatalogGUI.GetSpecification(requirement), "Workflow, target dimensions, validation rule, and PNG format."),
                EditorStyles.miniLabel);
            EditorGUILayout.Space(5f);
            if (!string.IsNullOrWhiteSpace(requirement.guidance))
            {
                EditorGUILayout.HelpBox(requirement.guidance, MessageType.None);
            }
            if (!string.IsNullOrWhiteSpace(requirement.sourceUrl) &&
                GUILayout.Button(Tip("Open Source Guidelines", "Open the specification URL stored by the master template."), EditorStyles.miniButton))
            {
                Application.OpenURL(requirement.sourceUrl);
            }

            if (requirement.workflow != ScreenshotAssetWorkflow.External)
            {
                EditorGUILayout.Space(8f);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(
                    Tip("Next Capture File", "Filename and location that the next managed source capture will use."),
                    ScreenshotCatalogUtility.GetNextCaptureAssetPath(catalog, category, requirement, slot));
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(7f);
            if (requirement.workflow != ScreenshotAssetWorkflow.External)
            {
                DrawVersionField("Source", slot, false, slot.sourceVersions, slot.activeSource, texture =>
                {
                    Undo.RecordObject(catalog, "Change Active Screenshot Source");
                    ScreenshotCatalogUtility.AddSourceVersion(slot, texture);
                }, index => ScreenshotCatalogUtility.RemoveSourceVersion(slot, index));
            }
            if (requirement.workflow != ScreenshotAssetWorkflow.Capture)
            {
                DrawVersionField("Final", slot, true, slot.finalVersions, slot.activeFinal, texture =>
                {
                    Undo.RecordObject(catalog, "Change Final Screenshot Asset");
                    ScreenshotCatalogUtility.AddFinalVersion(slot, texture);
                }, index => ScreenshotCatalogUtility.RemoveFinalVersion(slot, index));
            }

            Texture2D reviewTexture = ScreenshotCatalogUtility.GetReviewTexture(requirement, slot);
            if (reviewTexture != null && !ScreenshotCatalogUtility.ValidateTexture(reviewTexture, requirement, out string validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
            }
            DrawSelectedSlotActions(category, requirement, slot);
            DrawSelectedSlotWarnings(requirement, slot);
        }

        private void DrawSelectedSlotActions(
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot)
        {
            bool canCapture = !category.obsolete && !requirement.obsolete && !slot.obsolete &&
                              requirement.workflow != ScreenshotAssetWorkflow.External;
            bool armed = IsArmed(category, requirement, slot);
            EditorGUILayout.Space(10f);
            Rect divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(divider, new Color(0.5f, 0.5f, 0.5f, 0.35f));
            EditorGUILayout.Space(10f);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!canCapture);
            if (GUILayout.Button(Tip(armed ? "Disarm" : "Arm", armed
                    ? "Disarm this slot without changing assigned images."
                    : "Arm this slot as the target for the next managed capture."), GUILayout.Height(32f)))
            {
                ToggleArmedSlot(category, requirement, slot);
            }
            if (GUILayout.Button(Tip("Apply Resolution", "Set the Unity Game View to this slot's exact configured resolution."), GUILayout.Height(32f)))
            {
                ApplyGameViewResolution(requirement, true);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!canCapture || !armed || captureCamera == null ||
                                         !EditorApplication.isPlaying || !IsValidOutputRoot(catalog.outputRoot));
            string captureLabel = useScreenshotter ? "Capture (F12)" : "Capture";
            if (captureActionReference != null && captureActionReference.action != null)
            {
                captureLabel = "Capture (" + captureActionReference.action.name + ")";
            }
            if (GUILayout.Button(Tip(captureLabel, "Capture the selected Camera, import a new source version, and make it active."), GUILayout.Height(32f)))
            {
                CaptureArmedSlot();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectedSlotWarnings(ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            if (requirement.workflow == ScreenshotAssetWorkflow.External)
            {
                return;
            }
            if (captureCamera == null)
            {
                EditorGUILayout.HelpBox("Select a Capture Camera in the top bar.", MessageType.Warning);
            }
            if (!IsValidOutputRoot(catalog.outputRoot))
            {
                EditorGUILayout.HelpBox("Choose an output folder beneath Assets.", MessageType.Error);
            }
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode before capturing this slot.", MessageType.Info);
            }
        }

        private void DrawStatusBarGUI()
        {
            if (catalog == null || !TryGetSelectedSlot(out ScreenshotCatalogCategory category,
                    out ScreenshotCatalogRequirement requirement,
                    out ScreenshotCatalogSlot slot))
            {
                EditorGUILayout.LabelField("No catalog slot selected", EditorStyles.miniLabel);
                return;
            }

            Texture2D reviewTexture = ScreenshotCatalogUtility.GetReviewTexture(requirement, slot);
            ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
            string assetPath = reviewTexture == null ? "No active asset" : AssetDatabase.GetAssetPath(reviewTexture);
            bool valid = reviewTexture != null && ScreenshotCatalogUtility.ValidateTexture(reviewTexture, requirement, out _);
            bool narrow = position.width < ScreenshotCatalogWindowView.NarrowLayoutThreshold;
            if (narrow)
            {
                EditorGUILayout.LabelField(Tip(Path.GetFileName(assetPath), assetPath), EditorStyles.miniLabel);
            }
            EditorGUILayout.BeginHorizontal();
            if (!narrow)
            {
                EditorGUILayout.LabelField(Tip(Path.GetFileName(assetPath), assetPath), EditorStyles.miniLabel, GUILayout.MinWidth(130f));
                GUILayout.FlexibleSpace();
            }
            DrawFooterStatus(reviewTexture != null, "File found", "No active asset");
            DrawFooterStatus(valid, "Specification valid", "Resolution, format, or transparency is invalid");
            DrawFooterStatus(status == ScreenshotCatalogStatus.Complete,
                "Workflow complete",
                ScreenshotCatalogUtility.GetStatusLabel(status));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawFooterStatus(bool success, string successLabel, string failureLabel)
        {
            Color previousColor = GUI.color;
            GUI.color = success ? ScreenshotCatalogGUI.GetStatusColor(ScreenshotCatalogStatus.Complete) : Color.gray;
            GUILayout.Label(Tip((success ? "✓  " : "○  ") + (success ? successLabel : failureLabel),
                success ? successLabel : failureLabel), EditorStyles.miniBoldLabel, GUILayout.Width(138f));
            GUI.color = previousColor;
        }

        private void DrawHeaderGUI()
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(155f, Mathf.Max(120f, position.width * 0.32f));
            try
            {
                DrawAssetSelection();
                EditorGUILayout.Space(3f);

                if (catalog == null)
                {
                    EditorGUILayout.HelpBox("Select or create a catalog to begin tracking required images.", MessageType.Info);
                    return;
                }

                DrawCatalogConfiguration();
                EditorGUILayout.Space(3f);
                DrawCaptureToolbar();
                EditorGUILayout.Space(3f);
                DrawGoogleDriveSync();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        private void DrawCatalogGUI()
        {
            if (catalog == null)
            {
                return;
            }
            DrawCatalogContents();
        }

        private void DrawAssetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(Tip("Assets", "Select the catalog and master template that define this capture session."), EditorStyles.boldLabel);
            ScreenshotCatalog newCatalog = (ScreenshotCatalog)EditorGUILayout.ObjectField(
                Tip("Catalog", "The per-project, campaign, or release asset that tracks required slots, capture history, and final deliverables."),
                catalog,
                typeof(ScreenshotCatalog),
                false);
            if (newCatalog != catalog)
            {
                catalog = newCatalog;
                expandedRequirementId = null;
                initializedRequirementSelection = false;
                pendingScrollRequirementId = null;
                ClearArmedSlot();
                RepaintContainers();
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
            EditorGUILayout.EndVertical();
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

            int newIndex = EditorGUILayout.Popup(
                Tip("Master Template", "Reusable requirement definitions used to synchronize this catalog. Changing it resets the included-category selection."),
                currentIndex,
                labels);
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
            expandedRequirementId = null;
            initializedRequirementSelection = false;
            ClearSelectedSlot();
            ClearArmedSlot();
            EnsureSelectedSlot();
            RepaintContainers();
        }

        private void DrawCatalogConfiguration()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string includedSummary = catalog.template == null
                ? "No template"
                : catalog.includedCategoryIds.Count + " categor" + (catalog.includedCategoryIds.Count == 1 ? "y" : "ies");
            showCatalogConfiguration = DrawSectionFoldout(
                showCatalogConfiguration,
                Tip("Catalog Configuration  ·  " + includedSummary, "Choose where managed captures are saved and which template categories this catalog tracks."));
            if (!showCatalogConfiguration)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(
                Tip("Output Root", "Read-only project-relative folder beneath Assets. Managed captures are organized below it by catalog and category."),
                catalog.outputRoot);
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button(Tip("Browse...", "Choose the output root with a folder browser. The folder must be inside this project's Assets folder."), GUILayout.Width(75)))
            {
                SelectOutputRoot();
            }
            EditorGUILayout.EndHorizontal();

            if (!IsValidOutputRoot(catalog.outputRoot))
            {
                EditorGUILayout.HelpBox("Output Root must be a project-relative folder beneath Assets.", MessageType.Error);
            }

            if (catalog.template == null)
            {
                EditorGUILayout.HelpBox("This catalog has no master template.", MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField(Tip("Included Categories", "Template categories that will be copied into this catalog during synchronization."), EditorStyles.miniBoldLabel);
            foreach (ScreenshotCategoryDefinition category in catalog.template.categories)
            {
                bool included = catalog.includedCategoryIds.Contains(category.id);
                bool newIncluded = EditorGUILayout.ToggleLeft(
                    Tip(category.name, "Include or exclude this category the next time the catalog synchronizes with its master template."),
                    included);
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

            if (GUILayout.Button(Tip(
                    "Synchronize From Template",
                    "Add or update included requirements from the master template, preserve capture history, mark removed populated entries obsolete, and prune empty obsolete entries.")))
            {
                Undo.RecordObject(catalog, "Synchronize Screenshot Catalog");
                ScreenshotCatalogUtility.Synchronize(catalog);
                AssetDatabase.SaveAssets();
                RepaintContainers();
            }
            EditorGUILayout.EndVertical();
        }

        private void SelectOutputRoot()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string initialFolder = IsValidOutputRoot(catalog.outputRoot)
                ? Path.GetFullPath(Path.Combine(projectRoot, catalog.outputRoot.Replace('/', Path.DirectorySeparatorChar)))
                : Application.dataPath;
            string selectedFolder = EditorUtility.OpenFolderPanel("Select Screenshot Output Folder", initialFolder, string.Empty);
            if (string.IsNullOrEmpty(selectedFolder))
            {
                return;
            }

            string assetFolder;
            if (!TryConvertToAssetFolder(selectedFolder, out assetFolder))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Screenshot Output Folder",
                    "Choose a folder inside this project's Assets folder so captured images can be imported and referenced by the catalog.",
                    "OK");
                return;
            }

            Undo.RecordObject(catalog, "Change Screenshot Output Root");
            catalog.outputRoot = assetFolder;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            RepaintContainers();
        }

        private void DrawCaptureToolbar()
        {
            ScreenshotCatalogRequirement requirement;
            ScreenshotCatalogSlot slot;
            ScreenshotCatalogCategory category;
            bool armed = TryGetArmedSlot(out category, out requirement, out slot);
            string armedSummary = armed ? requirement.name + " / " + slot.name : "Nothing armed";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showCaptureConfiguration = DrawSectionFoldout(
                showCaptureConfiguration,
                Tip("Capture  ·  " + armedSummary, "Configure the scene camera and input used to capture the armed catalog slot."));
            if (!showCaptureConfiguration)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(2f);
            Camera newCamera = (Camera)EditorGUILayout.ObjectField(
                Tip("Camera", "Scene Camera used to render managed screenshots. The sole active Camera is selected automatically when possible."),
                captureCamera,
                typeof(Camera),
                true);
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

            bool newUseScreenshotter = EditorGUILayout.Toggle(
                Tip("Use Screenshotter", "Add and configure Screenshotter and PlayerInput on the selected Camera during Play Mode. Disable this to capture directly from an ordinary gameplay Camera."),
                useScreenshotter);
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

            bool newMatchGameViewResolution = EditorGUILayout.Toggle(
                Tip("Match Game View To Armed Slot", "When a slot is armed, select or create its exact fixed resolution in the Unity Game View and refresh the Game View scale."),
                matchGameViewResolution);
            if (newMatchGameViewResolution != matchGameViewResolution)
            {
                matchGameViewResolution = newMatchGameViewResolution;
                gameViewResolutionWarning = null;
                if (matchGameViewResolution)
                {
                    ApplyArmedGameViewResolution();
                }
            }
            if (!string.IsNullOrEmpty(gameViewResolutionWarning))
            {
                EditorGUILayout.HelpBox(gameViewResolutionWarning, MessageType.Warning);
            }

            InputActionReference newCaptureAction = (InputActionReference)EditorGUILayout.ObjectField(
                Tip("Capture Input Action", "Optional Input System action that triggers the armed capture in Play Mode. Leave empty to use Screenshotter's normal F12 input when Screenshotter is enabled."),
                captureActionReference,
                typeof(InputActionReference),
                false);
            if (newCaptureAction != captureActionReference)
            {
                UnbindCaptureAction();
                captureActionReference = newCaptureAction;
                if (EditorApplication.isPlaying)
                {
                    BindCaptureAction();
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                Tip("Armed Slot", "The catalog slot that will receive the next managed capture."),
                Tip(armed ? category.name + " / " + requirement.name + " / " + slot.name : "None", "Category, requirement, and slot currently targeted for capture."));
            EditorGUI.BeginDisabledGroup(!armed);
            if (GUILayout.Button(Tip("Clear", "Disarm the current slot without changing its assigned images."), GUILayout.Width(60)))
            {
                ClearArmedSlot();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(!armed || captureCamera == null || !EditorApplication.isPlaying || !IsValidOutputRoot(catalog.outputRoot));
            string captureButtonLabel = armed ? "Capture Armed Slot" : "Select a slot below to capture";
            if (armed && captureActionReference != null && captureActionReference.action != null)
            {
                captureButtonLabel += " (" + captureActionReference.action.name + ")";
            }
            else if (armed && useScreenshotter)
            {
                captureButtonLabel += " (F12)";
            }
            if (GUILayout.Button(Tip(captureButtonLabel, "Capture the selected Camera at the armed requirement's resolution, save a new version, import it, and make it the active source."), GUILayout.Height(30)))
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
            EditorGUILayout.EndVertical();
        }

        private void DrawGoogleDriveSync()
        {
            ScreenshotGoogleDriveProfile currentProfile = catalog.googleDriveProfile;
            bool currentConfigured = ScreenshotGoogleDriveService.IsConfigured(currentProfile);
            bool currentAuthorized = currentProfile != null && ScreenshotGoogleDriveService.IsAuthorized(currentProfile);
            int currentPending = currentProfile == null ? 0 : ScreenshotGoogleDriveService.CountPending(catalog);
            string driveSummary = currentProfile == null
                ? "Not configured"
                : !currentConfigured ? "Profile incomplete"
                : !currentAuthorized ? "Disconnected"
                : currentPending == 0 ? "No uploads pending" : currentPending + " upload" + (currentPending == 1 ? " pending" : "s pending");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showGoogleDriveSync = DrawSectionFoldout(
                showGoogleDriveSync,
                Tip("Google Drive Sync  ·  " + driveSummary, "Push locally tracked versions to Google Drive or pull matching PNGs from its Source and Final folders."));
            if (!showGoogleDriveSync)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.indentLevel++;
            ScreenshotGoogleDriveProfile newProfile = (ScreenshotGoogleDriveProfile)EditorGUILayout.ObjectField(
                Tip("Sync Profile", "Reusable OAuth and destination-folder settings for Google Drive uploads."),
                catalog.googleDriveProfile,
                typeof(ScreenshotGoogleDriveProfile),
                false);
            if (newProfile != catalog.googleDriveProfile)
            {
                Undo.RecordObject(catalog, "Change Google Drive Sync Profile");
                catalog.googleDriveProfile = newProfile;
                EditorUtility.SetDirty(catalog);
                googleDriveMessage = null;
            }

            ScreenshotGoogleDriveProfile profile = catalog.googleDriveProfile;
            if (profile == null)
            {
                EditorGUILayout.HelpBox(
                    "Create a profile with Assets > Create > Screenshotter > Google Drive Sync Profile, configure it in the Inspector, then assign it here.",
                    MessageType.Info);
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(
                Tip("Destination", "Google Drive folder ID configured by the selected sync profile. 'root' means My Drive."),
                string.IsNullOrWhiteSpace(profile.destinationFolderId) ? "root" : profile.destinationFolderId);
            EditorGUI.EndDisabledGroup();

            bool configured = ScreenshotGoogleDriveService.IsConfigured(profile);
            bool authorized = ScreenshotGoogleDriveService.IsAuthorized(profile);
            int pending = ScreenshotGoogleDriveService.CountPending(catalog);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                Tip("Connection", "Whether this editor has a stored OAuth refresh token for the selected profile."));
            DrawTextBadge(
                !configured ? "Profile incomplete" : authorized ? "Connected" : "Disconnected",
                configured && authorized ? new Color(0.35f, 0.8f, 0.4f) : new Color(0.95f, 0.55f, 0.25f),
                "OAuth tokens are stored locally under Library/Screenshotter.");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Tip("Sync Status", "Whether tracked source and final versions still need to be uploaded through this profile."));
            DrawTextBadge(
                pending == 0 ? "No uploads pending" : pending + " pending",
                pending == 0 ? new Color(0.35f, 0.8f, 0.4f) : new Color(0.95f, 0.75f, 0.25f),
                pending == 0
                    ? "Every locally tracked version has a Google Drive file binding for this profile. Use Pull New to check for remote additions."
                    : "These locally tracked versions have not yet been uploaded through this profile.");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    Tip("Show Profile", "Select the sync profile in the Inspector to load OAuth credentials or change the destination folder."),
                    EditorStyles.miniButtonLeft))
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }

            EditorGUI.BeginDisabledGroup(googleDriveBusy || !configured);
            if (!authorized)
            {
                if (GUILayout.Button(
                        Tip("Connect", "Open Google authorization in the system browser and store the resulting token locally for this project."),
                        EditorStyles.miniButtonMid))
                {
                    ConnectGoogleDrive(profile);
                }
            }
            else if (GUILayout.Button(
                         Tip("Disconnect", "Remove the locally stored OAuth token. This does not revoke access in Google or change uploaded files."),
                         EditorStyles.miniButtonMid))
            {
                ScreenshotGoogleDriveService.Disconnect(profile);
                googleDriveMessage = "Disconnected from Google Drive on this editor.";
                googleDriveMessageType = MessageType.Info;
                RepaintContainers();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button(
                    Tip("Open Folder", "Open the configured destination folder in Google Drive."),
                    EditorStyles.miniButtonRight))
            {
                Application.OpenURL(ScreenshotGoogleDriveService.GetFolderUrl(profile));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(googleDriveBusy || !configured || !authorized);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    Tip("Pull New", "Download new PNG files from each category's Source and Final folders, import them into the project, and attach filenames that match catalog slots."),
                    GUILayout.Height(26f)))
            {
                PullNewFromGoogleDrive();
            }
            EditorGUI.BeginDisabledGroup(pending == 0);
            if (GUILayout.Button(
                    Tip(pending == 0
                            ? "Push New (0)"
                            : "Push " + pending + " New Version" + (pending == 1 ? string.Empty : "s"),
                        "Upload every locally tracked version that has not been uploaded through this profile. Existing Drive files are never overwritten."),
                    GUILayout.Height(26f)))
            {
                PushNewToGoogleDrive();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(googleDriveMessage))
            {
                EditorGUILayout.HelpBox(googleDriveMessage, googleDriveMessageType);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private async void ConnectGoogleDrive(ScreenshotGoogleDriveProfile profile)
        {
            googleDriveBusy = true;
            googleDriveMessage = "Waiting for Google authorization in your browser...";
            googleDriveMessageType = MessageType.Info;
            RepaintContainers();
            try
            {
                await ScreenshotGoogleDriveService.AuthorizeAsync(profile);
                googleDriveMessage = "Connected to Google Drive.";
                googleDriveMessageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                googleDriveMessage = exception.GetBaseException().Message;
                googleDriveMessageType = MessageType.Error;
                Debug.LogException(exception);
            }
            finally
            {
                googleDriveBusy = false;
                RepaintContainers();
            }
        }

        private async void PushNewToGoogleDrive()
        {
            googleDriveBusy = true;
            googleDriveMessage = "Preparing Google Drive sync...";
            googleDriveMessageType = MessageType.Info;
            RepaintContainers();
            try
            {
                ScreenshotGoogleDrivePushResult result = await ScreenshotGoogleDriveService.PushNewAsync(catalog, progress =>
                {
                    googleDriveMessage = progress;
                    googleDriveMessageType = MessageType.Info;
                    RepaintContainers();
                });
                googleDriveMessage = string.Format(
                    "Uploaded {0} new version{1}. {2} version{3} already synced.",
                    result.uploaded,
                    result.uploaded == 1 ? string.Empty : "s",
                    result.alreadySynced,
                    result.alreadySynced == 1 ? " was" : "s were");
                googleDriveMessageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                googleDriveMessage = exception.GetBaseException().Message;
                googleDriveMessageType = MessageType.Error;
                Debug.LogException(exception);
            }
            finally
            {
                googleDriveBusy = false;
                RepaintContainers();
            }
        }

        private async void PullNewFromGoogleDrive()
        {
            googleDriveBusy = true;
            googleDriveMessage = "Scanning Google Drive Source and Final folders...";
            googleDriveMessageType = MessageType.Info;
            RepaintContainers();
            try
            {
                ScreenshotGoogleDrivePullResult result = await ScreenshotGoogleDriveService.PullNewAsync(catalog, progress =>
                {
                    googleDriveMessage = progress;
                    googleDriveMessageType = MessageType.Info;
                    RepaintContainers();
                });
                googleDriveMessage = string.Format(
                    "Downloaded {0} new version{1}. {2} already synced. {3} unmatched.{4}",
                    result.downloaded,
                    result.downloaded == 1 ? string.Empty : "s",
                    result.alreadySynced,
                    result.unmatched,
                    result.metadataFailures == 0
                        ? string.Empty
                        : " " + result.metadataFailures + " imported file metadata update" +
                          (result.metadataFailures == 1 ? " failed." : "s failed."));
                googleDriveMessageType = result.unmatched > 0 || result.metadataFailures > 0
                    ? MessageType.Warning
                    : MessageType.Info;
            }
            catch (Exception exception)
            {
                googleDriveMessage = exception.GetBaseException().Message;
                googleDriveMessageType = MessageType.Error;
                Debug.LogException(exception);
            }
            finally
            {
                googleDriveBusy = false;
                RepaintContainers();
            }
        }

        private void DrawCatalogContents()
        {
            ScreenshotCatalogCategory[] categories = catalog.categories.ToArray();
            var entries = categories
                .SelectMany(category => category.requirements)
                .SelectMany(requirement => requirement.slots.Select(slot => new
                {
                    requirement,
                    slot,
                    status = ScreenshotCatalogUtility.GetStatus(requirement, slot)
                }))
                .ToArray();
            int complete = entries.Count(item => item.status == ScreenshotCatalogStatus.Complete);
            int sourceReady = entries.Count(item => item.status == ScreenshotCatalogStatus.SourceReady);
            int missing = entries.Count(item => item.status == ScreenshotCatalogStatus.Missing && item.slot.required);
            int invalid = entries.Count(item => item.status == ScreenshotCatalogStatus.Invalid);
            int obsolete = entries.Count(item => item.status == ScreenshotCatalogStatus.Obsolete);
            if (!initializedRequirementSelection)
            {
                var preferred = entries.FirstOrDefault(item => item.requirement.definitionId == armedRequirementId) ??
                                entries.FirstOrDefault(item => item.status == ScreenshotCatalogStatus.Invalid) ??
                                entries.FirstOrDefault(item => item.status == ScreenshotCatalogStatus.Missing && item.slot.required);
                expandedRequirementId = preferred == null ? string.Empty : preferred.requirement.definitionId;
                initializedRequirementSelection = true;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                Tip("Requirements", "Current completion summary across the selected catalog categories."),
                EditorStyles.boldLabel,
                GUILayout.Width(90f));
            ScreenshotCatalogGUI.DrawCountBadge("Complete", complete, ScreenshotCatalogStatus.Complete);
            ScreenshotCatalogGUI.DrawCountBadge("Source Ready", sourceReady, ScreenshotCatalogStatus.SourceReady);
            ScreenshotCatalogGUI.DrawCountBadge("Missing", missing, ScreenshotCatalogStatus.Missing);
            ScreenshotCatalogGUI.DrawCountBadge("Invalid", invalid, ScreenshotCatalogStatus.Invalid);
            if (obsolete > 0)
            {
                ScreenshotCatalogGUI.DrawCountBadge("Obsolete", obsolete, ScreenshotCatalogStatus.Obsolete);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(Tip("Search", "Filter requirements by requirement or slot name."), GUILayout.Width(42f));
            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.textField;
            string newSearch = GUILayout.TextField(requirementSearch ?? string.Empty, searchStyle);
            if (newSearch != requirementSearch)
            {
                requirementSearch = newSearch;
            }
            if (!string.IsNullOrEmpty(requirementSearch) && GUILayout.Button(
                    Tip("×", "Clear the requirement search."),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(22f)))
            {
                requirementSearch = string.Empty;
                GUI.FocusControl(null);
            }
            string[] filterLabels = { "All", "Missing", "Source Ready", "Complete", "Invalid", "Obsolete" };
            ScreenshotRequirementFilter newFilter = (ScreenshotRequirementFilter)EditorGUILayout.Popup(
                (int)requirementFilter,
                filterLabels,
                EditorStyles.toolbarPopup,
                GUILayout.Width(92f));
            if (newFilter != requirementFilter)
            {
                requirementFilter = newFilter;
            }
            if (GUILayout.Button(
                    Tip("Next Missing", "Select, expand, and scroll to the next missing required requirement."),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(92f)))
            {
                SelectNextMissing(categories);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2f);

            if (!categories.SelectMany(category => category.requirements).Any(MatchesRequirementFilter))
            {
                EditorGUILayout.HelpBox("No requirements match the current search and status filter.", MessageType.Info);
                return;
            }

            foreach (ScreenshotCatalogCategory category in categories)
            {
                ScreenshotCatalogRequirement[] visibleRequirements = category.requirements
                    .Where(MatchesRequirementFilter)
                    .ToArray();
                if (visibleRequirements.Length == 0)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                ScreenshotCatalogSlot[] categorySlots = category.requirements
                    .SelectMany(item => item.slots)
                    .Where(slot => slot.required)
                    .ToArray();
                if (categorySlots.Length == 0)
                {
                    categorySlots = category.requirements.SelectMany(item => item.slots).ToArray();
                }
                int completeCount = category.requirements.Sum(item => item.slots.Count(slot =>
                    categorySlots.Contains(slot) && ScreenshotCatalogUtility.GetStatus(item, slot) == ScreenshotCatalogStatus.Complete));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    Tip(category.name + (category.obsolete ? " (Obsolete)" : string.Empty), "Catalog category copied from the master template. Obsolete categories are retained because they contain capture history."),
                    EditorStyles.boldLabel);
                GUILayout.Label(
                    Tip(completeCount + " / " + categorySlots.Length + " complete", "Number of required slots in this category whose workflow is complete."),
                    EditorStyles.miniLabel,
                    GUILayout.Width(92f));
                EditorGUILayout.EndHorizontal();
                foreach (ScreenshotCatalogRequirement requirement in visibleRequirements)
                {
                    DrawRequirement(category, requirement);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }

        private void DrawRequirement(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement)
        {
            EditorGUILayout.BeginVertical("box");
            ScreenshotCatalogStatus aggregateStatus = GetAggregateStatus(requirement);
            bool armedRequirement = requirement.definitionId == armedRequirementId;
            bool expanded = expandedRequirementId == requirement.definitionId || armedRequirement;
            bool singleSlot = requirement.slots.Count == 1;
            EditorGUILayout.BeginHorizontal();
            bool newExpanded = EditorGUILayout.Foldout(
                expanded,
                Tip(
                    requirement.name + (requirement.obsolete ? " (Obsolete)" : string.Empty),
                    string.IsNullOrWhiteSpace(requirement.guidance)
                        ? "Expand this requirement to assign assets and manage versions."
                        : requirement.guidance),
                true);
            if (newExpanded != expanded)
            {
                expandedRequirementId = newExpanded ? requirement.definitionId : string.Empty;
                expanded = newExpanded;
            }
            DrawStatusBadge(aggregateStatus, ScreenshotCatalogUtility.GetStatusLabel(aggregateStatus));
            if (singleSlot && !requirement.obsolete && !requirement.slots[0].obsolete &&
                requirement.workflow != ScreenshotAssetWorkflow.External)
            {
                ScreenshotCatalogSlot onlySlot = requirement.slots[0];
                bool isArmed = IsArmed(category, requirement, onlySlot);
                if (GUILayout.Button(
                        Tip(
                            isArmed ? "Armed" : "Arm",
                            isArmed
                                ? "Disarm this slot without changing any assigned images."
                                : "Target this slot for the next managed capture and apply its configured resolution."),
                        EditorStyles.miniButton,
                        GUILayout.Width(54f)))
                {
                    ToggleArmedSlot(category, requirement, onlySlot);
                    if (!isArmed)
                    {
                        expandedRequirementId = requirement.definitionId;
                        expanded = true;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            Rect headerRect = GUILayoutUtility.GetLastRect();

            string specification = ScreenshotCatalogGUI.GetSpecification(requirement);
            EditorGUILayout.LabelField(
                Tip(specification, "Workflow, target dimensions, dimension validation rule, and required PNG format."),
                EditorStyles.miniLabel);
            if (!expanded)
            {
                EditorGUILayout.EndVertical();
                ScrollToRequirementIfRequested(requirement, headerRect);
                return;
            }

            if (!string.IsNullOrWhiteSpace(requirement.guidance))
            {
                EditorGUILayout.HelpBox(requirement.guidance, MessageType.None);
            }
            if (!string.IsNullOrWhiteSpace(requirement.sourceUrl))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        Tip("Open Source Guidelines", "Open the specification URL stored by the master template."),
                        EditorStyles.miniButton,
                        GUILayout.Width(145f)))
                {
                    Application.OpenURL(requirement.sourceUrl);
                }
                EditorGUILayout.EndHorizontal();
            }

            bool showSlotNames = requirement.slots.Count > 1 ||
                                 requirement.slots.Any(item => !string.Equals(item.name, requirement.name, StringComparison.OrdinalIgnoreCase));
            foreach (ScreenshotCatalogSlot slot in requirement.slots)
            {
                DrawSlot(category, requirement, slot, showSlotNames, singleSlot);
            }
            EditorGUILayout.EndVertical();
            ScrollToRequirementIfRequested(requirement, headerRect);
        }

        private void DrawSlot(
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot,
            bool showSlotName,
            bool singleSlot)
        {
            ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
            if (!singleSlot)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    Tip((showSlotName ? slot.name : "Asset") + (slot.required ? " *" : string.Empty), slot.required
                        ? "Required image slot. It contributes to the catalog's missing count until complete."
                        : "Optional image slot. It does not contribute to the catalog's missing count."),
                    GUILayout.MinWidth(150));
                DrawStatusBadge(status, ScreenshotCatalogUtility.GetStatusLabel(status));

                bool canCapture = !requirement.obsolete && !slot.obsolete && requirement.workflow != ScreenshotAssetWorkflow.External;
                if (canCapture)
                {
                    bool isArmed = IsArmed(category, requirement, slot);
                    if (GUILayout.Button(
                            Tip(
                                isArmed ? "Armed" : "Arm",
                                isArmed
                                    ? "Disarm this slot without changing any assigned images."
                                    : "Target this slot for the next managed capture and apply its configured resolution."),
                            GUILayout.Width(60)))
                    {
                        ToggleArmedSlot(category, requirement, slot);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            if (requirement.workflow != ScreenshotAssetWorkflow.External)
            {
                DrawVersionField("Source", slot, false, slot.sourceVersions, slot.activeSource, texture =>
                {
                    Undo.RecordObject(catalog, "Change Active Screenshot Source");
                    ScreenshotCatalogUtility.AddSourceVersion(slot, texture);
                }, index => ScreenshotCatalogUtility.RemoveSourceVersion(slot, index));
            }

            if (requirement.workflow != ScreenshotAssetWorkflow.Capture)
            {
                DrawVersionField("Final", slot, true, slot.finalVersions, slot.activeFinal, texture =>
                {
                    Undo.RecordObject(catalog, "Change Final Screenshot Asset");
                    ScreenshotCatalogUtility.AddFinalVersion(slot, texture);
                }, index => ScreenshotCatalogUtility.RemoveFinalVersion(slot, index));
            }

            Texture2D active = requirement.workflow == ScreenshotAssetWorkflow.Capture ? slot.activeSource : slot.activeFinal;
            if (active != null && !ScreenshotCatalogUtility.ValidateTexture(active, requirement, out string validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
            }
            if (!singleSlot)
            {
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawVersionField(
            string label,
            ScreenshotCatalogSlot slot,
            bool finalAsset,
            System.Collections.Generic.List<Texture2D> versions,
            Texture2D active,
            Action<Texture2D> assign,
            Action<int> remove)
        {
            EditorGUI.BeginChangeCheck();
            string assetTooltip = label == "Source"
                ? "Active raw capture for this slot. Assign an existing PNG or capture a new managed version."
                : "Active externally prepared final PNG. Capture Then Final and External workflows require this asset for completion.";
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Tip(label, assetTooltip));
            Texture2D selected = (Texture2D)EditorGUILayout.ObjectField(
                active,
                typeof(Texture2D),
                false,
                GUILayout.Width(72f),
                GUILayout.Height(64f));
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                assign(selected);
                EditorUtility.SetDirty(catalog);
            }

            if (versions.Count == 0)
            {
                return;
            }

            string[] labels = versions.Select((texture, index) => texture == null ? "Missing v" + (index + 1) : texture.name).ToArray();
            int currentIndex = versions.IndexOf(active);
            if (currentIndex < 0)
            {
                currentIndex = versions.Count - 1;
            }
            EditorGUILayout.BeginHorizontal();
            int newIndex = EditorGUILayout.Popup(
                Tip(label + " Version", "Select which retained " + label.ToLowerInvariant() + " version is active for this slot."),
                currentIndex,
                labels);
            if (newIndex != currentIndex)
            {
                assign(versions[newIndex]);
                EditorUtility.SetDirty(catalog);
            }
            Texture2D selectedVersion = versions[newIndex];
            bool tracked = ScreenshotCatalogUtility.IsVersionTracked(slot, finalAsset, selectedVersion);
            bool synced = tracked && ScreenshotGoogleDriveService.IsVersionSynced(catalog, slot, finalAsset, selectedVersion);
            Color previousColor = GUI.color;
            GUI.color = synced
                ? new Color(0.35f, 0.8f, 0.4f)
                : tracked ? new Color(0.95f, 0.75f, 0.25f) : Color.gray;
            GUILayout.Label(
                Tip(synced ? "Drive" : tracked ? "Pending" : "Untracked", synced
                    ? "This version has a Google Drive file binding for the selected sync profile."
                    : tracked
                        ? "This version is tracked and will be uploaded by the selected sync profile."
                        : "This version remains in catalog history but is excluded from Google Drive sync."),
                EditorStyles.miniBoldLabel,
                GUILayout.Width(58f));
            GUI.color = previousColor;
            if (GUILayout.Button(
                    Tip(tracked ? "Untrack" : "Track", tracked
                        ? "Exclude this version from future Drive sync without removing its catalog history, local PNG, or existing Drive binding."
                        : "Include this version in Google Drive sync. An existing binding will be reused when available."),
                    GUILayout.Width(62f)))
            {
                Undo.RecordObject(catalog, tracked ? "Untrack Screenshot Version" : "Track Screenshot Version");
                ScreenshotCatalogUtility.SetVersionTracked(slot, finalAsset, selectedVersion, !tracked);
                EditorUtility.SetDirty(catalog);
                RepaintContainers();
            }
            if (GUILayout.Button(
                    Tip("Remove", "Remove this version from catalog history without deleting its project file or existing Drive file."),
                    GUILayout.Width(58f)))
            {
                Undo.RecordObject(catalog, "Remove Screenshot Version");
                remove(newIndex);
                EditorUtility.SetDirty(catalog);
                RepaintContainers();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void Arm(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            armedCategoryId = category.definitionId;
            armedRequirementId = requirement.definitionId;
            armedSlotId = slot.definitionId;
            ApplyGameViewResolution(requirement);
            AutoSelectCamera();
            if (EditorApplication.isPlaying && useScreenshotter)
            {
                ApplyRequirement(EnsureRuntimeScreenshotter(), requirement);
            }
        }

        private void ToggleArmedSlot(
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot)
        {
            if (IsArmed(category, requirement, slot))
            {
                ClearArmedSlot();
            }
            else
            {
                Arm(category, requirement, slot);
            }
            RepaintContainers();
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

            string assetPath = ScreenshotCatalogUtility.GetNextCaptureAssetPath(catalog, category, requirement, slot);
            string absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

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
            ScreenshotCatalogUtility.AddSourceVersion(slot, texture, true);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            RepaintContainers();
            Debug.Log("Captured catalog image: " + assetPath, texture);
        }

        internal void EnsureSelectedSlot()
        {
            if (TryGetSelectedSlot(out _, out _, out _))
            {
                return;
            }

            ScreenshotCatalogCategory armedCategory;
            ScreenshotCatalogRequirement armedRequirement;
            ScreenshotCatalogSlot armedSlot;
            if (TryGetArmedSlot(out armedCategory, out armedRequirement, out armedSlot))
            {
                SelectSlot(armedCategory, armedRequirement, armedSlot, false);
                return;
            }

            if (catalog == null)
            {
                ClearSelectedSlot();
                return;
            }

            var entries = catalog.categories
                .SelectMany(category => category.requirements.SelectMany(requirement =>
                    requirement.slots.Select(slot => new { category, requirement, slot })))
                .ToArray();
            var preferred = entries.FirstOrDefault(item =>
                                ScreenshotCatalogUtility.GetStatus(item.requirement, item.slot) == ScreenshotCatalogStatus.Invalid) ??
                            entries.FirstOrDefault(item => item.slot.required &&
                                ScreenshotCatalogUtility.GetStatus(item.requirement, item.slot) == ScreenshotCatalogStatus.Missing) ??
                            entries.FirstOrDefault(item => !item.category.obsolete && !item.requirement.obsolete && !item.slot.obsolete) ??
                            entries.FirstOrDefault();
            if (preferred == null)
            {
                ClearSelectedSlot();
                return;
            }
            SelectSlot(preferred.category, preferred.requirement, preferred.slot, false);
        }

        internal bool TryGetSelectedSlot(
            out ScreenshotCatalogCategory category,
            out ScreenshotCatalogRequirement requirement,
            out ScreenshotCatalogSlot slot)
        {
            category = null;
            requirement = null;
            slot = null;
            if (catalog == null || string.IsNullOrEmpty(selectedSlotId))
            {
                return false;
            }
            category = catalog.categories.FirstOrDefault(item => item.definitionId == selectedCategoryId);
            requirement = category == null
                ? null
                : category.requirements.FirstOrDefault(item => item.definitionId == selectedRequirementId);
            slot = requirement == null
                ? null
                : requirement.slots.FirstOrDefault(item => item.definitionId == selectedSlotId);
            return category != null && requirement != null && slot != null;
        }

        private bool IsSelected(
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot)
        {
            return category.definitionId == selectedCategoryId &&
                   requirement.definitionId == selectedRequirementId &&
                   slot.definitionId == selectedSlotId;
        }

        internal void SelectSlot(
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot,
            bool repaint = true)
        {
            selectedCategoryId = category.definitionId;
            selectedRequirementId = requirement.definitionId;
            selectedSlotId = slot.definitionId;
            showSettingsPage = false;
            if (repaint)
            {
                RepaintContainers();
            }
        }

        private void ClearSelectedSlot()
        {
            selectedCategoryId = null;
            selectedRequirementId = null;
            selectedSlotId = null;
        }

        private bool MatchesSlotFilter(
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot)
        {
            string search = (requirementSearch ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(search) &&
                category.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                requirement.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                slot.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
            switch (requirementFilter)
            {
                case ScreenshotRequirementFilter.Missing:
                    return slot.required && status == ScreenshotCatalogStatus.Missing;
                case ScreenshotRequirementFilter.SourceReady:
                    return status == ScreenshotCatalogStatus.SourceReady;
                case ScreenshotRequirementFilter.Complete:
                    return status == ScreenshotCatalogStatus.Complete;
                case ScreenshotRequirementFilter.Invalid:
                    return status == ScreenshotCatalogStatus.Invalid;
                case ScreenshotRequirementFilter.Obsolete:
                    return status == ScreenshotCatalogStatus.Obsolete;
                default:
                    return true;
            }
        }

        internal void SelectNextMissingSlot()
        {
            if (catalog == null)
            {
                return;
            }
            var missing = catalog.categories
                .SelectMany(category => category.requirements.SelectMany(requirement => requirement.slots
                    .Where(slot => slot.required &&
                                   ScreenshotCatalogUtility.GetStatus(requirement, slot) == ScreenshotCatalogStatus.Missing)
                    .Where(slot => string.IsNullOrWhiteSpace(requirementSearch) ||
                                   category.name.IndexOf(requirementSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   requirement.name.IndexOf(requirementSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   slot.name.IndexOf(requirementSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(slot => new { category, requirement, slot })))
                .ToArray();
            if (missing.Length == 0)
            {
                return;
            }
            int current = Array.FindIndex(missing, item => IsSelected(item.category, item.requirement, item.slot));
            var next = missing[(current + 1) % missing.Length];
            requirementFilter = ScreenshotRequirementFilter.Missing;
            SelectSlot(next.category, next.requirement, next.slot);
        }

        private static string GetSlotDisplayName(ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            if (requirement.slots.Count == 1 &&
                string.Equals(requirement.name, slot.name, StringComparison.OrdinalIgnoreCase))
            {
                return requirement.name;
            }
            return requirement.slots.Count == 1 ? requirement.name : slot.name;
        }

        private static string GetSlotWorkspaceTitle(ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            return requirement.slots.Count == 1 || string.Equals(requirement.name, slot.name, StringComparison.OrdinalIgnoreCase)
                ? requirement.name
                : requirement.name + " — " + slot.name;
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
                RepaintContainers();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                runtimeScreenshotter = null;
                cameraSetupWarning = null;
                UnbindCaptureAction();
                RepaintContainers();
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

        private void ApplyArmedGameViewResolution()
        {
            ScreenshotCatalogCategory category;
            ScreenshotCatalogRequirement requirement;
            ScreenshotCatalogSlot slot;
            if (TryGetArmedSlot(out category, out requirement, out slot))
            {
                ApplyGameViewResolution(requirement);
            }
        }

        private void ApplyGameViewResolution(ScreenshotCatalogRequirement requirement, bool force = false)
        {
            if ((!matchGameViewResolution && !force) || requirement == null)
            {
                return;
            }

            if (!GameViewResolutionUtility.TrySetResolution(requirement.width, requirement.height, out gameViewResolutionWarning))
            {
                Debug.LogWarning(gameViewResolutionWarning);
            }
            RepaintContainers();
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

        internal static bool TryConvertToAssetFolder(string absoluteFolder, out string assetFolder)
        {
            assetFolder = null;
            if (string.IsNullOrWhiteSpace(absoluteFolder))
            {
                return false;
            }

            try
            {
                string assetsRoot = Path.GetFullPath(Application.dataPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string selected = Path.GetFullPath(absoluteFolder)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(selected, assetsRoot, StringComparison.OrdinalIgnoreCase))
                {
                    assetFolder = "Assets";
                    return true;
                }

                string prefix = assetsRoot + Path.DirectorySeparatorChar;
                if (!selected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                assetFolder = ("Assets/" + selected.Substring(prefix.Length)).Replace('\\', '/');
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static ScreenshotCatalogStatus GetAggregateStatus(ScreenshotCatalogRequirement requirement)
        {
            if (requirement.obsolete)
            {
                return ScreenshotCatalogStatus.Obsolete;
            }

            ScreenshotCatalogSlot[] relevantSlots = requirement.slots.Where(slot => slot.required).ToArray();
            if (relevantSlots.Length == 0)
            {
                relevantSlots = requirement.slots.ToArray();
            }
            ScreenshotCatalogStatus[] statuses = relevantSlots
                .Select(slot => ScreenshotCatalogUtility.GetStatus(requirement, slot))
                .ToArray();
            if (statuses.Any(status => status == ScreenshotCatalogStatus.Invalid))
            {
                return ScreenshotCatalogStatus.Invalid;
            }
            if (statuses.Any(status => status == ScreenshotCatalogStatus.Missing))
            {
                return ScreenshotCatalogStatus.Missing;
            }
            if (statuses.Any(status => status == ScreenshotCatalogStatus.SourceReady))
            {
                return ScreenshotCatalogStatus.SourceReady;
            }
            return statuses.Length == 0 ? ScreenshotCatalogStatus.Missing : ScreenshotCatalogStatus.Complete;
        }

        private bool MatchesRequirementFilter(ScreenshotCatalogRequirement requirement)
        {
            string search = (requirementSearch ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(search) &&
                requirement.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                !requirement.slots.Any(slot => slot.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            if (requirementFilter == ScreenshotRequirementFilter.All)
            {
                return true;
            }

            ScreenshotCatalogStatus aggregate = GetAggregateStatus(requirement);
            switch (requirementFilter)
            {
                case ScreenshotRequirementFilter.Missing:
                    return requirement.slots.Any(slot => slot.required &&
                        ScreenshotCatalogUtility.GetStatus(requirement, slot) == ScreenshotCatalogStatus.Missing);
                case ScreenshotRequirementFilter.SourceReady:
                    return requirement.slots.Any(slot =>
                        ScreenshotCatalogUtility.GetStatus(requirement, slot) == ScreenshotCatalogStatus.SourceReady);
                case ScreenshotRequirementFilter.Complete:
                    return aggregate == ScreenshotCatalogStatus.Complete;
                case ScreenshotRequirementFilter.Invalid:
                    return aggregate == ScreenshotCatalogStatus.Invalid;
                case ScreenshotRequirementFilter.Obsolete:
                    return requirement.obsolete;
                default:
                    return true;
            }
        }

        private void SelectNextMissing(ScreenshotCatalogCategory[] categories)
        {
            ScreenshotCatalogRequirement[] missingRequirements = categories
                .SelectMany(category => category.requirements)
                .Where(requirement => string.IsNullOrWhiteSpace(requirementSearch) ||
                    requirement.name.IndexOf(requirementSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    requirement.slots.Any(slot => slot.name.IndexOf(requirementSearch, StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(requirement => requirement.slots.Any(slot => slot.required &&
                    ScreenshotCatalogUtility.GetStatus(requirement, slot) == ScreenshotCatalogStatus.Missing))
                .ToArray();
            if (missingRequirements.Length == 0)
            {
                return;
            }

            int currentIndex = Array.FindIndex(missingRequirements, requirement => requirement.definitionId == expandedRequirementId);
            ScreenshotCatalogRequirement next = missingRequirements[(currentIndex + 1) % missingRequirements.Length];
            requirementFilter = ScreenshotRequirementFilter.Missing;
            expandedRequirementId = next.definitionId;
            pendingScrollRequirementId = next.definitionId;
            RepaintContainers();
        }

        private void ScrollToRequirementIfRequested(ScreenshotCatalogRequirement requirement, Rect headerRect)
        {
            if (pendingScrollRequirementId != requirement.definitionId ||
                Event.current.type != EventType.Repaint ||
                catalogScrollView == null)
            {
                return;
            }

            Vector2 currentOffset = catalogScrollView.scrollOffset;
            catalogScrollView.scrollOffset = new Vector2(currentOffset.x, Mathf.Max(0f, headerRect.y - 8f));
            pendingScrollRequirementId = null;
        }

        private static bool DrawSectionFoldout(bool expanded, GUIContent content)
        {
            GUIStyle style = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold
            };
            return EditorGUILayout.Foldout(expanded, content, true, style);
        }

        private static void DrawStatusBadge(ScreenshotCatalogStatus status, string label)
        {
            ScreenshotCatalogGUI.DrawStatusBadge(status, label);
        }

        private static void DrawTextBadge(string label, Color color, string tooltip)
        {
            ScreenshotCatalogGUI.DrawTextBadge(label, color, tooltip);
        }

        private static GUIContent Tip(string text, string tooltip)
        {
            return new GUIContent(text, tooltip);
        }

        private void RepaintContainers()
        {
            if (windowView != null)
            {
                windowView.MarkDirtyRepaint();
                Repaint();
                return;
            }
            if (headerContainer != null)
            {
                headerContainer.MarkDirtyRepaint();
            }
            if (catalogContainer != null)
            {
                catalogContainer.MarkDirtyRepaint();
            }
            Repaint();
        }
    }
}
