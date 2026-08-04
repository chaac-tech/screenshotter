using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SkatanicStudios
{
    internal sealed class ScreenshotCatalogWindowView
    {
        internal const float NarrowLayoutThreshold = 760f;
        internal const float TopBarWrapThreshold = 860f;

        internal static bool ShouldWrapTopBar(float width)
        {
            return width > 0f && width < TopBarWrapThreshold;
        }

        internal readonly IMGUIContainer topBar;
        internal readonly IMGUIContainer navigatorToolbar;
        internal readonly VisualElement navigatorContents;
        internal readonly IMGUIContainer workspaceContents;
        internal readonly IMGUIContainer statusBar;
        internal readonly ScrollView navigatorScrollView;
        internal readonly ScrollView workspaceScrollView;

        private readonly VisualElement body;
        private readonly VisualElement navigatorPane;
        private readonly VisualElement workspacePane;
        private readonly Action<VisualElement> buildNavigator;

        internal ScreenshotCatalogWindowView(
            VisualElement root,
            Action drawTopBar,
            Action drawNavigatorToolbar,
            Action<VisualElement> buildNavigator,
            Action drawWorkspace,
            Action drawStatusBar)
        {
            this.buildNavigator = buildNavigator;
            root.Clear();
            root.name = "screenshot-catalog-root";
            root.AddToClassList("screenshot-catalog-root");
            root.style.flexDirection = FlexDirection.Column;

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.skatanicstudios.screenshotter/Editor/ScreenshotCatalogWindow.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            topBar = CreateContainer("screenshot-catalog-top-bar", "screenshot-catalog-top-bar", drawTopBar);
            topBar.style.flexShrink = 0;
            root.Add(topBar);

            body = new VisualElement { name = "screenshot-catalog-body" };
            body.AddToClassList("screenshot-catalog-body");
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            root.Add(body);

            navigatorPane = new VisualElement { name = "screenshot-catalog-navigator" };
            navigatorPane.AddToClassList("screenshot-catalog-navigator");
            navigatorToolbar = CreateContainer(
                "screenshot-catalog-navigator-toolbar",
                "screenshot-catalog-navigator-toolbar",
                drawNavigatorToolbar);
            navigatorToolbar.style.flexShrink = 0f;
            navigatorPane.Add(navigatorToolbar);
            navigatorScrollView = new ScrollView { name = "screenshot-catalog-navigator-scroll" };
            navigatorScrollView.viewDataKey = "ScreenshotCatalogWindow.NavigatorScroll";
            navigatorScrollView.style.flexGrow = 1;
            navigatorContents = new VisualElement { name = "screenshot-catalog-navigator-contents" };
            navigatorContents.AddToClassList("screenshot-catalog-navigator-contents");
            navigatorScrollView.Add(navigatorContents);
            navigatorPane.Add(navigatorScrollView);
            body.Add(navigatorPane);

            workspacePane = new VisualElement { name = "screenshot-catalog-workspace" };
            workspacePane.AddToClassList("screenshot-catalog-workspace");
            workspacePane.style.flexGrow = 1;
            workspaceScrollView = new ScrollView { name = "screenshot-catalog-workspace-scroll" };
            workspaceScrollView.viewDataKey = "ScreenshotCatalogWindow.WorkspaceScroll";
            workspaceScrollView.style.flexGrow = 1;
            workspaceContents = CreateContainer(
                "screenshot-catalog-workspace-contents",
                "screenshot-catalog-workspace-contents",
                drawWorkspace);
            workspaceScrollView.Add(workspaceContents);
            workspacePane.Add(workspaceScrollView);
            body.Add(workspacePane);

            statusBar = CreateContainer("screenshot-catalog-status-bar", "screenshot-catalog-status-bar", drawStatusBar);
            statusBar.style.flexShrink = 0;
            root.Add(statusBar);

            body.RegisterCallback<GeometryChangedEvent>(evt => ApplyResponsiveLayout(evt.newRect.width));
            ApplyResponsiveLayout(root.layout.width);
            RefreshNavigator();
        }

        internal void MarkDirtyRepaint()
        {
            topBar.MarkDirtyRepaint();
            navigatorToolbar.MarkDirtyRepaint();
            workspaceContents.MarkDirtyRepaint();
            statusBar.MarkDirtyRepaint();
            RefreshNavigator();
        }

        internal void RefreshNavigator()
        {
            navigatorContents.Clear();
            buildNavigator?.Invoke(navigatorContents);
        }

        internal void ApplyResponsiveLayout(float width)
        {
            bool narrow = width > 0f && width < NarrowLayoutThreshold;
            body.style.flexDirection = narrow ? FlexDirection.Column : FlexDirection.Row;
            navigatorPane.EnableInClassList("screenshot-catalog-navigator--narrow", narrow);
            workspacePane.EnableInClassList("screenshot-catalog-workspace--narrow", narrow);

            if (narrow)
            {
                navigatorPane.style.width = new StyleLength(StyleKeyword.Auto);
                navigatorPane.style.height = 210f;
                navigatorPane.style.flexGrow = 0f;
            }
            else
            {
                float navigatorWidth = Mathf.Clamp(width * 0.28f, 220f, 320f);
                navigatorPane.style.width = navigatorWidth;
                navigatorPane.style.height = new StyleLength(StyleKeyword.Auto);
                navigatorPane.style.flexGrow = 0f;
            }
        }

        private static IMGUIContainer CreateContainer(string name, string className, Action draw)
        {
            IMGUIContainer container = new IMGUIContainer(draw) { name = name };
            container.AddToClassList(className);
            return container;
        }
    }
}
