using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    [CustomEditor(typeof(ScreenshotGoogleDriveProfile))]
    internal sealed class ScreenshotGoogleDriveProfileEditor : Editor
    {
        [Serializable]
        private sealed class GoogleCredentialsFile
        {
            public GoogleInstalledCredentials installed;
            public GoogleInstalledCredentials web;
        }

        [Serializable]
        private sealed class GoogleInstalledCredentials
        {
            public string client_id;
            public string client_secret;
        }

        public override void OnInspectorGUI()
        {
            ScreenshotGoogleDriveProfile profile = (ScreenshotGoogleDriveProfile)target;
            EditorGUILayout.HelpBox(
                "Editor-only OAuth settings shared by screenshot catalogs. Login tokens are stored under Library/Screenshotter and are not serialized into this asset.",
                MessageType.Info);

            DrawString(profile, "Application Name", "Name sent to Google Drive with API requests.", ref profile.applicationName);
            DrawString(profile, "Client ID", "Desktop OAuth client ID from Google Cloud Console.", ref profile.clientId);

            string secret = EditorGUILayout.PasswordField(
                new GUIContent("Client Secret", "Desktop OAuth client secret from Google Cloud Console. It identifies the installed application but cannot be kept confidential by a desktop app."),
                profile.clientSecret ?? string.Empty);
            if (secret != profile.clientSecret)
            {
                Undo.RecordObject(profile, "Edit Google Drive Profile");
                profile.clientSecret = secret;
                EditorUtility.SetDirty(profile);
            }

            if (GUILayout.Button(new GUIContent(
                    "Load OAuth Credentials JSON...",
                    "Load client_id and client_secret from the Desktop OAuth credentials JSON downloaded from Google Cloud Console.")))
            {
                LoadCredentials(profile);
            }

            EditorGUILayout.Space();
            string folder = EditorGUILayout.TextField(
                new GUIContent("Destination Folder", "Google Drive folder ID or folder URL. Use 'root' to create catalog folders directly in My Drive."),
                profile.destinationFolderId ?? string.Empty);
            if (folder != profile.destinationFolderId)
            {
                Undo.RecordObject(profile, "Edit Google Drive Profile");
                profile.destinationFolderId = ExtractFolderId(folder);
                EditorUtility.SetDirty(profile);
            }

            EditorGUILayout.HelpBox(
                "This integration requests Google Drive access so it can use a folder selected by ID. Uploads are immutable: untracking a local version never deletes the remote file.",
                MessageType.None);
        }

        internal static string ExtractFolderId(string value)
        {
            string trimmed = string.IsNullOrWhiteSpace(value) ? "root" : value.Trim();
            const string marker = "/folders/";
            int markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                string id = trimmed.Substring(markerIndex + marker.Length);
                int separator = id.IndexOfAny(new[] { '?', '#', '/' });
                return separator >= 0 ? id.Substring(0, separator) : id;
            }
            return trimmed;
        }

        private static void LoadCredentials(ScreenshotGoogleDriveProfile profile)
        {
            string path = EditorUtility.OpenFilePanel("Load Google OAuth Credentials", string.Empty, "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                GoogleCredentialsFile file = JsonUtility.FromJson<GoogleCredentialsFile>(File.ReadAllText(path));
                GoogleInstalledCredentials credentials = file != null && file.installed != null ? file.installed : file != null ? file.web : null;
                if (credentials == null || string.IsNullOrWhiteSpace(credentials.client_id))
                {
                    throw new InvalidDataException("The file does not contain installed or web OAuth client credentials.");
                }

                Undo.RecordObject(profile, "Load Google OAuth Credentials");
                profile.clientId = credentials.client_id;
                profile.clientSecret = credentials.client_secret;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Invalid OAuth Credentials", exception.Message, "OK");
            }
        }

        private static void DrawString(ScreenshotGoogleDriveProfile profile, string label, string tooltip, ref string value)
        {
            string next = EditorGUILayout.TextField(new GUIContent(label, tooltip), value ?? string.Empty);
            if (next == value)
            {
                return;
            }

            Undo.RecordObject(profile, "Edit Google Drive Profile");
            value = next;
            EditorUtility.SetDirty(profile);
        }
    }
}
