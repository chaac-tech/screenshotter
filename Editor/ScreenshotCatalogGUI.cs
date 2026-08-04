using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    internal static class ScreenshotCatalogGUI
    {
        internal static string GetSpecification(ScreenshotCatalogRequirement requirement)
        {
            return string.Format(
                "{0}  ·  {1} × {2}  ·  {3}  ·  {4}",
                GetWorkflowLabel(requirement.workflow),
                requirement.width,
                requirement.height,
                requirement.dimensionRule,
                requirement.imageFormat == ScreenshotImageFormat.Png32 ? "PNG32" : "PNG24");
        }

        private static string GetWorkflowLabel(ScreenshotAssetWorkflow workflow)
        {
            switch (workflow)
            {
                case ScreenshotAssetWorkflow.CaptureThenFinal:
                    return "Capture → Final";
                case ScreenshotAssetWorkflow.External:
                    return "External Final";
                default:
                    return "Capture";
            }
        }

        internal static Color GetStatusColor(ScreenshotCatalogStatus status)
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
                    return new Color(0.65f, 0.65f, 0.65f);
            }
        }

        internal static string GetStatusTooltip(ScreenshotCatalogStatus status)
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

        internal static void DrawStatusBadge(ScreenshotCatalogStatus status, string label = null)
        {
            DrawTextBadge(
                string.IsNullOrEmpty(label) ? ScreenshotCatalogUtility.GetStatusLabel(status) : label,
                GetStatusColor(status),
                GetStatusTooltip(status));
        }

        internal static void DrawCountBadge(string label, int count, ScreenshotCatalogStatus status)
        {
            DrawTextBadge(
                label + ": " + count,
                GetStatusColor(status),
                label + " catalog slot count.");
        }

        internal static void DrawTextBadge(string label, Color color, string tooltip)
        {
            GUIContent content = new GUIContent(label, tooltip);
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = color;
            float width = Mathf.Max(72f, style.CalcSize(content).x + 14f);
            Rect rect = GUILayoutUtility.GetRect(
                width,
                EditorGUIUtility.singleLineHeight,
                GUILayout.Width(width),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.18f));
            GUI.Label(rect, content, style);
        }
    }
}
