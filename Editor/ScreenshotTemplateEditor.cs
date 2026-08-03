using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    [CustomEditor(typeof(ScreenshotTemplate))]
    internal sealed class ScreenshotTemplateEditor : Editor
    {
        private readonly Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> requirementFoldouts = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            ScreenshotTemplate template = (ScreenshotTemplate)target;
            ScreenshotCatalogUtility.EnsureTemplateIds(template);

            EditorGUILayout.HelpBox(
                "Categories and requirements are reusable definitions. Catalogs copy this metadata while retaining their captured images when synchronized.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Categories", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(template.categories.Count.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            for (int categoryIndex = 0; categoryIndex < template.categories.Count; categoryIndex++)
            {
                DrawCategory(template, template.categories[categoryIndex], categoryIndex);
                EditorGUILayout.Space(2f);
            }

            if (GUILayout.Button("Add Category", GUILayout.Height(24f)))
            {
                Undo.RecordObject(template, "Add Screenshot Category");
                ScreenshotCategoryDefinition category = new ScreenshotCategoryDefinition();
                template.categories.Add(category);
                FinishStructuralChange(template);
            }
        }

        private void DrawCategory(ScreenshotTemplate template, ScreenshotCategoryDefinition category, int categoryIndex)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            bool expanded = GetFoldout(categoryFoldouts, category.id, true);
            string title = string.IsNullOrWhiteSpace(category.name) ? "Unnamed Category" : category.name;
            expanded = EditorGUILayout.Foldout(
                expanded,
                string.Format("{0}  ({1})", title, category.requirements.Count),
                true,
                EditorStyles.foldout);
            categoryFoldouts[category.id] = expanded;
            DrawMoveButtons(template, template.categories, categoryIndex, "Screenshot Category");
            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58f)))
            {
                Undo.RecordObject(template, "Remove Screenshot Category");
                template.categories.RemoveAt(categoryIndex);
                FinishStructuralChange(template);
            }
            EditorGUILayout.EndHorizontal();

            if (expanded)
            {
                EditorGUI.indentLevel++;
                DrawStringField(template, "Name", ref category.name);
                DrawBoolField(template, "Included By Default", ref category.includedByDefault);
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Requirements", EditorStyles.miniBoldLabel);

                for (int requirementIndex = 0; requirementIndex < category.requirements.Count; requirementIndex++)
                {
                    DrawRequirement(template, category, category.requirements[requirementIndex], requirementIndex);
                    EditorGUILayout.Space(2f);
                }

                if (GUILayout.Button("Add Requirement"))
                {
                    Undo.RecordObject(template, "Add Screenshot Requirement");
                    ScreenshotRequirementDefinition requirement = new ScreenshotRequirementDefinition();
                    requirement.slots.Add(new ScreenshotSlotDefinition());
                    category.requirements.Add(requirement);
                    FinishStructuralChange(template);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRequirement(
            ScreenshotTemplate template,
            ScreenshotCategoryDefinition category,
            ScreenshotRequirementDefinition requirement,
            int requirementIndex)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            bool expanded = GetFoldout(requirementFoldouts, requirement.id, false);
            string title = string.IsNullOrWhiteSpace(requirement.name) ? "Unnamed Requirement" : requirement.name;
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            requirementFoldouts[requirement.id] = expanded;
            GUILayout.FlexibleSpace();
            GUILayout.Label(GetSpecification(requirement), EditorStyles.miniLabel);
            DrawMoveButtons(template, category.requirements, requirementIndex, "Screenshot Requirement");
            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58f)))
            {
                Undo.RecordObject(template, "Remove Screenshot Requirement");
                category.requirements.RemoveAt(requirementIndex);
                FinishStructuralChange(template);
            }
            EditorGUILayout.EndHorizontal();

            if (expanded)
            {
                EditorGUI.indentLevel++;
                DrawStringField(template, "Asset Name", ref requirement.name);
                DrawEnumField(template, "Workflow", ref requirement.workflow);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Resolution");
                DrawCompactIntField(template, "W", ref requirement.width);
                DrawCompactIntField(template, "H", ref requirement.height);
                EditorGUILayout.EndHorizontal();

                DrawEnumField(template, "Dimension Rule", ref requirement.dimensionRule);
                DrawEnumField(template, "Image Format", ref requirement.imageFormat);
                DrawBoolField(template, "Require Transparency", ref requirement.requireTransparency);
                DrawTextArea(template, "Capture Guidance", ref requirement.guidance);

                EditorGUILayout.BeginHorizontal();
                DrawStringField(template, "Source URL", ref requirement.sourceUrl);
                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(requirement.sourceUrl));
                if (GUILayout.Button("Open", GUILayout.Width(48f)))
                {
                    Application.OpenURL(requirement.sourceUrl);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Slots", EditorStyles.miniBoldLabel);
                for (int slotIndex = 0; slotIndex < requirement.slots.Count; slotIndex++)
                {
                    DrawSlot(template, requirement, requirement.slots[slotIndex], slotIndex);
                }

                if (GUILayout.Button("Add Slot"))
                {
                    Undo.RecordObject(template, "Add Screenshot Slot");
                    requirement.slots.Add(new ScreenshotSlotDefinition());
                    FinishStructuralChange(template);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawSlot(
            ScreenshotTemplate template,
            ScreenshotRequirementDefinition requirement,
            ScreenshotSlotDefinition slot,
            int slotIndex)
        {
            EditorGUILayout.BeginHorizontal();
            DrawStringField(template, GUIContent.none, ref slot.name);
            bool required = GUILayout.Toggle(slot.required, "Required", GUILayout.Width(72f));
            if (required != slot.required)
            {
                Undo.RecordObject(template, "Edit Screenshot Template");
                slot.required = required;
                EditorUtility.SetDirty(template);
            }
            DrawMoveButtons(template, requirement.slots, slotIndex, "Screenshot Slot");
            EditorGUI.BeginDisabledGroup(requirement.slots.Count <= 1);
            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58f)))
            {
                Undo.RecordObject(template, "Remove Screenshot Slot");
                requirement.slots.RemoveAt(slotIndex);
                FinishStructuralChange(template);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawStringField(ScreenshotTemplate template, string label, ref string value)
        {
            string next = EditorGUILayout.TextField(label, value ?? string.Empty);
            ApplyValue(template, ref value, next);
        }

        private static void DrawStringField(ScreenshotTemplate template, GUIContent label, ref string value)
        {
            string next = EditorGUILayout.TextField(label, value ?? string.Empty);
            ApplyValue(template, ref value, next);
        }

        private static void DrawTextArea(ScreenshotTemplate template, string label, ref string value)
        {
            EditorGUILayout.LabelField(label);
            string next = EditorGUILayout.TextArea(value ?? string.Empty, GUILayout.MinHeight(54f));
            ApplyValue(template, ref value, next);
        }

        private static void DrawBoolField(ScreenshotTemplate template, string label, ref bool value)
        {
            bool next = EditorGUILayout.Toggle(label, value);
            ApplyValue(template, ref value, next);
        }

        private static void DrawCompactIntField(ScreenshotTemplate template, string label, ref int value)
        {
            GUILayout.Label(label, GUILayout.Width(14f));
            int next = EditorGUILayout.IntField(value, GUILayout.MinWidth(55f));
            ApplyValue(template, ref value, Mathf.Max(0, next));
        }

        private static void DrawEnumField<T>(ScreenshotTemplate template, string label, ref T value) where T : struct
        {
            T next = (T)(object)EditorGUILayout.EnumPopup(label, (Enum)(object)value);
            ApplyValue(template, ref value, next);
        }

        private static void ApplyValue<T>(ScreenshotTemplate template, ref T value, T next)
        {
            if (EqualityComparer<T>.Default.Equals(value, next))
            {
                return;
            }

            Undo.RecordObject(template, "Edit Screenshot Template");
            value = next;
            EditorUtility.SetDirty(template);
        }

        private static void DrawMoveButtons<T>(ScreenshotTemplate template, List<T> list, int index, string itemName)
        {
            EditorGUI.BeginDisabledGroup(index == 0);
            if (GUILayout.Button("↑", EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
            {
                Undo.RecordObject(template, "Move " + itemName);
                T item = list[index];
                list.RemoveAt(index);
                list.Insert(index - 1, item);
                FinishStructuralChange(template);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(index >= list.Count - 1);
            if (GUILayout.Button("↓", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
            {
                Undo.RecordObject(template, "Move " + itemName);
                T item = list[index];
                list.RemoveAt(index);
                list.Insert(index + 1, item);
                FinishStructuralChange(template);
            }
            EditorGUI.EndDisabledGroup();
        }

        private static void FinishStructuralChange(ScreenshotTemplate template)
        {
            ScreenshotCatalogUtility.EnsureTemplateIds(template);
            EditorUtility.SetDirty(template);
            GUIUtility.ExitGUI();
        }

        private static bool GetFoldout(Dictionary<string, bool> foldouts, string id, bool defaultValue)
        {
            bool expanded;
            return foldouts.TryGetValue(id ?? string.Empty, out expanded) ? expanded : defaultValue;
        }

        private static string GetSpecification(ScreenshotRequirementDefinition requirement)
        {
            return string.Format(
                "{0} · {1}x{2} · {3}",
                requirement.workflow,
                requirement.width,
                requirement.height,
                requirement.imageFormat);
        }
    }
}
