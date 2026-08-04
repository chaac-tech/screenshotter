using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    internal static class ScreenshotCatalogIcons
    {
        private const string EvoIconRoot = "/Plugins/Evo/Evo UI/Sprites/Icons/";
        private const int EditorIconSize = 16;
        private const int TextIconPadding = 3;
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<int, Texture2D> TextPaddedCache = new Dictionary<int, Texture2D>();

        internal static Texture2D Settings { get { return LoadEvo("System/Settings.png", "SettingsIcon"); } }
        internal static Texture2D Play { get { return LoadEvo("Media/Play.png", "PlayButton"); } }
        internal static Texture2D Check { get { return LoadEvo("Status/Check.png", "TestPassed", 12); } }
        internal static Texture2D Remove { get { return LoadEvo("System/Trash Can.png", "TreeEditor.Trash", 13); } }
        internal static Texture2D Refresh { get { return LoadEvo("Navigation/Refresh.png", "Refresh"); } }
        internal static Texture2D Help { get { return LoadEvo("File/External Link.png", "_Help"); } }
        internal static Texture2D Camera { get { return LoadEvo("Media/Camera.png", "Camera Icon"); } }
        internal static Texture2D Folder { get { return LoadEvo("File/Folder Open.png", "Folder Icon"); } }
        internal static Texture2D Resolution { get { return LoadEvo("Device/Display.png", "ScaleTool"); } }
        internal static Texture2D Arm { get { return LoadEvo("Device/Scan.png", "Animation.Record"); } }

        internal static GUIContent Content(Texture2D image, string text, string tooltip)
        {
            Texture2D displayedImage = image;
            if (displayedImage != null && !string.IsNullOrEmpty(text))
            {
                displayedImage = AddTextPadding(displayedImage);
            }
            return new GUIContent(text, displayedImage, tooltip);
        }

        private static Texture2D LoadEvo(string relativePath, string unityFallback, int iconSize = EditorIconSize)
        {
            string cacheKey = relativePath + ":" + iconSize + (EditorGUIUtility.isProSkin ? ":dark" : ":light");
            Texture2D texture;
            if (Cache.TryGetValue(cacheKey, out texture))
            {
                return texture;
            }

            // Evo UI's icon set is white, so it is only suitable for Unity's dark theme.
            // Discover it instead of taking a package dependency; Screenshotter remains standalone.
            texture = EditorGUIUtility.isProSkin ? FindEvoTexture(relativePath, iconSize) : null;
            if (texture == null)
            {
                texture = LoadUnity(unityFallback);
                if (texture != null && iconSize != EditorIconSize)
                {
                    texture = CreateEditorSizedIcon(texture, iconSize);
                }
            }

            Cache[cacheKey] = texture;
            return texture;
        }

        private static Texture2D FindEvoTexture(string relativePath, int iconSize)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(relativePath);
            string expectedSuffix = EvoIconRoot + relativePath;
            string[] guids = AssetDatabase.FindAssets(fileName + " t:Sprite");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (!assetPath.EndsWith(expectedSuffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                Texture2D source = sprite != null
                    ? sprite.texture
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                return CreateEditorSizedIcon(source, iconSize);
            }

            return null;
        }

        private static Texture2D CreateEditorSizedIcon(Texture2D source, int iconSize)
        {
            if (source == null || (source.width == iconSize && source.height == iconSize))
            {
                return source;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(
                iconSize,
                iconSize,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            try
            {
                target.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, target);
                RenderTexture.active = target;

                Texture2D icon = new Texture2D(iconSize, iconSize, TextureFormat.RGBA32, false);
                icon.name = source.name + " (Screenshot Catalog)";
                icon.hideFlags = HideFlags.HideAndDontSave;
                icon.ReadPixels(new Rect(0, 0, iconSize, iconSize), 0, 0);
                icon.Apply(false, true);
                return icon;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Texture2D AddTextPadding(Texture2D source)
        {
            int sourceId = source.GetInstanceID();
            Texture2D padded;
            if (TextPaddedCache.TryGetValue(sourceId, out padded))
            {
                return padded;
            }

            int height = Mathf.Max(EditorIconSize, source.height);
            int width = source.width + TextIconPadding;
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            try
            {
                target.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, target);
                RenderTexture.active = target;

                padded = new Texture2D(width, height, TextureFormat.RGBA32, false);
                padded.name = source.name + " (Text Padded)";
                padded.hideFlags = HideFlags.HideAndDontSave;
                padded.SetPixels32(new Color32[width * height]);
                padded.ReadPixels(
                    new Rect(0, 0, source.width, source.height),
                    0,
                    Mathf.Max(0, (height - source.height) / 2));
                padded.Apply(false, true);
                TextPaddedCache[sourceId] = padded;
                return padded;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Texture2D LoadUnity(string name)
        {
            GUIContent content = EditorGUIUtility.IconContent(name);
            return content == null ? null : content.image as Texture2D;
        }
    }
}
