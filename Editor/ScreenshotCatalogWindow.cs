using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SkatanicStudios
{
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
        [SerializeField] private bool showGoogleDriveSync;
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
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            headerContainer = new IMGUIContainer(DrawHeaderGUI);
            headerContainer.style.flexShrink = 0;
            rootVisualElement.Add(headerContainer);

            catalogScrollView = new ScrollView();
            catalogScrollView.name = "screenshot-catalog-scroll-view";
            catalogScrollView.viewDataKey = "ScreenshotCatalogWindow.ScrollView";
            catalogScrollView.style.flexGrow = 1;
            catalogScrollView.style.flexShrink = 1;

            catalogContainer = new IMGUIContainer(DrawCatalogGUI);
            catalogContainer.style.flexGrow = 1;
            catalogScrollView.Add(catalogContainer);
            rootVisualElement.Add(catalogScrollView);
        }

        private void DrawHeaderGUI()
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
            DrawGoogleDriveSync();
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
            EditorGUILayout.LabelField(Tip("Assets", "Select the catalog and master template that define this capture session."), EditorStyles.boldLabel);
            ScreenshotCatalog newCatalog = (ScreenshotCatalog)EditorGUILayout.ObjectField(
                Tip("Catalog", "The per-project, campaign, or release asset that tracks required slots, capture history, and final deliverables."),
                catalog,
                typeof(ScreenshotCatalog),
                false);
            if (newCatalog != catalog)
            {
                catalog = newCatalog;
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
            ClearArmedSlot();
        }

        private void DrawCatalogConfiguration()
        {
            EditorGUILayout.LabelField(Tip("Catalog Configuration", "Choose where managed captures are saved and which template categories this catalog tracks."), EditorStyles.boldLabel);
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
            EditorGUILayout.LabelField(Tip("Capture", "Configure the scene camera and input used to capture the armed catalog slot."), EditorStyles.boldLabel);
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

            ScreenshotCatalogRequirement requirement;
            ScreenshotCatalogSlot slot;
            ScreenshotCatalogCategory category;
            bool armed = TryGetArmedSlot(out category, out requirement, out slot);
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
            string captureButtonLabel = "Capture Armed Slot";
            if (captureActionReference != null && captureActionReference.action != null)
            {
                captureButtonLabel += " (" + captureActionReference.action.name + ")";
            }
            else if (useScreenshotter)
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
        }

        private void DrawGoogleDriveSync()
        {
            showGoogleDriveSync = EditorGUILayout.Foldout(
                showGoogleDriveSync,
                Tip("Google Drive Sync", "Configure and push locally tracked screenshot versions to Google Drive."),
                true);
            if (!showGoogleDriveSync)
            {
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
            EditorGUILayout.LabelField(
                Tip("Connection", "Whether this editor has a stored OAuth refresh token for the selected profile."),
                Tip(!configured ? "Profile not configured" : authorized ? "Connected" : "Not connected", "OAuth tokens are stored locally under Library/Screenshotter."));
            EditorGUILayout.LabelField(
                Tip("Pending Uploads", "Tracked source and final versions that do not yet have a Google Drive file binding for this profile."),
                pending.ToString());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(Tip("Show Profile", "Select the sync profile in the Inspector to load OAuth credentials or change the destination folder.")))
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }

            EditorGUI.BeginDisabledGroup(googleDriveBusy || !configured);
            if (!authorized)
            {
                if (GUILayout.Button(Tip("Connect", "Open Google authorization in the system browser and store the resulting token locally for this project.")))
                {
                    ConnectGoogleDrive(profile);
                }
            }
            else if (GUILayout.Button(Tip("Disconnect", "Remove the locally stored OAuth token. This does not revoke access in Google or change uploaded files.")))
            {
                ScreenshotGoogleDriveService.Disconnect(profile);
                googleDriveMessage = "Disconnected from Google Drive on this editor.";
                googleDriveMessageType = MessageType.Info;
                RepaintContainers();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button(Tip("Open Folder", "Open the configured destination folder in Google Drive.")))
            {
                Application.OpenURL(ScreenshotGoogleDriveService.GetFolderUrl(profile));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(googleDriveBusy || !configured || !authorized || pending == 0);
            if (GUILayout.Button(
                    Tip("Push New (" + pending + ")", "Upload every locally tracked version that has not been uploaded through this profile. Existing Drive files are never overwritten."),
                    GUILayout.Height(26f)))
            {
                PushNewToGoogleDrive();
            }
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(googleDriveMessage))
            {
                EditorGUILayout.HelpBox(googleDriveMessage, googleDriveMessageType);
            }
            EditorGUI.indentLevel--;
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

        private void DrawCatalogContents()
        {
            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    Tip(category.name + (category.obsolete ? " (Obsolete)" : string.Empty), "Catalog category copied from the master template. Obsolete categories are retained because they contain capture history."),
                    EditorStyles.boldLabel);
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
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
            string specification = string.Format("{0} — {1}x{2} ({3}) — {4}", requirement.workflow, requirement.width, requirement.height, requirement.dimensionRule, requirement.imageFormat);
            EditorGUILayout.LabelField(
                Tip(requirement.name + (requirement.obsolete ? " (Obsolete)" : string.Empty), "Deliverable requirement copied from the master template."),
                EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                Tip(specification, "Workflow, target dimensions, dimension validation rule, and required PNG format."),
                EditorStyles.miniLabel);
            if (!string.IsNullOrWhiteSpace(requirement.guidance))
            {
                EditorGUILayout.HelpBox(requirement.guidance, MessageType.None);
            }
            if (!string.IsNullOrWhiteSpace(requirement.sourceUrl) && GUILayout.Button(
                    Tip("Open Source Guidelines", "Open the specification URL stored by the master template."),
                    EditorStyles.miniButton))
            {
                Application.OpenURL(requirement.sourceUrl);
            }

            for (int index = 0; index < requirement.slots.Count; index++)
            {
                ScreenshotCatalogSlot slot = requirement.slots[index];
                DrawSlot(category, requirement, slot, index);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSlot(ScreenshotCatalogCategory category, ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot, int slotIndex)
        {
            ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                Tip(slot.name + (slot.required ? " *" : string.Empty), slot.required
                    ? "Required image slot. It contributes to the catalog's missing count until complete."
                    : "Optional image slot. It does not contribute to the catalog's missing count."),
                GUILayout.MinWidth(150));
            Color previousColor = GUI.color;
            GUI.color = GetStatusColor(status);
            GUILayout.Label(
                Tip(ScreenshotCatalogUtility.GetStatusLabel(status), GetStatusTooltip(status)),
                EditorStyles.miniBoldLabel,
                GUILayout.Width(85));
            GUI.color = previousColor;

            bool canCapture = !requirement.obsolete && !slot.obsolete && requirement.workflow != ScreenshotAssetWorkflow.External;
            EditorGUI.BeginDisabledGroup(!canCapture);
            bool isArmed = IsArmed(category, requirement, slot);
            if (GUILayout.Button(
                    Tip(isArmed ? "Armed" : "Arm", "Target this slot for the next managed capture and apply its configured resolution."),
                    GUILayout.Width(60)))
            {
                Arm(category, requirement, slot);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (requirement.workflow != ScreenshotAssetWorkflow.External)
            {
                DrawVersionField("Source", slot, false, slot.sourceVersions, slot.activeSource, texture =>
                {
                    Undo.RecordObject(catalog, "Change Active Screenshot Source");
                    ScreenshotCatalogUtility.AddSourceVersion(slot, texture);
                }, index =>
                {
                    Undo.RecordObject(catalog, "Untrack Screenshot Source Version");
                    ScreenshotCatalogUtility.RemoveSourceVersion(slot, index);
                });
            }

            if (requirement.workflow != ScreenshotAssetWorkflow.Capture)
            {
                DrawVersionField("Final", slot, true, slot.finalVersions, slot.activeFinal, texture =>
                {
                    Undo.RecordObject(catalog, "Change Final Screenshot Asset");
                    ScreenshotCatalogUtility.AddFinalVersion(slot, texture);
                }, index =>
                {
                    Undo.RecordObject(catalog, "Untrack Final Screenshot Version");
                    ScreenshotCatalogUtility.RemoveFinalVersion(slot, index);
                });
            }

            Texture2D active = requirement.workflow == ScreenshotAssetWorkflow.Capture ? slot.activeSource : slot.activeFinal;
            if (active != null && !ScreenshotCatalogUtility.ValidateTexture(active, requirement, out string validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
            }
            EditorGUILayout.EndVertical();
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
            Texture2D selected = (Texture2D)EditorGUILayout.ObjectField(
                Tip(label, assetTooltip),
                active,
                typeof(Texture2D),
                false);
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
            bool synced = ScreenshotGoogleDriveService.IsVersionSynced(catalog, slot, finalAsset, versions[newIndex]);
            Color previousColor = GUI.color;
            GUI.color = synced ? new Color(0.35f, 0.8f, 0.4f) : new Color(0.95f, 0.75f, 0.25f);
            GUILayout.Label(
                Tip(synced ? "Drive" : "Local", synced
                    ? "This version has a Google Drive file binding for the selected sync profile."
                    : "This version has not been uploaded through the selected sync profile."),
                EditorStyles.miniBoldLabel,
                GUILayout.Width(38f));
            GUI.color = previousColor;
            if (GUILayout.Button(
                    Tip("Untrack", "Remove the selected version from this catalog's history without deleting its PNG file from the project."),
                    GUILayout.Width(62f)))
            {
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
            RepaintContainers();
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

        private void ApplyGameViewResolution(ScreenshotCatalogRequirement requirement)
        {
            if (!matchGameViewResolution || requirement == null)
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

        private static string GetStatusTooltip(ScreenshotCatalogStatus status)
        {
            switch (status)
            {
                case ScreenshotCatalogStatus.Complete:
                    return "This slot has the valid asset required by its workflow.";
                case ScreenshotCatalogStatus.SourceReady:
                    return "A valid source capture exists, but this workflow still requires a valid final image.";
                case ScreenshotCatalogStatus.Invalid:
                    return "The active asset is missing from disk or does not match the required dimensions, PNG format, or transparency setting.";
                case ScreenshotCatalogStatus.Obsolete:
                    return "This entry no longer exists in the synchronized template but is retained because it contains image history.";
                default:
                    return "This required slot does not yet have the asset needed by its workflow.";
            }
        }

        private static GUIContent Tip(string text, string tooltip)
        {
            return new GUIContent(text, tooltip);
        }

        private void RepaintContainers()
        {
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
