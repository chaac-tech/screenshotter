using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    [CustomEditor(typeof(ScreenshotCatalog))]
    internal sealed class ScreenshotCatalogEditor : Editor
    {
        private readonly Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            ScreenshotCatalog catalog = (ScreenshotCatalog)target;

            if (GUILayout.Button("Open Screenshot Catalog", GUILayout.Height(28)))
            {
                ScreenshotCatalogWindow.Open(catalog);
            }

            EditorGUILayout.Space();
            DrawOverview(catalog);

            if (catalog.template == null)
            {
                EditorGUILayout.HelpBox(
                    "No master template is assigned. Open the catalog window and select one from the Master Template dropdown.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            DrawSummary(catalog);
            EditorGUILayout.Space();
            DrawCategories(catalog);
        }

        private static void DrawOverview(ScreenshotCatalog catalog)
        {
            EditorGUILayout.LabelField("Catalog", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Master Template", catalog.template, typeof(ScreenshotTemplate), false);
            EditorGUILayout.TextField("Output Root", catalog.outputRoot);
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawSummary(ScreenshotCatalog catalog)
        {
            int missing = 0;
            int sourceReady = 0;
            int complete = 0;
            int invalid = 0;
            int obsolete = 0;

            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
                {
                    foreach (ScreenshotCatalogSlot slot in requirement.slots)
                    {
                        switch (ScreenshotCatalogUtility.GetStatus(requirement, slot))
                        {
                            case ScreenshotCatalogStatus.Missing:
                                if (slot.required) missing++;
                                break;
                            case ScreenshotCatalogStatus.SourceReady:
                                sourceReady++;
                                break;
                            case ScreenshotCatalogStatus.Complete:
                                complete++;
                                break;
                            case ScreenshotCatalogStatus.Invalid:
                                invalid++;
                                break;
                            case ScreenshotCatalogStatus.Obsolete:
                                obsolete++;
                                break;
                        }
                    }
                }
            }

            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            DrawCount("Complete", complete, new Color(0.35f, 0.8f, 0.4f));
            DrawCount("Source Ready", sourceReady, new Color(0.95f, 0.75f, 0.25f));
            DrawCount("Missing", missing, Color.white);
            DrawCount("Invalid", invalid, new Color(1f, 0.35f, 0.35f));
            if (obsolete > 0)
            {
                DrawCount("Obsolete", obsolete, Color.gray);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategories(ScreenshotCatalog catalog)
        {
            EditorGUILayout.LabelField("Requirements", EditorStyles.boldLabel);
            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                bool expanded;
                if (!categoryFoldouts.TryGetValue(category.definitionId, out expanded))
                {
                    expanded = !category.obsolete;
                }

                expanded = EditorGUILayout.Foldout(
                    expanded,
                    category.name + (category.obsolete ? " (Obsolete)" : string.Empty),
                    true);
                categoryFoldouts[category.definitionId] = expanded;
                if (!expanded)
                {
                    continue;
                }

                EditorGUI.indentLevel++;
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
                {
                    DrawRequirement(requirement);
                }
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawRequirement(ScreenshotCatalogRequirement requirement)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                requirement.name + (requirement.obsolete ? " (Obsolete)" : string.Empty),
                EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                string.Format("{0} · {1}x{2} · {3}", requirement.workflow, requirement.width, requirement.height, requirement.imageFormat),
                EditorStyles.miniLabel);

            foreach (ScreenshotCatalogSlot slot in requirement.slots)
            {
                ScreenshotCatalogStatus status = ScreenshotCatalogUtility.GetStatus(requirement, slot);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(slot.name + (slot.required ? " *" : string.Empty), GUILayout.MinWidth(120));
                Color previousColor = GUI.color;
                GUI.color = GetStatusColor(status);
                GUILayout.Label(status.ToString(), EditorStyles.miniBoldLabel, GUILayout.Width(85));
                GUI.color = previousColor;
                Texture2D active = requirement.workflow == ScreenshotAssetWorkflow.Capture ? slot.activeSource : slot.activeFinal;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(active, typeof(Texture2D), false, GUILayout.MinWidth(100));
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawCount(string label, int count, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUILayout.Label(label + ": " + count, EditorStyles.miniBoldLabel);
            GUI.color = previousColor;
        }

        private static Color GetStatusColor(ScreenshotCatalogStatus status)
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
                    return Color.white;
            }
        }
    }
}
