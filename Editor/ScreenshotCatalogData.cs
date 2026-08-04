using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    internal enum ScreenshotAssetWorkflow
    {
        Capture,
        External,
        CaptureThenFinal
    }

    internal enum ScreenshotDimensionRule
    {
        Exact,
        Maximum,
        Minimum
    }

    internal enum ScreenshotImageFormat
    {
        Png24,
        Png32
    }

    internal enum ScreenshotCatalogStatus
    {
        Missing,
        SourceReady,
        Complete,
        Invalid,
        Obsolete
    }

    [Serializable]
    internal sealed class ScreenshotSlotDefinition
    {
        [SerializeField, HideInInspector] internal string id;
        [SerializeField] internal string name = "Image";
        [SerializeField] internal bool required = true;
    }

    [Serializable]
    internal sealed class ScreenshotRequirementDefinition
    {
        [SerializeField, HideInInspector] internal string id;
        [SerializeField] internal string name = "New Requirement";
        [SerializeField] internal ScreenshotAssetWorkflow workflow = ScreenshotAssetWorkflow.Capture;
        [SerializeField] internal int width = 1920;
        [SerializeField] internal int height = 1080;
        [SerializeField] internal ScreenshotDimensionRule dimensionRule = ScreenshotDimensionRule.Exact;
        [SerializeField] internal ScreenshotImageFormat imageFormat = ScreenshotImageFormat.Png24;
        [SerializeField] internal bool requireTransparency;
        [SerializeField, TextArea(3, 8)] internal string guidance;
        [SerializeField] internal string sourceUrl;
        [SerializeField] internal List<ScreenshotSlotDefinition> slots = new List<ScreenshotSlotDefinition>();
    }

    [Serializable]
    internal sealed class ScreenshotCategoryDefinition
    {
        [SerializeField, HideInInspector] internal string id;
        [SerializeField] internal string name = "New Category";
        [SerializeField] internal bool includedByDefault = true;
        [SerializeField] internal List<ScreenshotRequirementDefinition> requirements = new List<ScreenshotRequirementDefinition>();
    }

    [Serializable]
    internal sealed class ScreenshotCatalogSlotState
    {
        [SerializeField] internal string definitionId;
        [SerializeField] internal string fallbackName;
        [SerializeField] internal bool fallbackRequired;
        [SerializeField] internal List<Texture2D> sourceVersions = new List<Texture2D>();
        [SerializeField] internal List<string> trackedSourceAssetGuids = new List<string>();
        [SerializeField] internal int activeSourceIndex = -1;
        [SerializeField] internal List<Texture2D> finalVersions = new List<Texture2D>();
        [SerializeField] internal List<string> trackedFinalAssetGuids = new List<string>();
        [SerializeField] internal int activeFinalIndex = -1;
    }

    [Serializable]
    internal sealed class ScreenshotCatalogRequirementState
    {
        [SerializeField] internal string categoryDefinitionId;
        [SerializeField] internal string fallbackCategoryName;
        [SerializeField] internal string definitionId;
        [SerializeField] internal string fallbackName;
        [SerializeField] internal ScreenshotAssetWorkflow fallbackWorkflow;
        [SerializeField] internal int fallbackWidth;
        [SerializeField] internal int fallbackHeight;
        [SerializeField] internal ScreenshotDimensionRule fallbackDimensionRule;
        [SerializeField] internal ScreenshotImageFormat fallbackImageFormat;
        [SerializeField] internal bool fallbackRequireTransparency;
        [SerializeField, TextArea(3, 8)] internal string fallbackGuidance;
        [SerializeField] internal string fallbackSourceUrl;
        [SerializeField] internal List<ScreenshotCatalogSlotState> slots = new List<ScreenshotCatalogSlotState>();
    }

    internal sealed class ScreenshotCatalogSlot
    {
        internal string definitionId;
        internal string name;
        internal bool required;
        internal bool obsolete;
        internal ScreenshotCatalog catalog;
        internal ScreenshotCatalogRequirement requirement;
        internal ScreenshotCatalogSlotState state;

        private readonly ScreenshotCatalogSlotState transientState = new ScreenshotCatalogSlotState();
        private ScreenshotCatalogSlotState CurrentState { get { return state ?? transientState; } }

        internal List<Texture2D> sourceVersions { get { return CurrentState.sourceVersions; } }
        internal List<Texture2D> finalVersions { get { return CurrentState.finalVersions; } }
        internal Texture2D activeSource
        {
            get { return GetActive(CurrentState.sourceVersions, CurrentState.activeSourceIndex); }
            set
            {
                ScreenshotCatalogSlotState current = CurrentState;
                SetActive(current.sourceVersions, ref current.activeSourceIndex, value);
            }
        }
        internal Texture2D activeFinal
        {
            get { return GetActive(CurrentState.finalVersions, CurrentState.activeFinalIndex); }
            set
            {
                ScreenshotCatalogSlotState current = CurrentState;
                SetActive(current.finalVersions, ref current.activeFinalIndex, value);
            }
        }

        private static Texture2D GetActive(List<Texture2D> versions, int index)
        {
            return index >= 0 && index < versions.Count ? versions[index] : null;
        }

        private static void SetActive(List<Texture2D> versions, ref int index, Texture2D texture)
        {
            if (texture != null && !versions.Contains(texture))
            {
                versions.Add(texture);
            }
            index = texture == null ? -1 : versions.IndexOf(texture);
        }
    }

    internal sealed class ScreenshotCatalogRequirement
    {
        internal string definitionId;
        internal string name;
        internal ScreenshotAssetWorkflow workflow;
        internal int width;
        internal int height;
        internal ScreenshotDimensionRule dimensionRule;
        internal ScreenshotImageFormat imageFormat;
        internal bool requireTransparency;
        internal string guidance;
        internal string sourceUrl;
        internal bool obsolete;
        internal ScreenshotCatalog catalog;
        internal string categoryDefinitionId;
        internal string categoryName;
        internal ScreenshotCatalogRequirementState state;
        internal List<ScreenshotCatalogSlot> slots = new List<ScreenshotCatalogSlot>();
    }

    internal sealed class ScreenshotCatalogCategory
    {
        internal string definitionId;
        internal string name;
        internal bool obsolete;
        internal List<ScreenshotCatalogRequirement> requirements = new List<ScreenshotCatalogRequirement>();
    }

    [Serializable]
    internal sealed class ScreenshotGoogleDriveBinding
    {
        [SerializeField] internal string profileGuid;
        [SerializeField] internal string slotDefinitionId;
        [SerializeField] internal bool finalAsset;
        [SerializeField] internal string localAssetGuid;
        [SerializeField] internal string driveFileId;
    }

    internal static class ScreenshotCatalogUtility
    {
        internal static void EnsureTemplateIds(ScreenshotTemplate template)
        {
            if (template == null)
            {
                return;
            }

            HashSet<string> ids = new HashSet<string>();
            foreach (ScreenshotCategoryDefinition category in template.categories)
            {
                EnsureId(ref category.id, ids);
                foreach (ScreenshotRequirementDefinition requirement in category.requirements)
                {
                    EnsureId(ref requirement.id, ids);
                    if (requirement.slots.Count == 0)
                    {
                        requirement.slots.Add(new ScreenshotSlotDefinition());
                    }

                    foreach (ScreenshotSlotDefinition slot in requirement.slots)
                    {
                        EnsureId(ref slot.id, ids);
                    }
                }
            }
        }

        internal static void InitializeCatalog(ScreenshotCatalog catalog, ScreenshotTemplate template)
        {
            catalog.template = template;
            EnsureTemplateIds(template);
            catalog.includedCategoryIds = template.categories
                .Where(category => category.includedByDefault)
                .Select(category => category.id)
                .ToList();
            Synchronize(catalog);
        }

        internal static void Synchronize(ScreenshotCatalog catalog)
        {
            if (catalog == null || catalog.template == null)
            {
                return;
            }

            EnsureTemplateIds(catalog.template);
            UpdateFallbackMetadata(catalog);
            PruneEmptyObsolete(catalog);
            EditorUtility.SetDirty(catalog);
        }

        internal static int PruneEmptyObsolete(ScreenshotCatalog catalog)
        {
            if (catalog == null)
            {
                return 0;
            }

            int removed = 0;
            foreach (ScreenshotCatalogRequirementState requirement in catalog.requirementStates)
            {
                removed += requirement.slots.RemoveAll(slot => !HasAssignedImages(slot));
            }
            removed += catalog.requirementStates.RemoveAll(requirement => requirement.slots.Count == 0);
            if (removed > 0)
            {
                EditorUtility.SetDirty(catalog);
            }
            return removed;
        }

        internal static List<ScreenshotCatalogCategory> ResolveCategories(ScreenshotCatalog catalog)
        {
            List<ScreenshotCatalogCategory> result = new List<ScreenshotCatalogCategory>();
            if (catalog == null)
            {
                return result;
            }

            HashSet<string> resolvedRequirements = new HashSet<string>();
            HashSet<string> resolvedSlots = new HashSet<string>();
            HashSet<string> included = new HashSet<string>(catalog.includedCategoryIds ?? new List<string>());
            if (catalog.template != null)
            {
                foreach (ScreenshotCategoryDefinition categoryDefinition in catalog.template.categories)
                {
                    if (!included.Contains(categoryDefinition.id))
                    {
                        continue;
                    }

                    ScreenshotCatalogCategory category = new ScreenshotCatalogCategory
                    {
                        definitionId = categoryDefinition.id,
                        name = categoryDefinition.name,
                        obsolete = false
                    };
                    result.Add(category);
                    foreach (ScreenshotRequirementDefinition definition in categoryDefinition.requirements)
                    {
                        ScreenshotCatalogRequirementState state = catalog.requirementStates.FirstOrDefault(item => item.definitionId == definition.id);
                        ScreenshotCatalogRequirement requirement = CreateRequirement(catalog, category, definition, state, false);
                        category.requirements.Add(requirement);
                        resolvedRequirements.Add(definition.id);
                        foreach (ScreenshotSlotDefinition slotDefinition in definition.slots)
                        {
                            ScreenshotCatalogSlotState slotState = state == null ? null : state.slots.FirstOrDefault(item => item.definitionId == slotDefinition.id);
                            requirement.slots.Add(CreateSlot(catalog, requirement, slotDefinition.id, slotDefinition.name, slotDefinition.required, false, slotState));
                            resolvedSlots.Add(slotDefinition.id);
                        }

                        if (state != null)
                        {
                            foreach (ScreenshotCatalogSlotState obsoleteSlot in state.slots.Where(item => !resolvedSlots.Contains(item.definitionId)))
                            {
                                requirement.slots.Add(CreateSlot(catalog, requirement, obsoleteSlot.definitionId, obsoleteSlot.fallbackName,
                                    obsoleteSlot.fallbackRequired, true, obsoleteSlot));
                            }
                        }
                    }
                }
            }

            foreach (ScreenshotCatalogRequirementState state in catalog.requirementStates.Where(item => !resolvedRequirements.Contains(item.definitionId)))
            {
                ScreenshotCatalogCategory category = result.FirstOrDefault(item => item.definitionId == state.categoryDefinitionId);
                if (category == null)
                {
                    category = new ScreenshotCatalogCategory
                    {
                        definitionId = state.categoryDefinitionId,
                        name = state.fallbackCategoryName,
                        obsolete = true
                    };
                    result.Add(category);
                }

                ScreenshotCatalogRequirement requirement = CreateRequirement(catalog, category, null, state, true);
                category.requirements.Add(requirement);
                foreach (ScreenshotCatalogSlotState slotState in state.slots)
                {
                    requirement.slots.Add(CreateSlot(catalog, requirement, slotState.definitionId, slotState.fallbackName,
                        slotState.fallbackRequired, true, slotState));
                }
            }

            return result;
        }

        internal static ScreenshotCatalogStatus GetStatus(ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            if (requirement.obsolete || slot.obsolete)
            {
                return ScreenshotCatalogStatus.Obsolete;
            }

            bool sourceValid = ValidateTexture(slot.activeSource, requirement, out _);
            bool finalValid = ValidateTexture(slot.activeFinal, requirement, out _);
            bool sourceAssigned = slot.activeSource != null;
            bool finalAssigned = slot.activeFinal != null;

            if ((sourceAssigned && !sourceValid) || (finalAssigned && !finalValid))
            {
                return ScreenshotCatalogStatus.Invalid;
            }

            switch (requirement.workflow)
            {
                case ScreenshotAssetWorkflow.Capture:
                    return sourceValid ? ScreenshotCatalogStatus.Complete : ScreenshotCatalogStatus.Missing;
                case ScreenshotAssetWorkflow.External:
                    return finalValid ? ScreenshotCatalogStatus.Complete : ScreenshotCatalogStatus.Missing;
                case ScreenshotAssetWorkflow.CaptureThenFinal:
                    if (finalValid)
                    {
                        return ScreenshotCatalogStatus.Complete;
                    }
                    return sourceValid ? ScreenshotCatalogStatus.SourceReady : ScreenshotCatalogStatus.Missing;
                default:
                    return ScreenshotCatalogStatus.Missing;
            }
        }

        internal static string GetStatusLabel(ScreenshotCatalogStatus status)
        {
            return status == ScreenshotCatalogStatus.SourceReady ? "Source Ready" : status.ToString();
        }

        internal static Texture2D GetReviewTexture(ScreenshotCatalogRequirement requirement, ScreenshotCatalogSlot slot)
        {
            switch (requirement.workflow)
            {
                case ScreenshotAssetWorkflow.Capture:
                    return slot.activeSource;
                case ScreenshotAssetWorkflow.External:
                    return slot.activeFinal;
                case ScreenshotAssetWorkflow.CaptureThenFinal:
                    return slot.activeFinal != null ? slot.activeFinal : slot.activeSource;
                default:
                    return null;
            }
        }

        internal static bool ValidateTexture(Texture2D texture, ScreenshotCatalogRequirement requirement, out string message)
        {
            if (texture == null)
            {
                message = "No image assigned.";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            {
                message = "The assigned image must be a PNG project asset.";
                return false;
            }

            int sourceWidth;
            int sourceHeight;
            ScreenshotImageFormat sourceFormat;
            if (!TryGetPngInfo(path, out sourceWidth, out sourceHeight, out sourceFormat))
            {
                message = "The assigned PNG header could not be read or uses an unsupported pixel format.";
                return false;
            }

            if (sourceFormat != requirement.imageFormat)
            {
                message = string.Format("Expected {0}; found {1}.", requirement.imageFormat, sourceFormat);
                return false;
            }

            bool dimensionsValid;
            switch (requirement.dimensionRule)
            {
                case ScreenshotDimensionRule.Maximum:
                    dimensionsValid = sourceWidth <= requirement.width && sourceHeight <= requirement.height;
                    break;
                case ScreenshotDimensionRule.Minimum:
                    dimensionsValid = sourceWidth >= requirement.width && sourceHeight >= requirement.height;
                    break;
                default:
                    dimensionsValid = sourceWidth == requirement.width && sourceHeight == requirement.height;
                    break;
            }

            if (!dimensionsValid)
            {
                message = string.Format("Expected {0} dimensions of {1}x{2}; found {3}x{4}.",
                    requirement.dimensionRule.ToString().ToLowerInvariant(), requirement.width, requirement.height, sourceWidth, sourceHeight);
                return false;
            }

            if (requirement.requireTransparency)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || !importer.DoesSourceTextureHaveAlpha())
                {
                    message = "The assigned PNG must contain transparency.";
                    return false;
                }
            }

            message = "Valid.";
            return true;
        }

        internal static bool TryGetPngDimensions(string assetPath, out int width, out int height)
        {
            ScreenshotImageFormat format;
            return TryGetPngInfo(assetPath, out width, out height, out format);
        }

        internal static bool TryGetPngInfo(string assetPath, out int width, out int height, out ScreenshotImageFormat format)
        {
            width = 0;
            height = 0;
            format = ScreenshotImageFormat.Png24;
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            byte[] header = new byte[26];
            using (FileStream stream = File.OpenRead(absolutePath))
            {
                if (stream.Read(header, 0, header.Length) != header.Length)
                {
                    return false;
                }
            }

            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int index = 0; index < signature.Length; index++)
            {
                if (header[index] != signature[index])
                {
                    return false;
                }
            }

            width = ReadBigEndianInt(header, 16);
            height = ReadBigEndianInt(header, 20);
            byte bitDepth = header[24];
            byte colorType = header[25];
            if (bitDepth != 8 || (colorType != 2 && colorType != 6))
            {
                return false;
            }

            format = colorType == 6 ? ScreenshotImageFormat.Png32 : ScreenshotImageFormat.Png24;
            return width > 0 && height > 0;
        }

        internal static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Image";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(value.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray());
            cleaned = string.Join("-", cleaned.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrEmpty(cleaned) ? "Image" : cleaned;
        }

        internal static string GetVersionedFilename(string requirementName, int slotIndex, int version)
        {
            return string.Format("{0}-{1:00}-v{2:000}.png", SanitizeName(requirementName), slotIndex + 1, version);
        }

        internal static string GetCatalogFolder(ScreenshotCatalog catalog, ScreenshotCatalogCategory category)
        {
            string root = string.IsNullOrWhiteSpace(catalog.outputRoot) ? "Assets/Screenshots" : catalog.outputRoot.TrimEnd('/', '\\');
            return string.Format("{0}/{1}/{2}", root, SanitizeName(catalog.name), SanitizeName(category.name)).Replace('\\', '/');
        }

        internal static int GetNextVersion(ScreenshotCatalogSlot slot)
        {
            return slot.sourceVersions.Count + 1;
        }

        internal static string GetNextCaptureAssetPath(
            ScreenshotCatalog catalog,
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot)
        {
            string assetFolder = GetCatalogFolder(catalog, category);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            int version = GetNextVersion(slot);
            int slotIndex = requirement.slots.IndexOf(slot);
            string assetPath;
            string absolutePath;
            do
            {
                string filename = GetVersionedFilename(requirement.name, slotIndex, version++);
                assetPath = assetFolder + "/" + filename;
                absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            }
            while (File.Exists(absolutePath));
            return assetPath.Replace('\\', '/');
        }

        internal static void AddSourceVersion(ScreenshotCatalogSlot slot, Texture2D texture, bool trackNewVersion = false)
        {
            EnsureState(slot);
            bool isNew = texture != null && !slot.sourceVersions.Contains(texture);
            slot.activeSource = texture;
            if (isNew && trackNewVersion)
            {
                SetVersionTracked(slot, false, texture, true);
            }
            MarkCatalogDirty(slot);
        }

        internal static void AddFinalVersion(ScreenshotCatalogSlot slot, Texture2D texture, bool trackNewVersion = false)
        {
            EnsureState(slot);
            bool isNew = texture != null && !slot.finalVersions.Contains(texture);
            slot.activeFinal = texture;
            if (isNew && trackNewVersion)
            {
                SetVersionTracked(slot, true, texture, true);
            }
            MarkCatalogDirty(slot);
        }

        internal static bool IsVersionTracked(ScreenshotCatalogSlot slot, bool finalAsset, Texture2D texture)
        {
            string guid = GetAssetGuid(texture);
            if (slot == null || slot.state == null || string.IsNullOrEmpty(guid))
            {
                return false;
            }

            return GetTrackedAssetGuids(slot.state, finalAsset).Contains(guid);
        }

        internal static void SetVersionTracked(ScreenshotCatalogSlot slot, bool finalAsset, Texture2D texture, bool tracked)
        {
            if (slot == null || texture == null)
            {
                return;
            }

            EnsureState(slot);
            string guid = GetAssetGuid(texture);
            if (slot.state == null || string.IsNullOrEmpty(guid))
            {
                return;
            }

            List<string> trackedGuids = GetTrackedAssetGuids(slot.state, finalAsset);
            if (tracked)
            {
                if (!trackedGuids.Contains(guid))
                {
                    trackedGuids.Add(guid);
                }
            }
            else
            {
                trackedGuids.RemoveAll(item => item == guid);
            }
            MarkCatalogDirty(slot);
        }

        internal static void RemoveSourceVersion(ScreenshotCatalogSlot slot, int index)
        {
            RemoveVersion(slot, false, index);
        }

        internal static void RemoveFinalVersion(ScreenshotCatalogSlot slot, int index)
        {
            RemoveVersion(slot, true, index);
        }

        private static void RemoveVersion(ScreenshotCatalogSlot slot, bool finalAsset, int index)
        {
            List<Texture2D> versions = finalAsset ? slot.finalVersions : slot.sourceVersions;
            if (index < 0 || index >= versions.Count)
            {
                return;
            }

            Texture2D removed = versions[index];
            Texture2D active = finalAsset ? slot.activeFinal : slot.activeSource;
            versions.RemoveAt(index);
            Texture2D promoted = active != null && versions.Contains(active)
                ? active
                : versions.LastOrDefault(texture => texture != null);
            if (finalAsset)
            {
                slot.activeFinal = promoted;
            }
            else
            {
                slot.activeSource = promoted;
            }

            if (slot.catalog != null)
            {
                PruneEmptyObsolete(slot.catalog);
                EditorUtility.SetDirty(slot.catalog);
            }

            RemoveTrackedGuid(slot, finalAsset, removed);
        }

        private static bool HasAssignedImages(ScreenshotCatalogSlotState slot)
        {
            return slot.sourceVersions.Any(texture => texture != null) ||
                   slot.finalVersions.Any(texture => texture != null);
        }

        private static void RemoveTrackedGuid(ScreenshotCatalogSlot slot, bool finalAsset, Texture2D texture)
        {
            if (slot == null || slot.state == null)
            {
                return;
            }

            string guid = GetAssetGuid(texture);
            if (!string.IsNullOrEmpty(guid))
            {
                GetTrackedAssetGuids(slot.state, finalAsset).RemoveAll(item => item == guid);
            }
        }

        private static List<string> GetTrackedAssetGuids(ScreenshotCatalogSlotState state, bool finalAsset)
        {
            return finalAsset ? state.trackedFinalAssetGuids : state.trackedSourceAssetGuids;
        }

        private static string GetAssetGuid(Texture2D texture)
        {
            string path = texture == null ? null : AssetDatabase.GetAssetPath(texture);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        private static ScreenshotCatalogRequirement CreateRequirement(
            ScreenshotCatalog catalog,
            ScreenshotCatalogCategory category,
            ScreenshotRequirementDefinition definition,
            ScreenshotCatalogRequirementState state,
            bool obsolete)
        {
            return new ScreenshotCatalogRequirement
            {
                catalog = catalog,
                categoryDefinitionId = category.definitionId,
                categoryName = category.name,
                state = state,
                definitionId = definition != null ? definition.id : state.definitionId,
                name = definition != null ? definition.name : state.fallbackName,
                workflow = definition != null ? definition.workflow : state.fallbackWorkflow,
                width = Mathf.Max(1, definition != null ? definition.width : state.fallbackWidth),
                height = Mathf.Max(1, definition != null ? definition.height : state.fallbackHeight),
                dimensionRule = definition != null ? definition.dimensionRule : state.fallbackDimensionRule,
                imageFormat = definition != null ? definition.imageFormat : state.fallbackImageFormat,
                requireTransparency = definition != null ? definition.requireTransparency : state.fallbackRequireTransparency,
                guidance = definition != null ? definition.guidance : state.fallbackGuidance,
                sourceUrl = definition != null ? definition.sourceUrl : state.fallbackSourceUrl,
                obsolete = obsolete
            };
        }

        private static ScreenshotCatalogSlot CreateSlot(
            ScreenshotCatalog catalog,
            ScreenshotCatalogRequirement requirement,
            string id,
            string name,
            bool required,
            bool obsolete,
            ScreenshotCatalogSlotState state)
        {
            return new ScreenshotCatalogSlot
            {
                catalog = catalog,
                requirement = requirement,
                state = state,
                definitionId = id,
                name = name,
                required = required,
                obsolete = obsolete
            };
        }

        private static ScreenshotCatalogSlotState EnsureState(ScreenshotCatalogSlot slot)
        {
            if (slot.state != null || slot.catalog == null || slot.requirement == null)
            {
                return slot.state;
            }

            ScreenshotCatalogRequirementState requirementState = slot.requirement.state;
            if (requirementState == null)
            {
                requirementState = new ScreenshotCatalogRequirementState();
                slot.catalog.requirementStates.Add(requirementState);
                slot.requirement.state = requirementState;
            }

            UpdateFallbackMetadata(requirementState, slot.requirement);
            slot.state = new ScreenshotCatalogSlotState
            {
                definitionId = slot.definitionId,
                fallbackName = slot.name,
                fallbackRequired = slot.required
            };
            requirementState.slots.Add(slot.state);
            return slot.state;
        }

        private static void UpdateFallbackMetadata(ScreenshotCatalog catalog)
        {
            foreach (ScreenshotCatalogCategory category in ResolveCategories(catalog))
            {
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
                {
                    if (requirement.state == null)
                    {
                        continue;
                    }

                    UpdateFallbackMetadata(requirement.state, requirement);
                    foreach (ScreenshotCatalogSlot slot in requirement.slots.Where(item => item.state != null && !item.obsolete))
                    {
                        slot.state.fallbackName = slot.name;
                        slot.state.fallbackRequired = slot.required;
                    }
                }
            }
        }

        private static void UpdateFallbackMetadata(ScreenshotCatalogRequirementState state, ScreenshotCatalogRequirement requirement)
        {
            state.categoryDefinitionId = requirement.categoryDefinitionId;
            state.fallbackCategoryName = requirement.categoryName;
            state.definitionId = requirement.definitionId;
            state.fallbackName = requirement.name;
            state.fallbackWorkflow = requirement.workflow;
            state.fallbackWidth = requirement.width;
            state.fallbackHeight = requirement.height;
            state.fallbackDimensionRule = requirement.dimensionRule;
            state.fallbackImageFormat = requirement.imageFormat;
            state.fallbackRequireTransparency = requirement.requireTransparency;
            state.fallbackGuidance = requirement.guidance;
            state.fallbackSourceUrl = requirement.sourceUrl;
        }

        private static void MarkCatalogDirty(ScreenshotCatalogSlot slot)
        {
            if (slot.catalog != null)
            {
                EditorUtility.SetDirty(slot.catalog);
            }
        }

        private static void EnsureId(ref string id, HashSet<string> ids)
        {
            if (string.IsNullOrEmpty(id) || !ids.Add(id))
            {
                do
                {
                    id = Guid.NewGuid().ToString("N");
                }
                while (!ids.Add(id));
            }
        }

        private static int ReadBigEndianInt(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) |
                   (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) |
                   bytes[offset + 3];
        }
    }
}
