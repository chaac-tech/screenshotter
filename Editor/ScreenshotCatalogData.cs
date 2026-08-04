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
    internal sealed class ScreenshotCatalogSlot
    {
        [SerializeField, HideInInspector] internal string definitionId;
        [SerializeField] internal string name;
        [SerializeField] internal bool required;
        [SerializeField] internal bool obsolete;
        [SerializeField] internal List<Texture2D> sourceVersions = new List<Texture2D>();
        [SerializeField] internal Texture2D activeSource;
        [SerializeField] internal List<Texture2D> finalVersions = new List<Texture2D>();
        [SerializeField] internal Texture2D activeFinal;
    }

    [Serializable]
    internal sealed class ScreenshotCatalogRequirement
    {
        [SerializeField, HideInInspector] internal string definitionId;
        [SerializeField] internal string name;
        [SerializeField] internal ScreenshotAssetWorkflow workflow;
        [SerializeField] internal int width;
        [SerializeField] internal int height;
        [SerializeField] internal ScreenshotDimensionRule dimensionRule;
        [SerializeField] internal ScreenshotImageFormat imageFormat;
        [SerializeField] internal bool requireTransparency;
        [SerializeField, TextArea(3, 8)] internal string guidance;
        [SerializeField] internal string sourceUrl;
        [SerializeField] internal bool obsolete;
        [SerializeField] internal List<ScreenshotCatalogSlot> slots = new List<ScreenshotCatalogSlot>();
    }

    [Serializable]
    internal sealed class ScreenshotCatalogCategory
    {
        [SerializeField, HideInInspector] internal string definitionId;
        [SerializeField] internal string name;
        [SerializeField] internal bool obsolete;
        [SerializeField] internal List<ScreenshotCatalogRequirement> requirements = new List<ScreenshotCatalogRequirement>();
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
            HashSet<string> included = new HashSet<string>(catalog.includedCategoryIds);
            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                category.obsolete = true;
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
                {
                    requirement.obsolete = true;
                    foreach (ScreenshotCatalogSlot slot in requirement.slots)
                    {
                        slot.obsolete = true;
                    }
                }
            }

            foreach (ScreenshotCategoryDefinition categoryDefinition in catalog.template.categories)
            {
                if (!included.Contains(categoryDefinition.id))
                {
                    continue;
                }

                ScreenshotCatalogCategory category = catalog.categories.FirstOrDefault(item => item.definitionId == categoryDefinition.id);
                if (category == null)
                {
                    category = new ScreenshotCatalogCategory { definitionId = categoryDefinition.id };
                    catalog.categories.Add(category);
                }

                category.name = categoryDefinition.name;
                category.obsolete = false;
                foreach (ScreenshotRequirementDefinition requirementDefinition in categoryDefinition.requirements)
                {
                    ScreenshotCatalogRequirement requirement = category.requirements.FirstOrDefault(item => item.definitionId == requirementDefinition.id);
                    if (requirement == null)
                    {
                        requirement = new ScreenshotCatalogRequirement { definitionId = requirementDefinition.id };
                        category.requirements.Add(requirement);
                    }

                    CopyRequirement(requirementDefinition, requirement);
                    foreach (ScreenshotSlotDefinition slotDefinition in requirementDefinition.slots)
                    {
                        ScreenshotCatalogSlot slot = requirement.slots.FirstOrDefault(item => item.definitionId == slotDefinition.id);
                        if (slot == null)
                        {
                            slot = new ScreenshotCatalogSlot { definitionId = slotDefinition.id };
                            requirement.slots.Add(slot);
                        }

                        slot.name = slotDefinition.name;
                        slot.required = slotDefinition.required;
                        slot.obsolete = false;
                    }
                }
            }

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
            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
                {
                    removed += requirement.slots.RemoveAll(slot => slot.obsolete && !HasAssignedImages(slot));
                }

                removed += category.requirements.RemoveAll(requirement =>
                    requirement.obsolete && !requirement.slots.Any(HasAssignedImages));
            }

            removed += catalog.categories.RemoveAll(category =>
                category.obsolete && !category.requirements
                    .SelectMany(requirement => requirement.slots)
                    .Any(HasAssignedImages));

            if (removed > 0)
            {
                EditorUtility.SetDirty(catalog);
            }
            return removed;
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

        internal static void AddSourceVersion(ScreenshotCatalogSlot slot, Texture2D texture)
        {
            if (texture != null && !slot.sourceVersions.Contains(texture))
            {
                slot.sourceVersions.Add(texture);
            }
            slot.activeSource = texture;
        }

        internal static void AddFinalVersion(ScreenshotCatalogSlot slot, Texture2D texture)
        {
            if (texture != null && !slot.finalVersions.Contains(texture))
            {
                slot.finalVersions.Add(texture);
            }
            slot.activeFinal = texture;
        }

        internal static void RemoveSourceVersion(ScreenshotCatalogSlot slot, int index)
        {
            RemoveVersion(slot.sourceVersions, ref slot.activeSource, index);
        }

        internal static void RemoveFinalVersion(ScreenshotCatalogSlot slot, int index)
        {
            RemoveVersion(slot.finalVersions, ref slot.activeFinal, index);
        }

        private static void RemoveVersion(List<Texture2D> versions, ref Texture2D active, int index)
        {
            if (index < 0 || index >= versions.Count)
            {
                return;
            }

            Texture2D removed = versions[index];
            versions.RemoveAt(index);
            if (active == removed || !versions.Contains(active))
            {
                active = versions.LastOrDefault(texture => texture != null);
            }
        }

        private static bool HasAssignedImages(ScreenshotCatalogSlot slot)
        {
            return slot.activeSource != null ||
                   slot.activeFinal != null ||
                   slot.sourceVersions.Any(texture => texture != null) ||
                   slot.finalVersions.Any(texture => texture != null);
        }

        private static void CopyRequirement(ScreenshotRequirementDefinition source, ScreenshotCatalogRequirement destination)
        {
            destination.name = source.name;
            destination.workflow = source.workflow;
            destination.width = Mathf.Max(1, source.width);
            destination.height = Mathf.Max(1, source.height);
            destination.dimensionRule = source.dimensionRule;
            destination.imageFormat = source.imageFormat;
            destination.requireTransparency = source.requireTransparency;
            destination.guidance = source.guidance;
            destination.sourceUrl = source.sourceUrl;
            destination.obsolete = false;
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
