using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkatanicStudios
{
    internal static class ScreenshotterCameraSetup
    {
        internal static void Capture(Camera camera, string absolutePath, int width, int height)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture renderTexture = new RenderTexture(width, height, 24);
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(screenshot);
            }
        }

        internal static Screenshotter Ensure(Camera camera, out string warning)
        {
            warning = null;
            if (camera == null)
            {
                return null;
            }

            Screenshotter screenshotter = camera.GetComponent<Screenshotter>();
            if (screenshotter == null)
            {
                screenshotter = camera.gameObject.AddComponent<Screenshotter>();
                Screenshotter template = LoadTemplate();
                if (template != null)
                {
                    EditorUtility.CopySerialized(template, screenshotter);
                }
            }

            PlayerInput playerInput = camera.GetComponent<PlayerInput>();
            Screenshotter prefabTemplate = LoadTemplate();
            PlayerInput inputTemplate = prefabTemplate != null ? prefabTemplate.GetComponent<PlayerInput>() : null;
            if (playerInput != null && playerInput.actions == null && inputTemplate != null)
            {
                bool wasActive = playerInput.inputIsActive;
                playerInput.DeactivateInput();
                EditorUtility.CopySerialized(inputTemplate, playerInput);
                playerInput.camera = camera;
                if (wasActive || EditorApplication.isPlaying)
                {
                    playerInput.ActivateInput();
                }
            }
            else if (playerInput != null && inputTemplate != null && playerInput.actions != inputTemplate.actions)
            {
                warning = "The camera already has a PlayerInput using different actions. It was preserved; use the window Capture button because Screenshotter's F12 binding may be unavailable.";
            }

            return screenshotter;
        }

        private static Screenshotter LoadTemplate()
        {
            string path = AssetDatabase.FindAssets("Screenshotter Camera t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(candidate => candidate.Replace('\\', '/').Contains("com.skatanicstudios.screenshotter") ||
                                             candidate.Replace('\\', '/').EndsWith("Editor/Prefabs/Screenshotter Camera.prefab"));
            GameObject prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null ? prefab.GetComponent<Screenshotter>() : null;
        }
    }
}
