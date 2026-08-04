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
            EditorGUILayout.LabelField(Tip("Categories", "Groups of related deliverables that catalogs can include independently."), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(template.categories.Count.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            for (int categoryIndex = 0; categoryIndex < template.categories.Count; categoryIndex++)
            {
                DrawCategory(template, template.categories[categoryIndex], categoryIndex);
                EditorGUILayout.Space(2f);
            }

            if (GUILayout.Button(Tip("Add Category", "Add a new group of screenshot requirements to this master template."), GUILayout.Height(24f)))
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
                Tip(string.Format("{0}  ({1})", title, category.requirements.Count), "Expand this category to edit its name, default inclusion, and requirements."),
                true,
                EditorStyles.foldout);
            categoryFoldouts[category.id] = expanded;
            DrawMoveButtons(template, template.categories, categoryIndex, "Screenshot Category");
            if (GUILayout.Button(Tip("Remove", "Remove this category from the template. Existing synchronized catalog captures are retained as obsolete."), EditorStyles.miniButton, GUILayout.Width(58f)))
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
                EditorGUILayout.LabelField(Tip("Requirements", "Deliverable specifications contained in this category."), EditorStyles.miniBoldLabel);

                for (int requirementIndex = 0; requirementIndex < category.requirements.Count; requirementIndex++)
                {
                    DrawRequirement(template, category, category.requirements[requirementIndex], requirementIndex);
                    EditorGUILayout.Space(2f);
                }

                if (GUILayout.Button(Tip("Add Requirement", "Add a deliverable specification to this category.")))
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
            expanded = EditorGUILayout.Foldout(expanded, Tip(title, "Expand this requirement to edit its capture specification, guidance, and slots."), true);
            requirementFoldouts[requirement.id] = expanded;
            GUILayout.FlexibleSpace();
            GUILayout.Label(Tip(GetSpecification(requirement), "Workflow, target resolution, and PNG format for this requirement."), EditorStyles.miniLabel);
            DrawMoveButtons(template, category.requirements, requirementIndex, "Screenshot Requirement");
            if (GUILayout.Button(Tip("Remove", "Remove this requirement. Existing synchronized catalog captures are retained as obsolete."), EditorStyles.miniButton, GUILayout.Width(58f)))
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
                EditorGUILayout.PrefixLabel(Tip("Resolution", "Target pixel dimensions used for capture and asset validation."));
                DrawCompactIntField(template, "W", "Target width in pixels.", ref requirement.width);
                DrawCompactIntField(template, "H", "Target height in pixels.", ref requirement.height);
                EditorGUILayout.EndHorizontal();

                DrawEnumField(template, "Dimension Rule", ref requirement.dimensionRule);
                DrawEnumField(template, "Image Format", ref requirement.imageFormat);
                DrawBoolField(template, "Require Transparency", ref requirement.requireTransparency);
                DrawTextArea(template, "Capture Guidance", ref requirement.guidance);

                EditorGUILayout.BeginHorizontal();
                DrawStringField(template, "Source URL", ref requirement.sourceUrl);
                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(requirement.sourceUrl));
                if (GUILayout.Button(Tip("Open", "Open the source guidelines URL in the default browser."), GUILayout.Width(48f)))
                {
                    Application.OpenURL(requirement.sourceUrl);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(Tip("Slots", "Distinct images needed to satisfy this requirement, such as Screenshot 1 through Screenshot 5."), EditorStyles.miniBoldLabel);
                for (int slotIndex = 0; slotIndex < requirement.slots.Count; slotIndex++)
                {
                    DrawSlot(template, requirement, requirement.slots[slotIndex], slotIndex);
                }

                if (GUILayout.Button(Tip("Add Slot", "Add another independently tracked image slot to this requirement.")))
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
            DrawStringField(template, Tip(string.Empty, "Display name used for this slot in catalogs and generated filenames."), ref slot.name);
            bool required = GUILayout.Toggle(slot.required, Tip("Required", "Required slots count as missing until their workflow is complete."), GUILayout.Width(72f));
            if (required != slot.required)
            {
                Undo.RecordObject(template, "Edit Screenshot Template");
                slot.required = required;
                EditorUtility.SetDirty(template);
            }
            DrawMoveButtons(template, requirement.slots, slotIndex, "Screenshot Slot");
            EditorGUI.BeginDisabledGroup(requirement.slots.Count <= 1);
            if (GUILayout.Button(Tip("Remove", "Remove this slot. At least one slot must remain on each requirement."), EditorStyles.miniButton, GUILayout.Width(58f)))
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
            string next = EditorGUILayout.TextField(Tip(label, GetFieldTooltip(label)), value ?? string.Empty);
            ApplyValue(template, ref value, next);
        }

        private static void DrawStringField(ScreenshotTemplate template, GUIContent label, ref string value)
        {
            string next = EditorGUILayout.TextField(label, value ?? string.Empty);
            ApplyValue(template, ref value, next);
        }

        private static void DrawTextArea(ScreenshotTemplate template, string label, ref string value)
        {
            EditorGUILayout.LabelField(Tip(label, GetFieldTooltip(label)));
            string next = EditorGUILayout.TextArea(value ?? string.Empty, GUILayout.MinHeight(54f));
            ApplyValue(template, ref value, next);
        }

        private static void DrawBoolField(ScreenshotTemplate template, string label, ref bool value)
        {
            bool next = EditorGUILayout.Toggle(Tip(label, GetFieldTooltip(label)), value);
            ApplyValue(template, ref value, next);
        }

        private static void DrawCompactIntField(ScreenshotTemplate template, string label, string tooltip, ref int value)
        {
            GUILayout.Label(Tip(label, tooltip), GUILayout.Width(14f));
            int next = EditorGUILayout.IntField(Tip(string.Empty, tooltip), value, GUILayout.MinWidth(55f));
            ApplyValue(template, ref value, Mathf.Max(0, next));
        }

        private static void DrawEnumField<T>(ScreenshotTemplate template, string label, ref T value) where T : struct
        {
            T next = (T)(object)EditorGUILayout.EnumPopup(Tip(label, GetFieldTooltip(label)), (Enum)(object)value);
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
            if (GUILayout.Button(Tip("↑", "Move this item earlier in the template."), EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
            {
                Undo.RecordObject(template, "Move " + itemName);
                T item = list[index];
                list.RemoveAt(index);
                list.Insert(index - 1, item);
                FinishStructuralChange(template);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(index >= list.Count - 1);
            if (GUILayout.Button(Tip("↓", "Move this item later in the template."), EditorStyles.miniButtonRight, GUILayout.Width(24f)))
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

        private static GUIContent Tip(string text, string tooltip)
        {
            return new GUIContent(text, tooltip);
        }

        private static string GetFieldTooltip(string label)
        {
            switch (label)
            {
                case "Name":
                    return "Category name shown in templates, catalogs, and the generated output folder.";
                case "Included By Default":
                    return "Automatically include this category when a catalog first selects this template.";
                case "Asset Name":
                    return "Deliverable name shown in catalogs and used as the base of managed screenshot filenames.";
                case "Workflow":
                    return "Capture creates a finished image; External requires manual assignment; Capture Then Final keeps a raw capture and requires an edited final image.";
                case "Dimension Rule":
                    return "How assigned image dimensions are validated against the configured width and height.";
                case "Image Format":
                    return "Required PNG color format. PNG32 supports an alpha channel; PNG24 does not.";
                case "Require Transparency":
                    return "Require the assigned final PNG to contain an alpha channel.";
                case "Capture Guidance":
                    return "Instructions shown to the person preparing or capturing this asset.";
                case "Source URL":
                    return "Optional link to the platform or sales specification that defines this requirement.";
                default:
                    return string.Empty;
            }
        }
    }
}
