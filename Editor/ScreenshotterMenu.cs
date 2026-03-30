using UnityEditor;

namespace SkatanicStudios
{
    public class ScreenshotterMenu
    {
        [MenuItem("GameObject/Screenshotter Camera", false, 10)]
        public static void CreateCamera()
        {
            string[] path = AssetDatabase.FindAssets("Screenshotter Camera", null);
            var screenshotterPrefab = (Screenshotter) AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(path[0]), typeof(Screenshotter));
            PrefabUtility.InstantiatePrefab(screenshotterPrefab.gameObject, Selection.activeGameObject?.transform);
        }
    }
}
