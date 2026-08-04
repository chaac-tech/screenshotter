using System.Collections.Generic;
using UnityEngine;

namespace SkatanicStudios
{
    [CreateAssetMenu(fileName = "Screenshot Catalog", menuName = "Screenshotter/Catalog")]
    public sealed class ScreenshotCatalog : ScriptableObject
    {
        [SerializeField] internal ScreenshotTemplate template;
        [SerializeField] internal string outputRoot = "Assets/Screenshots";
        [SerializeField] internal List<string> includedCategoryIds = new List<string>();
        [SerializeField] internal List<ScreenshotCatalogRequirementState> requirementStates = new List<ScreenshotCatalogRequirementState>();
        [SerializeField] internal ScreenshotGoogleDriveProfile googleDriveProfile;
        [SerializeField] internal List<ScreenshotGoogleDriveBinding> googleDriveBindings = new List<ScreenshotGoogleDriveBinding>();

        internal List<ScreenshotCatalogCategory> categories
        {
            get { return ScreenshotCatalogUtility.ResolveCategories(this); }
        }
    }
}
