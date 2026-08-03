using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    internal static class ScreenshotTemplatePresets
    {
        internal const string MetaGuidelinesUrl = "https://developers.meta.com/horizon/resources/asset-guidelines/";

        [MenuItem("Assets/Create/Screenshotter/Meta Horizon Master Template", priority = 110)]
        private static void CreateMetaTemplateAsset()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Screenshot Master Template.asset");
            ScreenshotTemplate template = CreateMetaMasterTemplate();
            AssetDatabase.CreateAsset(template, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = template;
            EditorGUIUtility.PingObject(template);
        }

        internal static ScreenshotTemplate CreateMetaMasterTemplate()
        {
            ScreenshotTemplate template = ScriptableObject.CreateInstance<ScreenshotTemplate>();
            template.categories.Add(CreateMetaCategory());
            template.categories.Add(new ScreenshotCategoryDefinition { name = "Sales", includedByDefault = false });
            template.categories.Add(new ScreenshotCategoryDefinition { name = "One Pager", includedByDefault = false });
            template.categories.Add(new ScreenshotCategoryDefinition { name = "Website", includedByDefault = false });
            ScreenshotCatalogUtility.EnsureTemplateIds(template);
            return template;
        }

        private static ScreenshotCategoryDefinition CreateMetaCategory()
        {
            ScreenshotCategoryDefinition category = new ScreenshotCategoryDefinition
            {
                name = "Meta Distribution",
                includedByDefault = true
            };

            category.requirements.Add(CreateRequirement(
                "Hero Cover", ScreenshotAssetWorkflow.CaptureThenFinal, 3000, 900,
                "Capture a clean, well-lit scene with a strong central focal point and low UI. The final branded artwork must use the same design as the cover assets, keep the exact app title in the safe area, and remain legible when cropped."));
            category.requirements.Add(CreateRequirement(
                "Cover Landscape", ScreenshotAssetWorkflow.CaptureThenFinal, 2560, 1440,
                "Capture representative key art with room for the app title and safe-area cropping. Keep the final design consistent with the Hero, Square, and Portrait covers."));
            category.requirements.Add(CreateRequirement(
                "Cover Square", ScreenshotAssetWorkflow.CaptureThenFinal, 1440, 1440,
                "Frame the primary character, object, or scene so it remains recognizable at small sizes. The final design must match the other branded covers."));
            category.requirements.Add(CreateRequirement(
                "Cover Portrait", ScreenshotAssetWorkflow.CaptureThenFinal, 1008, 1440,
                "Use a vertically composed scene with the primary focal point and title treatment inside the safe area. Keep branding consistent with the other covers."));
            category.requirements.Add(CreateRequirement(
                "Mini Landscape", ScreenshotAssetWorkflow.CaptureThenFinal, 1080, 360,
                "Choose a simple wide composition that stays readable at small sizes and leaves room for safe cropping and title treatment."));

            ScreenshotRequirementDefinition screenshots = CreateRequirement(
                "Screenshot", ScreenshotAssetWorkflow.Capture, 2560, 1440,
                "Capture five unique, sharp in-experience scenes with clear focal points. Prefer gameplay point of view, minimize UI, use good lighting, and do not add banners, titles, badges, or marketing text.");
            screenshots.slots.Clear();
            for (int index = 1; index <= 5; index++)
            {
                screenshots.slots.Add(new ScreenshotSlotDefinition { name = "Screenshot " + index, required = true });
            }
            category.requirements.Add(screenshots);

            category.requirements.Add(CreateRequirement(
                "Trailer Cover Image", ScreenshotAssetWorkflow.CaptureThenFinal, 2560, 1440,
                "Capture a visually descriptive frame representative of the experience. Use the final image as the trailer thumbnail and do not include third-party marketing logos."));
            category.requirements.Add(CreateRequirement(
                "Logo Transparent", ScreenshotAssetWorkflow.External, 9000, 1440,
                "Assign a simple, recognizable, contrast-proof logo with a transparent background. The dimensions are maximum bounds.",
                ScreenshotDimensionRule.Maximum, ScreenshotImageFormat.Png32, true));
            category.requirements.Add(CreateRequirement(
                "Icon", ScreenshotAssetWorkflow.External, 512, 512,
                "Assign the final store icon. It must represent the experience, use square corners, remain legible when scaled down, and contain no transparency."));
            category.requirements.Add(CreateRequirement(
                "Spatialized Icon Background", ScreenshotAssetWorkflow.External, 180, 180,
                "Assign the square background layer for the spatialized app tile."));
            category.requirements.Add(CreateRequirement(
                "Spatialized Icon Foreground", ScreenshotAssetWorkflow.External, 180, 180,
                "Assign the transparent foreground layer, keeping important content inside the recommended padding and omitting baked shadows.",
                ScreenshotDimensionRule.Exact, ScreenshotImageFormat.Png32, true));

            return category;
        }

        private static ScreenshotRequirementDefinition CreateRequirement(
            string name,
            ScreenshotAssetWorkflow workflow,
            int width,
            int height,
            string guidance,
            ScreenshotDimensionRule dimensionRule = ScreenshotDimensionRule.Exact,
            ScreenshotImageFormat imageFormat = ScreenshotImageFormat.Png24,
            bool requireTransparency = false)
        {
            return new ScreenshotRequirementDefinition
            {
                name = name,
                workflow = workflow,
                width = width,
                height = height,
                dimensionRule = dimensionRule,
                imageFormat = imageFormat,
                requireTransparency = requireTransparency,
                guidance = guidance,
                sourceUrl = MetaGuidelinesUrl,
                slots = new List<ScreenshotSlotDefinition>
                {
                    new ScreenshotSlotDefinition { name = name, required = true }
                }
            };
        }
    }
}
