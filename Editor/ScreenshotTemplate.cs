using System.Collections.Generic;
using UnityEngine;

namespace SkatanicStudios
{
    [CreateAssetMenu(fileName = "Screenshot Template", menuName = "Screenshotter/Requirement Template")]
    public sealed class ScreenshotTemplate : ScriptableObject
    {
        [SerializeField] internal List<ScreenshotCategoryDefinition> categories = new List<ScreenshotCategoryDefinition>();

        private void OnValidate()
        {
            ScreenshotCatalogUtility.EnsureTemplateIds(this);
        }
    }
}
