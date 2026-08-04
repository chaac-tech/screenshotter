using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

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
            slot.sourceVersions.Add(version);
            slot.activeSource = version;

            template.categories[0].requirements.RemoveAt(0);
            ScreenshotCatalogUtility.Synchronize(catalog);

            Assert.That(requirement.obsolete, Is.True);
            Assert.That(slot.sourceVersions.Single(), Is.SameAs(version));
            Assert.That(slot.activeSource, Is.SameAs(version));
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
            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogCategory category = new ScreenshotCatalogCategory { obsolete = true };
            ScreenshotCatalogRequirement emptyRequirement = new ScreenshotCatalogRequirement { obsolete = true };
            emptyRequirement.slots.Add(new ScreenshotCatalogSlot { obsolete = true });
            ScreenshotCatalogRequirement assignedRequirement = new ScreenshotCatalogRequirement { obsolete = true };
            Texture2D version = new Texture2D(2, 2);
            ScreenshotCatalogSlot assignedSlot = new ScreenshotCatalogSlot { obsolete = true };
            assignedSlot.sourceVersions.Add(version);
            assignedRequirement.slots.Add(assignedSlot);
            category.requirements.Add(emptyRequirement);
            category.requirements.Add(assignedRequirement);
            catalog.categories.Add(category);

            int removed = ScreenshotCatalogUtility.PruneEmptyObsolete(catalog);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(catalog.categories.Single(), Is.SameAs(category));
            Assert.That(category.requirements.Single(), Is.SameAs(assignedRequirement));
            Object.DestroyImmediate(version);
            Object.DestroyImmediate(catalog);
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

            ScreenshotCatalog catalog = ScriptableObject.CreateInstance<ScreenshotCatalog>();
            ScreenshotCatalogCategory category = new ScreenshotCatalogCategory { definitionId = "category", name = "Category" };
            ScreenshotCatalogRequirement requirement = new ScreenshotCatalogRequirement { definitionId = "requirement", name = "Requirement" };
            ScreenshotCatalogSlot slot = new ScreenshotCatalogSlot { definitionId = "slot", name = "Slot" };
            slot.sourceVersions.Add(texture);
            slot.activeSource = texture;
            requirement.slots.Add(slot);
            category.requirements.Add(requirement);
            catalog.categories.Add(category);
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
