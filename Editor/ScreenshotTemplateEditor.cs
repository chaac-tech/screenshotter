using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    [CustomEditor(typeof(ScreenshotTemplate))]
    internal sealed class ScreenshotTemplateEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Categories and requirements are reusable definitions. Catalogs copy their metadata and retain captured images when synchronized.",
                MessageType.Info);
            DrawDefaultInspector();

            if (GUILayout.Button("Repair Stable IDs"))
            {
                ScreenshotTemplate template = (ScreenshotTemplate)target;
                Undo.RecordObject(template, "Repair Screenshot Template IDs");
                ScreenshotCatalogUtility.EnsureTemplateIds(template);
                EditorUtility.SetDirty(template);
            }
        }
    }
}
