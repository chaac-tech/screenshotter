using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    internal static class GameViewResolutionUtility
    {
        private const BindingFlags AllBindings = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool TrySetResolution(int width, int height, out string error)
        {
            error = null;
            if (width <= 0 || height <= 0)
            {
                error = "The armed slot must have a positive width and height before it can update the Game View.";
                return false;
            }

            try
            {
                Assembly editorAssembly = typeof(Editor).Assembly;
                Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
                Type groupType = editorAssembly.GetType("UnityEditor.GameViewSizeGroupType");
                Type sizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
                Type sizeKindType = editorAssembly.GetType("UnityEditor.GameViewSizeType");
                Type gameViewType = editorAssembly.GetType("UnityEditor.GameView");
                if (sizesType == null || groupType == null || sizeType == null || sizeKindType == null || gameViewType == null)
                {
                    error = "Unity's internal Game View size API is unavailable in this editor version.";
                    return false;
                }

                object sizes;
                if (!TryGetSizeCollection(sizesType, out sizes))
                {
                    error = "Could not access the Unity Game View size collection.";
                    return false;
                }

                object groupValue = Enum.Parse(groupType, GetGroupName(groupType));
                MethodInfo getGroup = sizesType.GetMethod("GetGroup", AllBindings);
                object group = getGroup != null ? getGroup.Invoke(sizes, new[] { groupValue }) : null;
                if (group == null)
                {
                    error = "Could not access the active Game View size group.";
                    return false;
                }

                int sizeIndex = FindSizeIndex(group, width, height);
                if (sizeIndex < 0)
                {
                    sizeIndex = AddSize(group, sizeType, sizeKindType, width, height);
                }

                EditorWindow gameView = Resources.FindObjectsOfTypeAll(gameViewType)
                    .OfType<EditorWindow>()
                    .FirstOrDefault();
                if (gameView == null)
                {
                    gameView = EditorWindow.GetWindow(gameViewType);
                }

                MethodInfo sizeSelectionCallback = gameViewType.GetMethod("SizeSelectionCallback", AllBindings);
                PropertyInfo selectedSizeIndex = gameViewType.GetProperty("selectedSizeIndex", AllBindings);
                if (sizeSelectionCallback != null)
                {
                    sizeSelectionCallback.Invoke(gameView, new object[] { sizeIndex, null });
                }
                else if (selectedSizeIndex != null && selectedSizeIndex.CanWrite)
                {
                    selectedSizeIndex.SetValue(gameView, sizeIndex, null);
                }
                else
                {
                    error = "Could not select a Game View size in this editor version.";
                    return false;
                }

                RefreshGameView(gameView, gameViewType);
                EditorApplication.delayCall += () =>
                {
                    if (gameView != null)
                    {
                        RefreshGameView(gameView, gameViewType);
                    }
                };
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not update the Game View resolution: " + exception.GetBaseException().Message;
                return false;
            }
        }

        private static void RefreshGameView(EditorWindow gameView, Type gameViewType)
        {
            MethodInfo updateZoom = gameViewType.GetMethod("UpdateZoomAreaAndParent", AllBindings);
            if (updateZoom != null)
            {
                updateZoom.Invoke(gameView, null);
            }

            MethodInfo resized = gameViewType.GetMethod("OnResized", AllBindings);
            if (resized != null)
            {
                resized.Invoke(gameView, null);
            }

            gameView.Repaint();
            SceneView.RepaintAll();
        }

        internal static bool TryGetSizeCollection(out object sizes, out string error)
        {
            Type sizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
            if (sizesType == null)
            {
                sizes = null;
                error = "Unity's internal Game View size API is unavailable in this editor version.";
                return false;
            }

            if (!TryGetSizeCollection(sizesType, out sizes))
            {
                error = "Could not access the Unity Game View size collection.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryGetSizeCollection(Type sizesType, out object sizes)
        {
            for (Type type = sizesType; type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(
                    "instance",
                    AllBindings | BindingFlags.DeclaredOnly);
                MethodInfo getter = property != null ? property.GetGetMethod(true) : null;
                if (getter != null)
                {
                    sizes = getter.Invoke(null, null);
                    if (sizes != null)
                    {
                        return true;
                    }
                }

                FieldInfo field = type.GetField(
                    "s_Instance",
                    AllBindings | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    sizes = field.GetValue(null);
                    if (sizes != null)
                    {
                        return true;
                    }
                }
            }

            sizes = null;
            return false;
        }

        private static int FindSizeIndex(object group, int width, int height)
        {
            Type groupRuntimeType = group.GetType();
            MethodInfo getBuiltinCount = groupRuntimeType.GetMethod("GetBuiltinCount", AllBindings);
            MethodInfo getCustomCount = groupRuntimeType.GetMethod("GetCustomCount", AllBindings);
            MethodInfo getGameViewSize = groupRuntimeType.GetMethod("GetGameViewSize", AllBindings);
            int builtinCount = (int)getBuiltinCount.Invoke(group, null);
            int customCount = (int)getCustomCount.Invoke(group, null);
            for (int index = 0; index < builtinCount + customCount; index++)
            {
                object size = getGameViewSize.Invoke(group, new object[] { index });
                int candidateWidth = ReadIntMember(size, "width");
                int candidateHeight = ReadIntMember(size, "height");
                if (candidateWidth == width && candidateHeight == height)
                {
                    return index;
                }
            }
            return -1;
        }

        private static int AddSize(object group, Type sizeType, Type sizeKindType, int width, int height)
        {
            Type groupRuntimeType = group.GetType();
            int totalCount = (int)groupRuntimeType.GetMethod("GetBuiltinCount", AllBindings).Invoke(group, null) +
                             (int)groupRuntimeType.GetMethod("GetCustomCount", AllBindings).Invoke(group, null);
            object fixedResolution = Enum.Parse(sizeKindType, "FixedResolution");
            object size = Activator.CreateInstance(
                sizeType,
                AllBindings,
                null,
                new object[] { fixedResolution, width, height, "Screenshotter " + width + "x" + height },
                null);
            groupRuntimeType.GetMethod("AddCustomSize", AllBindings).Invoke(group, new[] { size });
            return totalCount;
        }

        private static int ReadIntMember(object target, string name)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, AllBindings);
            if (property != null)
            {
                return (int)property.GetValue(target, null);
            }
            FieldInfo field = type.GetField("m_" + char.ToUpperInvariant(name[0]) + name.Substring(1), AllBindings);
            return field != null ? (int)field.GetValue(target) : 0;
        }

        private static string GetGroupName(Type groupType)
        {
            string preferred;
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android:
                    preferred = "Android";
                    break;
                case BuildTarget.iOS:
                    preferred = "iOS";
                    break;
                default:
                    preferred = "Standalone";
                    break;
            }
            return Enum.GetNames(groupType).Contains(preferred) ? preferred : Enum.GetNames(groupType)[0];
        }
    }
}
