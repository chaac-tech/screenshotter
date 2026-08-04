using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SkatanicStudios
{
    internal sealed class ScreenshotCatalogTests
    {
        private const string TestFolder = "Assets/__ScreenshotterCatalogTests";

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, TestFolder));
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            ScreenshotterCaptureBridge.ManagedCaptureRequested = null;
            AssetDatabase.DeleteAsset(TestFolder);
        }

        [Test]
        public void MetaPresetContainsFiveScreenshotSlotsAndStableIds()
        {
            ScreenshotTemplate template = ScreenshotTemplatePresets.CreateMetaMasterTemplate();
            ScreenshotCategoryDefinition meta = template.categories.Single(category => category.name == "Meta Distribution");
            ScreenshotRequirementDefinition screenshots = meta.requirements.Single(requirement => requirement.name == "Screenshot");

            Assert.That(screenshots.slots.Count, Is.EqualTo(5));
            Assert.That(template.categories.All(category => !string.IsNullOrEmpty(category.id)), Is.True);
            Assert.That(meta.requirements.All(requirement => !string.IsNullOrEmpty(requirement.id)), Is.True);
            Assert.That(meta.requirements.SelectMany(requirement => requirement.slots).All(slot => !string.IsNullOrEmpty(slot.id)), Is.True);
            string[] ids = template.categories.Select(category => category.id)
                .Concat(template.categories.SelectMany(category => category.requirements).Select(requirement => requirement.id))
                .Concat(template.categories.SelectMany(category => category.requirements).SelectMany(requirement => requirement.slots).Select(slot => slot.id))
                .ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
            Object.DestroyImmediate(template);
        }

        [Test]
        public void CatalogAssetsAreDiscoverableByUnityTypeSearch()
        {
            Assert.That(typeof(ScreenshotTemplate).IsPublic, Is.True);
            Assert.That(typeof(ScreenshotCatalog).IsPublic, Is.True);
            string assetPath = TestFolder + "/discoverable-catalog.asset";
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            AssetDatabase.CreateAsset(catalog, assetPath);
            AssetDatabase.SaveAssets();

            string[] matches = AssetDatabase.FindAssets("t:ScreenshotCatalog", new[] { TestFolder });

            Assert.That(matches.Length, Is.EqualTo(1));
            Assert.That(AssetDatabase.GUIDToAssetPath(matches[0]), Is.EqualTo(assetPath));
        }

        [Test]
        public void SynchronizePreservesVersionsAndMarksRemovedEntriesObsolete()
        {
            ScreenshotTemplate template = ScreenshotTemplatePresets.CreateMetaMasterTemplate();
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            ScreenshotCatalogRequirement requirement = catalog.categories[0].requirements[0];
            ScreenshotCatalogSlot slot = requirement.slots[0];
            Texture2D version = new Texture2D(2, 2);
            ScreenshotCatalogUtility.AddSourceVersion(slot, version);

            template.categories[0].requirements.RemoveAt(0);
            ScreenshotCatalogUtility.Synchronize(catalog);

            ScreenshotCatalogRequirement obsoleteRequirement = catalog.categories
                .SelectMany(category => category.requirements)
                .Single(item => item.obsolete);
            ScreenshotCatalogSlot obsoleteSlot = obsoleteRequirement.slots.Single();
            Assert.That(obsoleteRequirement.obsolete, Is.True);
            Assert.That(obsoleteSlot.sourceVersions.Single(), Is.SameAs(version));
            Assert.That(obsoleteSlot.activeSource, Is.SameAs(version));
            Object.DestroyImmediate(version);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void SynchronizePrunesUncheckedCategoryWhenItHasNoAssignedImages()
        {
            ScreenshotTemplate template = ScreenshotTemplatePresets.CreateMetaMasterTemplate();
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            string categoryId = catalog.categories[0].definitionId;

            catalog.includedCategoryIds.Remove(categoryId);
            ScreenshotCatalogUtility.Synchronize(catalog);

            Assert.That(catalog.categories.Any(category => category.definitionId == categoryId), Is.False);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void PruneEmptyObsoleteKeepsEntriesWithVersionHistory()
        {
            ScreenshotTemplate template = ScreenshotTemplatePresets.CreateMetaMasterTemplate();
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            ScreenshotCatalogSlot slot = catalog.categories[0].requirements[0].slots[0];
            Texture2D version = new Texture2D(2, 2);
            ScreenshotCatalogUtility.AddSourceVersion(slot, version);
            catalog.requirementStates.Add(new ScreenshotCatalogRequirementState
            {
                definitionId = "empty",
                slots = { new ScreenshotCatalogSlotState { definitionId = "empty-slot" } }
            });

            int removed = ScreenshotCatalogUtility.PruneEmptyObsolete(catalog);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(catalog.requirementStates.Count, Is.EqualTo(1));
            Assert.That(catalog.requirementStates[0].slots.Single().sourceVersions.Single(), Is.SameAs(version));
            Object.DestroyImmediate(version);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void EmptyCatalogSerializesWithoutTemplateRequirementSnapshots()
        {
            ScreenshotTemplate template = ScreenshotTemplatePresets.CreateMetaMasterTemplate();
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            string templatePath = TestFolder + "/compact-template.asset";
            string catalogPath = TestFolder + "/compact-catalog.asset";
            AssetDatabase.CreateAsset(template, templatePath);
            AssetDatabase.CreateAsset(catalog, catalogPath);
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            AssetDatabase.SaveAssets();

            string yaml = File.ReadAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, catalogPath));

            Assert.That(yaml, Does.Contain("requirementStates: []"));
            Assert.That(yaml, Does.Not.Contain("guidance:"));
            Assert.That(yaml, Does.Not.Contain("sourceVersions:"));
            Assert.That(yaml, Does.Not.Contain("categories:"));
        }

        [Test]
        public void PopulatedCatalogSerializesOnlyItsTrackedSlotState()
        {
            string imagePath = TestFolder + "/tracked.png";
            CreatePngAsset(imagePath, 8, 8);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotTemplate template = ScreenshotTemplatePresets.CreateMetaMasterTemplate();
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            string templatePath = TestFolder + "/tracked-template.asset";
            string catalogPath = TestFolder + "/tracked-catalog.asset";
            AssetDatabase.CreateAsset(template, templatePath);
            AssetDatabase.CreateAsset(catalog, catalogPath);
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            ScreenshotCatalogSlot slot = catalog.categories[0].requirements[0].slots[0];
            ScreenshotCatalogUtility.AddSourceVersion(slot, texture);
            AssetDatabase.SaveAssets();

            string yaml = File.ReadAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, catalogPath));

            Assert.That(catalog.requirementStates.Count, Is.EqualTo(1));
            Assert.That(catalog.requirementStates[0].slots.Count, Is.EqualTo(1));
            Assert.That(yaml, Does.Contain("activeSourceIndex: 0"));
            Assert.That(yaml, Does.Not.Contain("activeSource:"));
            Assert.That(yaml, Does.Not.Contain("webViewLink:"));
            Assert.That(catalog.categories[0].requirements[0].slots[0].activeSource, Is.SameAs(texture));
        }

        [Test]
        public void FilenameIsSanitizedAndVersioned()
        {
            string filename = ScreenshotCatalogUtility.GetVersionedFilename("Hero Cover_Art", 0, 12);
            Assert.That(filename, Is.EqualTo("Hero-Cover-Art-01-v012.png"));
        }

        [Test]
        public void SourceReadyStatusUsesReadableLabel()
        {
            Assert.That(
                ScreenshotCatalogUtility.GetStatusLabel(ScreenshotCatalogStatus.SourceReady),
                Is.EqualTo("Source Ready"));
        }

        [Test]
        public void CaptureThenFinalReviewPrefersFinalAndFallsBackToSource()
        {
            ScreenshotCatalogRequirement requirement = new ScreenshotCatalogRequirement
            {
                workflow = ScreenshotAssetWorkflow.CaptureThenFinal
            };
            Texture2D source = new Texture2D(2, 2);
            Texture2D final = new Texture2D(2, 2);
            ScreenshotCatalogSlot slot = new ScreenshotCatalogSlot { activeSource = source };

            Assert.That(ScreenshotCatalogUtility.GetReviewTexture(requirement, slot), Is.SameAs(source));
            slot.activeFinal = final;
            Assert.That(ScreenshotCatalogUtility.GetReviewTexture(requirement, slot), Is.SameAs(final));

            Object.DestroyImmediate(source);
            Object.DestroyImmediate(final);
        }

        [Test]
        public void RemovingActiveVersionPromotesNewestRemainingVersion()
        {
            Texture2D first = new Texture2D(2, 2);
            Texture2D accidental = new Texture2D(2, 2);
            ScreenshotCatalogSlot slot = new ScreenshotCatalogSlot();
            ScreenshotCatalogUtility.AddSourceVersion(slot, first);
            ScreenshotCatalogUtility.AddSourceVersion(slot, accidental);

            ScreenshotCatalogUtility.RemoveSourceVersion(slot, 1);

            Assert.That(slot.sourceVersions, Is.EqualTo(new[] { first }));
            Assert.That(slot.activeSource, Is.SameAs(first));
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(accidental);
        }

        [Test]
        public void RemovingOnlyFinalVersionClearsActiveFinalWithoutDeletingAsset()
        {
            Texture2D final = new Texture2D(2, 2);
            ScreenshotCatalogSlot slot = new ScreenshotCatalogSlot();
            ScreenshotCatalogUtility.AddFinalVersion(slot, final);

            ScreenshotCatalogUtility.RemoveFinalVersion(slot, 0);

            Assert.That(slot.finalVersions, Is.Empty);
            Assert.That(slot.activeFinal, Is.Null);
            Assert.That(final, Is.Not.Null);
            Object.DestroyImmediate(final);
        }

        [Test]
        public void ManuallyAssignedVersionStartsUntracked()
        {
            string imagePath = TestFolder + "/manual-untracked.png";
            CreatePngAsset(imagePath, 16, 16);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotCatalogSlot slot = CreatePersistentSlot(out ScreenshotCatalog catalog, out ScreenshotTemplate template);

            ScreenshotCatalogUtility.AddSourceVersion(slot, texture);

            Assert.That(slot.activeSource, Is.SameAs(texture));
            Assert.That(ScreenshotCatalogUtility.IsVersionTracked(slot, false, texture), Is.False);
            Assert.That(ScreenshotGoogleDriveService.CountPending(catalog), Is.EqualTo(0));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void CapturedVersionCanBeAddedAsTracked()
        {
            string imagePath = TestFolder + "/captured-tracked.png";
            CreatePngAsset(imagePath, 16, 16);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotCatalogSlot slot = CreatePersistentSlot(out ScreenshotCatalog catalog, out ScreenshotTemplate template);

            ScreenshotCatalogUtility.AddSourceVersion(slot, texture, true);

            Assert.That(ScreenshotCatalogUtility.IsVersionTracked(slot, false, texture), Is.True);
            Assert.That(ScreenshotGoogleDriveService.CountPending(catalog), Is.EqualTo(1));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void UntrackingRetainsVersionActiveAssetAndDriveBinding()
        {
            string imagePath = TestFolder + "/retained-untracked.png";
            CreatePngAsset(imagePath, 16, 16);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotCatalogSlot slot = CreatePersistentSlot(out ScreenshotCatalog catalog, out ScreenshotTemplate template);
            ScreenshotCatalogUtility.AddSourceVersion(slot, texture, true);
            catalog.googleDriveBindings.Add(new ScreenshotGoogleDriveBinding
            {
                profileGuid = string.Empty,
                slotDefinitionId = slot.definitionId,
                localAssetGuid = AssetDatabase.AssetPathToGUID(imagePath),
                driveFileId = "existing-remote"
            });

            ScreenshotCatalogUtility.SetVersionTracked(slot, false, texture, false);

            Assert.That(slot.sourceVersions, Does.Contain(texture));
            Assert.That(slot.activeSource, Is.SameAs(texture));
            Assert.That(catalog.googleDriveBindings, Has.Count.EqualTo(1));
            Assert.That(ScreenshotGoogleDriveService.CountPending(catalog), Is.EqualTo(0));

            ScreenshotCatalogUtility.SetVersionTracked(slot, false, texture, true);
            Assert.That(ScreenshotGoogleDriveService.CountPending(catalog), Is.EqualTo(0));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void RemovingVersionRetainsDriveBinding()
        {
            string imagePath = TestFolder + "/removed-version.png";
            CreatePngAsset(imagePath, 16, 16);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotCatalogSlot slot = CreatePersistentSlot(out ScreenshotCatalog catalog, out ScreenshotTemplate template);
            ScreenshotCatalogUtility.AddSourceVersion(slot, texture, true);
            catalog.googleDriveBindings.Add(new ScreenshotGoogleDriveBinding
            {
                profileGuid = string.Empty,
                slotDefinitionId = slot.definitionId,
                localAssetGuid = AssetDatabase.AssetPathToGUID(imagePath),
                driveFileId = "existing-remote"
            });

            ScreenshotCatalogUtility.RemoveSourceVersion(slot, 0);

            Assert.That(slot.sourceVersions, Is.Empty);
            Assert.That(slot.activeSource, Is.Null);
            Assert.That(catalog.googleDriveBindings, Has.Count.EqualTo(1));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void ValidCapturedPngCompletesCaptureSlot()
        {
            string assetPath = TestFolder + "/valid.png";
            CreatePngAsset(assetPath, 64, 32);
            Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            ScreenshotCatalogRequirement requirement = new ScreenshotCatalogRequirement
            {
                workflow = ScreenshotAssetWorkflow.Capture,
                width = 64,
                height = 32,
                dimensionRule = ScreenshotDimensionRule.Exact
            };
            ScreenshotCatalogSlot slot = new ScreenshotCatalogSlot { activeSource = asset };

            Assert.That(ScreenshotCatalogUtility.GetStatus(requirement, slot), Is.EqualTo(ScreenshotCatalogStatus.Complete));
        }

        [Test]
        public void CaptureThenFinalReportsSourceReadyUntilFinalIsAssigned()
        {
            string assetPath = TestFolder + "/source.png";
            CreatePngAsset(assetPath, 64, 32);
            Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            ScreenshotCatalogRequirement requirement = new ScreenshotCatalogRequirement
            {
                workflow = ScreenshotAssetWorkflow.CaptureThenFinal,
                width = 64,
                height = 32,
                dimensionRule = ScreenshotDimensionRule.Exact
            };
            ScreenshotCatalogSlot slot = new ScreenshotCatalogSlot { activeSource = asset };

            Assert.That(ScreenshotCatalogUtility.GetStatus(requirement, slot), Is.EqualTo(ScreenshotCatalogStatus.SourceReady));
            slot.activeFinal = asset;
            Assert.That(ScreenshotCatalogUtility.GetStatus(requirement, slot), Is.EqualTo(ScreenshotCatalogStatus.Complete));
        }

        [Test]
        public void ManagedBridgeInterceptsTakeScreenshot()
        {
            GameObject gameObject = new GameObject("Screenshotter Test Camera", typeof(Camera));
            Screenshotter component = gameObject.AddComponent<Screenshotter>();
            int invocations = 0;
            ScreenshotterCaptureBridge.ManagedCaptureRequested = source =>
            {
                invocations++;
                return source == component;
            };

            component.TakeScreenshot();

            Assert.That(invocations, Is.EqualTo(1));
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void CameraSetupAddsAndConfiguresScreenshotterComponents()
        {
            GameObject gameObject = new GameObject("Catalog Capture Camera", typeof(Camera));
            Camera camera = gameObject.GetComponent<Camera>();

            Screenshotter first = ScreenshotterCameraSetup.Ensure(camera, out string warning);
            Screenshotter second = ScreenshotterCameraSetup.Ensure(camera, out _);
            PlayerInput playerInput = gameObject.GetComponent<PlayerInput>();

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(playerInput, Is.Not.Null);
            Assert.That(playerInput.actions, Is.Not.Null);
            Assert.That(warning, Is.Null);
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void PlainCameraCaptureUsesConfiguredDimensionsWithoutScreenshotter()
        {
            string assetPath = TestFolder + "/plain-camera.png";
            string absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
            GameObject gameObject = new GameObject("Plain Gameplay Camera", typeof(Camera));
            Camera camera = gameObject.GetComponent<Camera>();

            ScreenshotterCameraSetup.Capture(camera, absolutePath, 96, 54);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            Assert.That(gameObject.GetComponent<Screenshotter>(), Is.Null);
            Assert.That(ScreenshotCatalogUtility.TryGetPngDimensions(assetPath, out int width, out int height), Is.True);
            Assert.That(width, Is.EqualTo(96));
            Assert.That(height, Is.EqualTo(54));
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void CameraRenderCaptureUsesConfiguredDimensions()
        {
            string assetPath = TestFolder + "/rendered.png";
            string absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
            GameObject gameObject = new GameObject("Screenshotter Render Test", typeof(Camera));
            Screenshotter component = gameObject.AddComponent<Screenshotter>();
            component.gameViewScreenshot = false;
            component.screenShotResolution = new Vector2Int(80, 40);

            component.TakeNewScreenshot(absolutePath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D captured = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            bool readDimensions = ScreenshotCatalogUtility.TryGetPngDimensions(assetPath, out int width, out int height);

            Assert.That(captured, Is.Not.Null);
            Assert.That(readDimensions, Is.True);
            Assert.That(width, Is.EqualTo(80));
            Assert.That(height, Is.EqualTo(40));
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void GameViewResolutionRejectsInvalidSlotDimensions()
        {
            bool updated = GameViewResolutionUtility.TrySetResolution(0, 1080, out string error);

            Assert.That(updated, Is.False);
            Assert.That(error, Does.Contain("positive width and height"));
        }

        [Test]
        public void GameViewSizeCollectionCanBeResolved()
        {
            bool resolved = GameViewResolutionUtility.TryGetSizeCollection(out object sizes, out string error);

            Assert.That(resolved, Is.True, error);
            Assert.That(sizes, Is.Not.Null);
        }

        [Test]
        public void OutputFolderSelectionUsesProjectRelativeAssetPath()
        {
            string selectedFolder = Path.Combine(Application.dataPath, "Screenshots", "Campaign");

            bool converted = ScreenshotCatalogWindow.TryConvertToAssetFolder(selectedFolder, out string assetFolder);

            Assert.That(converted, Is.True);
            Assert.That(assetFolder, Is.EqualTo("Assets/Screenshots/Campaign"));
        }

        [Test]
        public void OutputFolderSelectionRejectsFolderOutsideAssets()
        {
            string selectedFolder = Directory.GetParent(Application.dataPath).FullName;

            bool converted = ScreenshotCatalogWindow.TryConvertToAssetFolder(selectedFolder, out string assetFolder);

            Assert.That(converted, Is.False);
            Assert.That(assetFolder, Is.Null);
        }

        [Test]
        public void GoogleDriveFolderUrlIsConvertedToFolderId()
        {
            string id = ScreenshotGoogleDriveProfileEditor.ExtractFolderId(
                "https://drive.google.com/drive/folders/1AbCdEf_123?usp=sharing");

            Assert.That(id, Is.EqualTo("1AbCdEf_123"));
        }

        [Test]
        public void GoogleDrivePendingUploadsAreScopedToSelectedProfile()
        {
            string imagePath = TestFolder + "/drive-source.png";
            CreatePngAsset(imagePath, 16, 16);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotGoogleDriveProfile firstProfile = ScriptableObject.CreateInstance<ScreenshotGoogleDriveProfile>();
            ScreenshotGoogleDriveProfile secondProfile = ScriptableObject.CreateInstance<ScreenshotGoogleDriveProfile>();
            string firstProfilePath = TestFolder + "/drive-profile-a.asset";
            string secondProfilePath = TestFolder + "/drive-profile-b.asset";
            AssetDatabase.CreateAsset(firstProfile, firstProfilePath);
            AssetDatabase.CreateAsset(secondProfile, secondProfilePath);

            ScreenshotTemplate template = ScriptableObject.CreateInstance<ScreenshotTemplate>();
            ScreenshotCategoryDefinition categoryDefinition = new ScreenshotCategoryDefinition { id = "category", name = "Category" };
            ScreenshotRequirementDefinition requirementDefinition = new ScreenshotRequirementDefinition { id = "requirement", name = "Requirement" };
            requirementDefinition.slots.Add(new ScreenshotSlotDefinition { id = "slot", name = "Slot" });
            categoryDefinition.requirements.Add(requirementDefinition);
            template.categories.Add(categoryDefinition);
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            ScreenshotCatalogSlot slot = catalog.categories.Single().requirements.Single().slots.Single();
            ScreenshotCatalogUtility.AddSourceVersion(slot, texture, true);
            catalog.googleDriveProfile = firstProfile;
            catalog.googleDriveBindings.Add(new ScreenshotGoogleDriveBinding
            {
                profileGuid = AssetDatabase.AssetPathToGUID(firstProfilePath),
                slotDefinitionId = slot.definitionId,
                localAssetGuid = AssetDatabase.AssetPathToGUID(imagePath),
                driveFileId = "remote-file"
            });

            Assert.That(ScreenshotGoogleDriveService.CountPending(catalog), Is.EqualTo(0));
            Assert.That(ScreenshotGoogleDriveService.IsVersionSynced(catalog, slot, false, texture), Is.True);

            catalog.googleDriveProfile = secondProfile;
            Assert.That(ScreenshotGoogleDriveService.CountPending(catalog), Is.EqualTo(1));
            Assert.That(ScreenshotGoogleDriveService.IsVersionSynced(catalog, slot, false, texture), Is.False);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void GoogleDriveFilenameMatchesRequirementAndSlot()
        {
            ScreenshotCatalogCategory category = new ScreenshotCatalogCategory { name = "Meta Distribution" };
            ScreenshotCatalogRequirement requirement = new ScreenshotCatalogRequirement { name = "Hero Cover" };
            ScreenshotCatalogSlot first = new ScreenshotCatalogSlot { name = "First" };
            ScreenshotCatalogSlot second = new ScreenshotCatalogSlot { name = "Second" };
            requirement.slots.Add(first);
            requirement.slots.Add(second);
            category.requirements.Add(requirement);

            bool matched = ScreenshotGoogleDriveService.TryMatchRemoteFilename(
                category, "Hero-Cover-02-v014.png", out ScreenshotCatalogRequirement matchedRequirement,
                out ScreenshotCatalogSlot matchedSlot);

            Assert.That(matched, Is.True);
            Assert.That(matchedRequirement, Is.SameAs(requirement));
            Assert.That(matchedSlot, Is.SameAs(second));
        }

        [Test]
        public void GoogleDriveFilenameRejectsUnknownAndMalformedVersions()
        {
            ScreenshotCatalogCategory category = new ScreenshotCatalogCategory { name = "Category" };
            ScreenshotCatalogRequirement requirement = new ScreenshotCatalogRequirement { name = "Hero Cover" };
            requirement.slots.Add(new ScreenshotCatalogSlot { name = "Image" });
            category.requirements.Add(requirement);

            Assert.That(ScreenshotGoogleDriveService.TryMatchRemoteFilename(
                category, "Unknown-01-v001.png", out _, out _), Is.False);
            Assert.That(ScreenshotGoogleDriveService.TryMatchRemoteFilename(
                category, "Hero-Cover-01-vLatest.png", out _, out _), Is.False);
            Assert.That(ScreenshotGoogleDriveService.TryMatchRemoteFilename(
                category, "Hero-Cover-01-v001.jpg", out _, out _), Is.False);
        }

        [Test]
        public void GoogleDriveDownloadsUseSeparateSourceAndFinalFoldersWithoutOverwriting()
        {
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            catalog.name = "Catalog";
            catalog.outputRoot = TestFolder;
            ScreenshotCatalogCategory category = new ScreenshotCatalogCategory { name = "Meta Distribution" };

            string sourcePath = ScreenshotGoogleDriveService.GetDownloadAssetPath(
                catalog, category, false, "Hero-Cover-01-v001.png");
            string finalPath = ScreenshotGoogleDriveService.GetDownloadAssetPath(
                catalog, category, true, "Hero-Cover-01-v001.png");

            Assert.That(sourcePath, Is.EqualTo(TestFolder + "/Catalog/Meta-Distribution/Source/Hero-Cover-01-v001.png"));
            Assert.That(finalPath, Is.EqualTo(TestFolder + "/Catalog/Meta-Distribution/Final/Hero-Cover-01-v001.png"));
            Assert.That(sourcePath, Is.Not.EqualTo(finalPath));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                sourcePath.Replace('/', Path.DirectorySeparatorChar))));
            File.WriteAllBytes(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                sourcePath.Replace('/', Path.DirectorySeparatorChar)), new byte[] { 1 });
            string duplicatePath = ScreenshotGoogleDriveService.GetDownloadAssetPath(
                catalog, category, false, "Hero-Cover-01-v001.png");
            Assert.That(duplicatePath, Is.EqualTo(
                TestFolder + "/Catalog/Meta-Distribution/Source/Hero-Cover-01-v001-drive-001.png"));
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void RequiredProgressExcludesOptionalAndObsoleteSlots()
        {
            string imagePath = TestFolder + "/progress-valid.png";
            CreatePngAsset(imagePath, 16, 16);
            Texture2D valid = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotTemplate template = ScriptableObject.CreateInstance<ScreenshotTemplate>();
            ScreenshotCategoryDefinition category = new ScreenshotCategoryDefinition { id = "category", name = "Category" };
            ScreenshotRequirementDefinition completeDefinition = CreateRequirementDefinition("complete", "Complete", true);
            ScreenshotRequirementDefinition missingDefinition = CreateRequirementDefinition("missing", "Missing", true);
            ScreenshotRequirementDefinition optionalDefinition = CreateRequirementDefinition("optional", "Optional", false);
            ScreenshotRequirementDefinition obsoleteDefinition = CreateRequirementDefinition("obsolete", "Obsolete", true);
            category.requirements.Add(completeDefinition);
            category.requirements.Add(missingDefinition);
            category.requirements.Add(optionalDefinition);
            category.requirements.Add(obsoleteDefinition);
            template.categories.Add(category);
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);

            ScreenshotCatalogUtility.AddSourceVersion(
                catalog.categories.Single().requirements.Single(item => item.definitionId == "complete").slots.Single(), valid);
            ScreenshotCatalogUtility.AddSourceVersion(
                catalog.categories.Single().requirements.Single(item => item.definitionId == "obsolete").slots.Single(), valid);
            category.requirements.Remove(obsoleteDefinition);
            ScreenshotCatalogUtility.Synchronize(catalog);

            ScreenshotCatalogGUI.GetRequiredProgress(catalog, out int complete, out int total);

            Assert.That(total, Is.EqualTo(2));
            Assert.That(complete, Is.EqualTo(1));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void CatalogStatusIconsMatchMasterDetailLegend()
        {
            Assert.That(ScreenshotCatalogGUI.GetStatusIcon(ScreenshotCatalogStatus.Complete), Is.EqualTo("✓"));
            Assert.That(ScreenshotCatalogGUI.GetStatusIcon(ScreenshotCatalogStatus.SourceReady), Is.EqualTo("●"));
            Assert.That(ScreenshotCatalogGUI.GetStatusIcon(ScreenshotCatalogStatus.Missing), Is.EqualTo("○"));
            Assert.That(ScreenshotCatalogGUI.GetStatusIcon(ScreenshotCatalogStatus.Invalid), Is.EqualTo("!"));
            Assert.That(ScreenshotCatalogGUI.GetStatusIcon(ScreenshotCatalogStatus.Obsolete), Is.EqualTo("—"));
        }

        [Test]
        public void NavigatorStatusLabelsDoNotRelyOnColorOrSymbolsAlone()
        {
            Assert.That(ScreenshotCatalogWindow.GetNavigatorStatusLabel(ScreenshotCatalogStatus.Complete), Does.Contain("Complete"));
            Assert.That(ScreenshotCatalogWindow.GetNavigatorStatusLabel(ScreenshotCatalogStatus.SourceReady), Does.Contain("Ready"));
            Assert.That(ScreenshotCatalogWindow.GetNavigatorStatusLabel(ScreenshotCatalogStatus.Missing), Does.Contain("Missing"));
            Assert.That(ScreenshotCatalogWindow.GetNavigatorStatusLabel(ScreenshotCatalogStatus.Invalid), Does.Contain("Invalid"));
            Assert.That(ScreenshotCatalogWindow.GetNavigatorStatusLabel(ScreenshotCatalogStatus.Obsolete), Does.Contain("Obsolete"));
        }

        [Test]
        public void FooterIdentifiesTheAssetItActuallyValidates()
        {
            ScreenshotCatalogRequirement requirement = new ScreenshotCatalogRequirement
            {
                workflow = ScreenshotAssetWorkflow.CaptureThenFinal
            };
            ScreenshotCatalogSlot slot = new ScreenshotCatalogSlot();
            Texture2D source = new Texture2D(1, 1);
            Texture2D final = new Texture2D(1, 1);
            slot.activeSource = source;
            slot.activeFinal = final;

            Assert.That(ScreenshotCatalogWindow.GetReviewAssetRole(requirement, slot, source), Is.EqualTo("Active source"));
            Assert.That(ScreenshotCatalogWindow.GetReviewAssetRole(requirement, slot, final), Is.EqualTo("Active final"));

            Object.DestroyImmediate(source);
            Object.DestroyImmediate(final);
        }

        [Test]
        public void WindowInitialSelectionPrefersInvalidAndPreservesExplicitSelection()
        {
            string imagePath = TestFolder + "/selection-invalid.png";
            CreatePngAsset(imagePath, 8, 8);
            Texture2D invalid = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            ScreenshotTemplate template = ScriptableObject.CreateInstance<ScreenshotTemplate>();
            ScreenshotCategoryDefinition categoryDefinition = new ScreenshotCategoryDefinition { id = "category", name = "Category" };
            categoryDefinition.requirements.Add(CreateRequirementDefinition("missing", "Missing", true));
            categoryDefinition.requirements.Add(CreateRequirementDefinition("invalid", "Invalid", true));
            template.categories.Add(categoryDefinition);
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            ScreenshotCatalogRequirement invalidRequirement = catalog.categories.Single().requirements.Single(item => item.definitionId == "invalid");
            ScreenshotCatalogUtility.AddSourceVersion(invalidRequirement.slots.Single(), invalid);
            ScreenshotCatalogWindow window = ScriptableObject.CreateInstance<ScreenshotCatalogWindow>();

            window.SetCatalog(catalog);

            Assert.That(window.SelectedRequirementId, Is.EqualTo("invalid"));
            ScreenshotCatalogCategory category = catalog.categories.Single();
            ScreenshotCatalogRequirement missing = category.requirements.Single(item => item.definitionId == "missing");
            window.SelectSlot(category, missing, missing.slots.Single());
            window.EnsureSelectedSlot();
            Assert.That(window.SelectedRequirementId, Is.EqualTo("missing"));

            Object.DestroyImmediate(window);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void NextCapturePathUsesConfiguredNamingAndSkipsExistingFile()
        {
            CreatePersistentSlot(out ScreenshotCatalog catalog, out ScreenshotTemplate template);
            ScreenshotCatalogCategory category = catalog.categories.Single();
            ScreenshotCatalogRequirement requirement = category.requirements.Single();
            ScreenshotCatalogSlot slot = requirement.slots.Single();
            requirement.name = "Hero Cover";
            catalog.outputRoot = TestFolder;
            string firstPath = ScreenshotCatalogUtility.GetNextCaptureAssetPath(catalog, category, requirement, slot);
            string absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, firstPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, new byte[] { 1 });

            string nextPath = ScreenshotCatalogUtility.GetNextCaptureAssetPath(catalog, category, requirement, slot);

            Assert.That(Path.GetFileName(firstPath), Is.EqualTo("Hero-Cover-01-v001.png"));
            Assert.That(Path.GetFileName(nextPath), Is.EqualTo("Hero-Cover-01-v002.png"));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void WindowBuildsNamedMasterDetailRegionsAndSerializesPageState()
        {
            ScreenshotCatalogWindow source = ScriptableObject.CreateInstance<ScreenshotCatalogWindow>();
            Assert.That(source.rootVisualElement.Q("screenshot-catalog-top-bar"), Is.Not.Null);
            Assert.That(source.rootVisualElement.Q("screenshot-catalog-navigator-scroll"), Is.Not.Null);
            Assert.That(source.rootVisualElement.Q("screenshot-catalog-workspace-scroll"), Is.Not.Null);
            Assert.That(source.rootVisualElement.Q("screenshot-catalog-status-bar"), Is.Not.Null);
            ScreenshotTemplate template = ScriptableObject.CreateInstance<ScreenshotTemplate>();
            ScreenshotCategoryDefinition category = new ScreenshotCategoryDefinition { id = "category", name = "Category" };
            category.requirements.Add(CreateRequirementDefinition("requirement", "Requirement", true));
            template.categories.Add(category);
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            source.SetCatalog(catalog);
            VisualElement navigator = source.rootVisualElement.Q("screenshot-catalog-navigator-contents");
            Assert.That(navigator.childCount, Is.GreaterThan(0));
            Label navigatorStatus = navigator.Q<Label>(className: "screenshot-catalog-slot-status");
            Assert.That(navigatorStatus, Is.Not.Null);
            Assert.That(navigatorStatus.text, Does.Contain("Missing"));
            Assert.That(source.SelectedSlotId, Is.EqualTo("requirement-slot"));
            source.SettingsPageVisible = true;
            string json = EditorJsonUtility.ToJson(source);
            ScreenshotCatalogWindow restored = ScriptableObject.CreateInstance<ScreenshotCatalogWindow>();

            EditorJsonUtility.FromJsonOverwrite(json, restored);

            Assert.That(restored.SettingsPageVisible, Is.True);
            Object.DestroyImmediate(restored);
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template);
        }

        private static ScreenshotCatalogSlot CreatePersistentSlot(
            out ScreenshotCatalog catalog,
            out ScreenshotTemplate template)
        {
            template = ScriptableObject.CreateInstance<ScreenshotTemplate>();
            ScreenshotCategoryDefinition categoryDefinition = new ScreenshotCategoryDefinition { id = "category", name = "Category" };
            ScreenshotRequirementDefinition requirementDefinition = new ScreenshotRequirementDefinition { id = "requirement", name = "Requirement" };
            requirementDefinition.slots.Add(new ScreenshotSlotDefinition { id = "slot", name = "Slot" });
            categoryDefinition.requirements.Add(requirementDefinition);
            template.categories.Add(categoryDefinition);
            catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogUtility.InitializeCatalog(catalog, template);
            return catalog.categories.Single().requirements.Single().slots.Single();
        }

        private static ScreenshotRequirementDefinition CreateRequirementDefinition(
            string id,
            string name,
            bool required)
        {
            ScreenshotRequirementDefinition definition = new ScreenshotRequirementDefinition
            {
                id = id,
                name = name,
                width = 16,
                height = 16,
                workflow = ScreenshotAssetWorkflow.Capture,
                imageFormat = ScreenshotImageFormat.Png24
            };
            definition.slots.Add(new ScreenshotSlotDefinition
            {
                id = id + "-slot",
                name = name,
                required = required
            });
            return definition;
        }

        private static void CreatePngAsset(string assetPath, int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            string absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
