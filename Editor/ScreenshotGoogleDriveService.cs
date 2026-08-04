using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace SkatanicStudios
{
    internal sealed class ScreenshotGoogleDrivePushResult
    {
        internal int uploaded;
        internal int alreadySynced;
        internal int skipped;
    }

    internal sealed class ScreenshotGoogleDrivePullResult
    {
        internal int downloaded;
        internal int alreadySynced;
        internal int unmatched;
        internal int metadataFailures;
    }

    internal static class ScreenshotGoogleDriveService
    {
        private const string DriveScope = "https://www.googleapis.com/auth/drive";
        private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string DriveFilesEndpoint = "https://www.googleapis.com/drive/v3/files";
        private const string DriveUploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        [Serializable]
        private sealed class StoredToken
        {
            public string access_token;
            public string refresh_token;
            public long expires_utc_ticks;
        }

        [Serializable]
        private sealed class TokenResponse
        {
            public string access_token;
            public string refresh_token;
            public int expires_in;
            public string error;
            public string error_description;
        }

        [Serializable]
        private sealed class DriveFile
        {
            public string id;
            public string name;
            public string webViewLink;
            public string md5Checksum;
            public string mimeType;
            public DriveAppProperties appProperties;
        }

        [Serializable]
        private sealed class DriveAppProperties
        {
            public string screenshotterCatalog;
            public string screenshotterCategory;
            public string screenshotterRequirement;
            public string screenshotterSlot;
            public string screenshotterKind;
            public string screenshotterAssetGuid;
        }

        [Serializable]
        private sealed class DriveFileList
        {
            public DriveFile[] files;
            public string nextPageToken;
        }

        private sealed class UploadJob
        {
            internal string categoryId;
            internal string categoryName;
            internal string requirementId;
            internal string slotId;
            internal bool finalAsset;
            internal string assetGuid;
            internal string assetPath;
            internal string fileName;
        }

        internal static bool IsConfigured(ScreenshotGoogleDriveProfile profile)
        {
            return profile != null && !string.IsNullOrWhiteSpace(profile.clientId);
        }

        internal static bool IsAuthorized(ScreenshotGoogleDriveProfile profile)
        {
            StoredToken token = LoadToken(profile);
            return token != null && (!string.IsNullOrWhiteSpace(token.refresh_token) ||
                                     (!string.IsNullOrWhiteSpace(token.access_token) && token.expires_utc_ticks > DateTime.UtcNow.Ticks));
        }

        internal static async Task AuthorizeAsync(ScreenshotGoogleDriveProfile profile)
        {
            if (!IsConfigured(profile))
            {
                throw new InvalidOperationException("Assign a Desktop OAuth client ID in the Google Drive sync profile first.");
            }

            string verifier = CreateRandomUrlToken(64);
            string challenge;
            using (SHA256 sha = SHA256.Create())
            {
                challenge = ToBase64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            }
            string state = CreateRandomUrlToken(24);
            int port = ReserveLoopbackPort();
            string redirectUri = "http://127.0.0.1:" + port + "/";

            using (HttpListener listener = new HttpListener())
            {
                listener.Prefixes.Add(redirectUri);
                listener.Start();

                string authorizationUrl = AuthorizationEndpoint +
                    "?client_id=" + Encode(profile.clientId) +
                    "&redirect_uri=" + Encode(redirectUri) +
                    "&response_type=code" +
                    "&scope=" + Encode(DriveScope) +
                    "&access_type=offline" +
                    "&prompt=consent" +
                    "&code_challenge=" + Encode(challenge) +
                    "&code_challenge_method=S256" +
                    "&state=" + Encode(state);
                Application.OpenURL(authorizationUrl);

                Task<HttpListenerContext> contextTask = listener.GetContextAsync();
                Task completed = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(3)));
                if (completed != contextTask)
                {
                    throw new TimeoutException("Google authorization timed out. Try Connect again.");
                }

                HttpListenerContext context = await contextTask;
                string returnedState = context.Request.QueryString["state"];
                string code = context.Request.QueryString["code"];
                string oauthError = context.Request.QueryString["error"];
                WriteBrowserResponse(context, string.IsNullOrEmpty(oauthError), oauthError);

                if (!string.Equals(state, returnedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Google authorization returned an invalid state value.");
                }
                if (!string.IsNullOrEmpty(oauthError))
                {
                    throw new InvalidOperationException("Google authorization failed: " + oauthError);
                }
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new InvalidOperationException("Google authorization did not return an authorization code.");
                }

                Dictionary<string, string> values = new Dictionary<string, string>
                {
                    { "client_id", profile.clientId },
                    { "code", code },
                    { "code_verifier", verifier },
                    { "grant_type", "authorization_code" },
                    { "redirect_uri", redirectUri }
                };
                if (!string.IsNullOrWhiteSpace(profile.clientSecret))
                {
                    values["client_secret"] = profile.clientSecret;
                }

                TokenResponse response = await PostTokenAsync(values);
                StoredToken previous = LoadToken(profile);
                SaveToken(profile, new StoredToken
                {
                    access_token = response.access_token,
                    refresh_token = !string.IsNullOrWhiteSpace(response.refresh_token)
                        ? response.refresh_token
                        : previous != null ? previous.refresh_token : null,
                    expires_utc_ticks = DateTime.UtcNow.AddSeconds(Math.Max(60, response.expires_in)).Ticks
                });
            }
        }

        internal static void Disconnect(ScreenshotGoogleDriveProfile profile)
        {
            string path = GetTokenPath(profile);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        internal static string GetFolderUrl(ScreenshotGoogleDriveProfile profile)
        {
            string folderId = GetDestinationFolderId(profile);
            return folderId == "root"
                ? "https://drive.google.com/drive/my-drive"
                : "https://drive.google.com/drive/folders/" + folderId;
        }

        internal static int CountPending(ScreenshotCatalog catalog)
        {
            if (catalog == null)
            {
                return 0;
            }
            return BuildUploadJobs(catalog).Count;
        }

        internal static bool IsVersionSynced(ScreenshotCatalog catalog, ScreenshotCatalogSlot slot, bool finalAsset, Texture2D texture)
        {
            if (catalog == null || slot == null || texture == null)
            {
                return false;
            }
            string path = AssetDatabase.GetAssetPath(texture);
            string guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
            string profileGuid = GetProfileGuid(catalog.googleDriveProfile);
            return !string.IsNullOrEmpty(guid) && catalog.googleDriveBindings.Any(binding =>
                binding.profileGuid == profileGuid &&
                binding.slotDefinitionId == slot.definitionId &&
                binding.finalAsset == finalAsset &&
                binding.localAssetGuid == guid &&
                !string.IsNullOrEmpty(binding.driveFileId));
        }

        internal static async Task<ScreenshotGoogleDrivePushResult> PushNewAsync(
            ScreenshotCatalog catalog,
            Action<string> reportProgress)
        {
            if (catalog == null || catalog.googleDriveProfile == null)
            {
                throw new InvalidOperationException("Assign a Google Drive sync profile to the catalog first.");
            }
            if (!IsConfigured(catalog.googleDriveProfile))
            {
                throw new InvalidOperationException("The Google Drive sync profile has no OAuth client ID.");
            }

            List<UploadJob> jobs = BuildUploadJobs(catalog);
            ScreenshotGoogleDrivePushResult result = new ScreenshotGoogleDrivePushResult();
            result.alreadySynced = CountSyncedVersions(catalog);
            result.skipped = Math.Max(0, CountTrackedVersions(catalog) - result.alreadySynced - jobs.Count);
            if (jobs.Count == 0)
            {
                return result;
            }

            string accessToken = await GetAccessTokenAsync(catalog.googleDriveProfile);
            string catalogGuid = GetCatalogGuid(catalog);
            string profileGuid = GetProfileGuid(catalog.googleDriveProfile);
            string rootFolderId = GetDestinationFolderId(catalog.googleDriveProfile);
            reportProgress?.Invoke("Preparing Drive folder for " + catalog.name + "...");
            string catalogFolderId = await EnsureFolderAsync(
                accessToken,
                rootFolderId,
                ScreenshotCatalogUtility.SanitizeName(catalog.name),
                "catalog:" + catalogGuid);

            Dictionary<string, string> categoryFolders = new Dictionary<string, string>();
            Dictionary<string, string> assetFolders = new Dictionary<string, string>();
            for (int index = 0; index < jobs.Count; index++)
            {
                UploadJob job = jobs[index];
                string categoryFolderId;
                if (!categoryFolders.TryGetValue(job.categoryId, out categoryFolderId))
                {
                    categoryFolderId = await EnsureFolderAsync(
                        accessToken,
                        catalogFolderId,
                        ScreenshotCatalogUtility.SanitizeName(job.categoryName),
                        "category:" + catalogGuid + ":" + job.categoryId);
                    categoryFolders[job.categoryId] = categoryFolderId;
                }

                string kind = job.finalAsset ? "final" : "source";
                string assetFolderKey = job.categoryId + ":" + kind;
                string assetFolderId;
                if (!assetFolders.TryGetValue(assetFolderKey, out assetFolderId))
                {
                    assetFolderId = await EnsureFolderAsync(
                        accessToken,
                        categoryFolderId,
                        job.finalAsset ? "Final" : "Source",
                        "kind:" + catalogGuid + ":" + job.categoryId + ":" + kind);
                    assetFolders[assetFolderKey] = assetFolderId;
                }

                reportProgress?.Invoke(string.Format("Uploading {0} ({1}/{2})...", job.fileName, index + 1, jobs.Count));
                DriveFile uploaded = await UploadFileAsync(accessToken, assetFolderId, catalogGuid, job);
                catalog.googleDriveBindings.Add(new ScreenshotGoogleDriveBinding
                {
                    profileGuid = profileGuid,
                    slotDefinitionId = job.slotId,
                    finalAsset = job.finalAsset,
                    localAssetGuid = job.assetGuid,
                    driveFileId = uploaded.id
                });
                result.uploaded++;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            reportProgress?.Invoke("Google Drive sync complete.");
            return result;
        }

        internal static async Task<ScreenshotGoogleDrivePullResult> PullNewAsync(
            ScreenshotCatalog catalog,
            Action<string> reportProgress)
        {
            if (catalog == null || catalog.googleDriveProfile == null)
            {
                throw new InvalidOperationException("Assign a Google Drive sync profile to the catalog first.");
            }
            if (!IsConfigured(catalog.googleDriveProfile))
            {
                throw new InvalidOperationException("The Google Drive sync profile has no OAuth client ID.");
            }

            string accessToken = await GetAccessTokenAsync(catalog.googleDriveProfile);
            string catalogGuid = GetCatalogGuid(catalog);
            string profileGuid = GetProfileGuid(catalog.googleDriveProfile);
            string rootFolderId = GetDestinationFolderId(catalog.googleDriveProfile);
            ScreenshotGoogleDrivePullResult result = new ScreenshotGoogleDrivePullResult();
            reportProgress?.Invoke("Preparing Drive folders for " + catalog.name + "...");
            string catalogFolderId = await EnsureFolderAsync(
                accessToken,
                rootFolderId,
                ScreenshotCatalogUtility.SanitizeName(catalog.name),
                "catalog:" + catalogGuid);

            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                string categoryFolderId = await EnsureFolderAsync(
                    accessToken,
                    catalogFolderId,
                    ScreenshotCatalogUtility.SanitizeName(category.name),
                    "category:" + catalogGuid + ":" + category.definitionId);

                await PullFolderAsync(catalog, category, false, accessToken, profileGuid, catalogGuid,
                    categoryFolderId, result, reportProgress);
                await PullFolderAsync(catalog, category, true, accessToken, profileGuid, catalogGuid,
                    categoryFolderId, result, reportProgress);
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            reportProgress?.Invoke("Google Drive pull complete.");
            return result;
        }

        private static async Task PullFolderAsync(
            ScreenshotCatalog catalog,
            ScreenshotCatalogCategory category,
            bool finalAsset,
            string accessToken,
            string profileGuid,
            string catalogGuid,
            string categoryFolderId,
            ScreenshotGoogleDrivePullResult result,
            Action<string> reportProgress)
        {
            string kind = finalAsset ? "final" : "source";
            string folderId = await EnsureFolderAsync(
                accessToken,
                categoryFolderId,
                finalAsset ? "Final" : "Source",
                "kind:" + catalogGuid + ":" + category.definitionId + ":" + kind);
            List<DriveFile> files = await ListPngFilesAsync(accessToken, folderId);
            foreach (DriveFile file in files.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase))
            {
                if (catalog.googleDriveBindings.Any(binding =>
                        binding.profileGuid == profileGuid && binding.driveFileId == file.id))
                {
                    result.alreadySynced++;
                    continue;
                }

                ScreenshotCatalogRequirement requirement;
                ScreenshotCatalogSlot slot;
                if (!TryMatchRemoteFile(category, file, finalAsset, catalogGuid, out requirement, out slot))
                {
                    result.unmatched++;
                    continue;
                }

                reportProgress?.Invoke("Downloading " + file.name + "...");
                byte[] bytes = await DownloadFileAsync(accessToken, file.id);
                string assetPath = GetDownloadAssetPath(catalog, category, finalAsset, file.name);
                string absolutePath = ToAbsoluteAssetPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    result.unmatched++;
                    continue;
                }

                if (finalAsset)
                {
                    ScreenshotCatalogUtility.AddFinalVersion(slot, texture, true);
                }
                else
                {
                    ScreenshotCatalogUtility.AddSourceVersion(slot, texture, true);
                }

                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                catalog.googleDriveBindings.Add(new ScreenshotGoogleDriveBinding
                {
                    profileGuid = profileGuid,
                    slotDefinitionId = slot.definitionId,
                    finalAsset = finalAsset,
                    localAssetGuid = assetGuid,
                    driveFileId = file.id
                });
                result.downloaded++;

                try
                {
                    await ApplyFileMetadataAsync(accessToken, file.id, catalogGuid, category.definitionId,
                        requirement.definitionId, slot.definitionId, finalAsset, assetGuid);
                }
                catch (Exception exception)
                {
                    result.metadataFailures++;
                    Debug.LogWarning("Screenshotter imported '" + file.name +
                                     "' but could not add its Google Drive metadata: " + exception.GetBaseException().Message);
                }
            }
        }

        private static bool TryMatchRemoteFile(
            ScreenshotCatalogCategory category,
            DriveFile file,
            bool finalAsset,
            string catalogGuid,
            out ScreenshotCatalogRequirement requirement,
            out ScreenshotCatalogSlot slot)
        {
            requirement = null;
            slot = null;
            DriveAppProperties properties = file.appProperties;
            if (properties != null &&
                (string.IsNullOrEmpty(properties.screenshotterCatalog) || properties.screenshotterCatalog == catalogGuid) &&
                properties.screenshotterCategory == category.definitionId &&
                properties.screenshotterKind == (finalAsset ? "final" : "source"))
            {
                requirement = category.requirements.FirstOrDefault(item => item.definitionId == properties.screenshotterRequirement);
                slot = requirement == null
                    ? null
                    : requirement.slots.FirstOrDefault(item => item.definitionId == properties.screenshotterSlot);
                if (slot != null)
                {
                    return true;
                }
            }

            return TryMatchRemoteFilename(category, file.name, out requirement, out slot);
        }

        internal static bool TryMatchRemoteFilename(
            ScreenshotCatalogCategory category,
            string fileName,
            out ScreenshotCatalogRequirement requirement,
            out ScreenshotCatalogSlot slot)
        {
            requirement = null;
            slot = null;
            if (category == null || string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var matches = category.requirements
                .SelectMany(candidateRequirement => candidateRequirement.slots.Select((candidateSlot, slotIndex) => new
                {
                    requirement = candidateRequirement,
                    slot = candidateSlot,
                    prefix = ScreenshotCatalogUtility.SanitizeName(candidateRequirement.name) +
                             "-" + (slotIndex + 1).ToString("00") + "-v"
                }))
                .Where(candidate =>
                    nameWithoutExtension.StartsWith(candidate.prefix, StringComparison.OrdinalIgnoreCase) &&
                    nameWithoutExtension.Length > candidate.prefix.Length &&
                    nameWithoutExtension.Substring(candidate.prefix.Length).All(char.IsDigit))
                .ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            requirement = matches[0].requirement;
            slot = matches[0].slot;
            return true;
        }

        internal static string GetDownloadAssetPath(
            ScreenshotCatalog catalog,
            ScreenshotCatalogCategory category,
            bool finalAsset,
            string remoteFileName)
        {
            string folder = ScreenshotCatalogUtility.GetCatalogFolder(catalog, category) +
                            "/" + (finalAsset ? "Final" : "Source");
            string baseName = ScreenshotCatalogUtility.SanitizeName(Path.GetFileNameWithoutExtension(remoteFileName));
            string candidate = folder + "/" + baseName + ".png";
            int duplicate = 1;
            while (File.Exists(ToAbsoluteAssetPath(candidate)) || AssetDatabase.LoadMainAssetAtPath(candidate) != null)
            {
                candidate = string.Format("{0}/{1}-drive-{2:000}.png", folder, baseName, duplicate++);
            }
            return candidate.Replace('\\', '/');
        }

        private static List<UploadJob> BuildUploadJobs(ScreenshotCatalog catalog)
        {
            List<UploadJob> jobs = new List<UploadJob>();
            foreach (ScreenshotCatalogCategory category in catalog.categories)
            {
                foreach (ScreenshotCatalogRequirement requirement in category.requirements)
                {
                    foreach (ScreenshotCatalogSlot slot in requirement.slots)
                    {
                        AddJobs(catalog, jobs, category, requirement, slot, slot.sourceVersions, false);
                        AddJobs(catalog, jobs, category, requirement, slot, slot.finalVersions, true);
                    }
                }
            }
            return jobs;
        }

        private static void AddJobs(
            ScreenshotCatalog catalog,
            List<UploadJob> jobs,
            ScreenshotCatalogCategory category,
            ScreenshotCatalogRequirement requirement,
            ScreenshotCatalogSlot slot,
            List<Texture2D> versions,
            bool finalAsset)
        {
            foreach (Texture2D texture in versions)
            {
                if (texture == null || !ScreenshotCatalogUtility.IsVersionTracked(slot, finalAsset, texture))
                {
                    continue;
                }
                string assetPath = AssetDatabase.GetAssetPath(texture);
                string assetGuid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
                string profileGuid = GetProfileGuid(catalog.googleDriveProfile);
                if (string.IsNullOrEmpty(assetGuid) || catalog.googleDriveBindings.Any(binding =>
                        binding.profileGuid == profileGuid &&
                        binding.slotDefinitionId == slot.definitionId &&
                        binding.finalAsset == finalAsset &&
                        binding.localAssetGuid == assetGuid &&
                        !string.IsNullOrEmpty(binding.driveFileId)))
                {
                    continue;
                }

                jobs.Add(new UploadJob
                {
                    categoryId = category.definitionId,
                    categoryName = category.name,
                    requirementId = requirement.definitionId,
                    slotId = slot.definitionId,
                    finalAsset = finalAsset,
                    assetGuid = assetGuid,
                    assetPath = assetPath,
                    fileName = Path.GetFileName(assetPath)
                });
            }
        }

        private static int CountTrackedVersions(ScreenshotCatalog catalog)
        {
            return catalog.categories
                .SelectMany(category => category.requirements)
                .SelectMany(requirement => requirement.slots)
                .Sum(slot =>
                    slot.sourceVersions.Count(texture => ScreenshotCatalogUtility.IsVersionTracked(slot, false, texture)) +
                    slot.finalVersions.Count(texture => ScreenshotCatalogUtility.IsVersionTracked(slot, true, texture)));
        }

        private static int CountSyncedVersions(ScreenshotCatalog catalog)
        {
            int count = 0;
            foreach (ScreenshotCatalogRequirement requirement in catalog.categories.SelectMany(category => category.requirements))
            {
                foreach (ScreenshotCatalogSlot slot in requirement.slots)
                {
                    count += slot.sourceVersions.Count(texture =>
                        ScreenshotCatalogUtility.IsVersionTracked(slot, false, texture) &&
                        IsVersionSynced(catalog, slot, false, texture));
                    count += slot.finalVersions.Count(texture =>
                        ScreenshotCatalogUtility.IsVersionTracked(slot, true, texture) &&
                        IsVersionSynced(catalog, slot, true, texture));
                }
            }
            return count;
        }

        private static async Task<string> GetAccessTokenAsync(ScreenshotGoogleDriveProfile profile)
        {
            StoredToken token = LoadToken(profile);
            if (token == null)
            {
                throw new InvalidOperationException("Connect the Google Drive profile before pushing screenshots.");
            }
            if (!string.IsNullOrWhiteSpace(token.access_token) && token.expires_utc_ticks > DateTime.UtcNow.AddMinutes(1).Ticks)
            {
                return token.access_token;
            }
            if (string.IsNullOrWhiteSpace(token.refresh_token))
            {
                throw new InvalidOperationException("The Google authorization has no refresh token. Disconnect and connect again.");
            }

            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "client_id", profile.clientId },
                { "refresh_token", token.refresh_token },
                { "grant_type", "refresh_token" }
            };
            if (!string.IsNullOrWhiteSpace(profile.clientSecret))
            {
                values["client_secret"] = profile.clientSecret;
            }
            TokenResponse response = await PostTokenAsync(values);
            token.access_token = response.access_token;
            token.expires_utc_ticks = DateTime.UtcNow.AddSeconds(Math.Max(60, response.expires_in)).Ticks;
            SaveToken(profile, token);
            return token.access_token;
        }

        private static async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> values)
        {
            using (FormUrlEncodedContent content = new FormUrlEncodedContent(values))
            using (HttpResponseMessage response = await HttpClient.PostAsync(TokenEndpoint, content))
            {
                string json = await response.Content.ReadAsStringAsync();
                TokenResponse token = JsonUtility.FromJson<TokenResponse>(json);
                if (!response.IsSuccessStatusCode || token == null || string.IsNullOrWhiteSpace(token.access_token))
                {
                    string detail = token != null && !string.IsNullOrWhiteSpace(token.error_description)
                        ? token.error_description
                        : json;
                    throw new InvalidOperationException("Google token request failed: " + detail);
                }
                return token;
            }
        }

        private static async Task<string> EnsureFolderAsync(string accessToken, string parentId, string name, string key)
        {
            string query = "'" + EscapeDriveQuery(parentId) + "' in parents and " +
                           "mimeType = 'application/vnd.google-apps.folder' and trashed = false and " +
                           "appProperties has { key='screenshotterKey' and value='" + EscapeDriveQuery(key) + "' }";
            string url = DriveFilesEndpoint + "?q=" + Encode(query) +
                         "&spaces=drive&pageSize=10&fields=files(id,name,webViewLink)" +
                         "&supportsAllDrives=true&includeItemsFromAllDrives=true";
            using (HttpRequestMessage request = AuthorizedRequest(HttpMethod.Get, url, accessToken))
            using (HttpResponseMessage response = await HttpClient.SendAsync(request))
            {
                string json = await response.Content.ReadAsStringAsync();
                ThrowDriveError(response, json);
                DriveFileList list = JsonUtility.FromJson<DriveFileList>(json);
                if (list != null && list.files != null && list.files.Length > 0)
                {
                    return list.files[0].id;
                }
            }

            string metadata = "{\"name\":\"" + EscapeJson(name) + "\"," +
                              "\"mimeType\":\"application/vnd.google-apps.folder\"," +
                              "\"parents\":[\"" + EscapeJson(parentId) + "\"]," +
                              "\"appProperties\":{\"screenshotterKey\":\"" + EscapeJson(key) + "\"}}";
            string createUrl = DriveFilesEndpoint + "?fields=id,name,webViewLink&supportsAllDrives=true";
            using (HttpRequestMessage request = AuthorizedRequest(HttpMethod.Post, createUrl, accessToken))
            {
                request.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await HttpClient.SendAsync(request))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    ThrowDriveError(response, json);
                    DriveFile folder = JsonUtility.FromJson<DriveFile>(json);
                    if (folder == null || string.IsNullOrWhiteSpace(folder.id))
                    {
                        throw new InvalidOperationException("Google Drive created a folder but returned no file ID.");
                    }
                    return folder.id;
                }
            }
        }

        private static async Task<List<DriveFile>> ListPngFilesAsync(string accessToken, string parentId)
        {
            List<DriveFile> files = new List<DriveFile>();
            string pageToken = null;
            do
            {
                string query = "'" + EscapeDriveQuery(parentId) + "' in parents and trashed = false";
                string url = DriveFilesEndpoint + "?q=" + Encode(query) +
                             "&spaces=drive&pageSize=1000" +
                             "&fields=nextPageToken,files(id,name,mimeType,md5Checksum,appProperties)" +
                             "&supportsAllDrives=true&includeItemsFromAllDrives=true";
                if (!string.IsNullOrEmpty(pageToken))
                {
                    url += "&pageToken=" + Encode(pageToken);
                }

                using (HttpRequestMessage request = AuthorizedRequest(HttpMethod.Get, url, accessToken))
                using (HttpResponseMessage response = await HttpClient.SendAsync(request))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    ThrowDriveError(response, json);
                    DriveFileList page = JsonUtility.FromJson<DriveFileList>(json);
                    if (page != null && page.files != null)
                    {
                        files.AddRange(page.files.Where(file =>
                            file != null &&
                            string.Equals(Path.GetExtension(file.name), ".png", StringComparison.OrdinalIgnoreCase)));
                    }
                    pageToken = page == null ? null : page.nextPageToken;
                }
            }
            while (!string.IsNullOrEmpty(pageToken));
            return files;
        }

        private static async Task<byte[]> DownloadFileAsync(string accessToken, string fileId)
        {
            string url = DriveFilesEndpoint + "/" + Encode(fileId) + "?alt=media&supportsAllDrives=true";
            using (HttpRequestMessage request = AuthorizedRequest(HttpMethod.Get, url, accessToken))
            using (HttpResponseMessage response = await HttpClient.SendAsync(request))
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "Google Drive download failed (" + (int)response.StatusCode + "): " +
                        Encoding.UTF8.GetString(bytes));
                }
                return bytes;
            }
        }

        private static async Task ApplyFileMetadataAsync(
            string accessToken,
            string fileId,
            string catalogGuid,
            string categoryId,
            string requirementId,
            string slotId,
            bool finalAsset,
            string assetGuid)
        {
            string metadata = "{\"appProperties\":{" +
                              "\"screenshotterCatalog\":\"" + EscapeJson(catalogGuid) + "\"," +
                              "\"screenshotterCategory\":\"" + EscapeJson(categoryId) + "\"," +
                              "\"screenshotterRequirement\":\"" + EscapeJson(requirementId) + "\"," +
                              "\"screenshotterSlot\":\"" + EscapeJson(slotId) + "\"," +
                              "\"screenshotterKind\":\"" + (finalAsset ? "final" : "source") + "\"," +
                              "\"screenshotterAssetGuid\":\"" + EscapeJson(assetGuid) + "\"}}";
            string url = DriveFilesEndpoint + "/" + Encode(fileId) +
                         "?fields=id,appProperties&supportsAllDrives=true";
            using (HttpRequestMessage request = AuthorizedRequest(new HttpMethod("PATCH"), url, accessToken))
            {
                request.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await HttpClient.SendAsync(request))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    ThrowDriveError(response, json);
                }
            }
        }

        private static async Task<DriveFile> UploadFileAsync(
            string accessToken,
            string parentId,
            string catalogGuid,
            UploadJob job)
        {
            string absolutePath = ToAbsoluteAssetPath(job.assetPath);
            byte[] fileBytes = File.ReadAllBytes(absolutePath);
            string metadata = "{\"name\":\"" + EscapeJson(job.fileName) + "\"," +
                              "\"parents\":[\"" + EscapeJson(parentId) + "\"]," +
                              "\"appProperties\":{" +
                              "\"screenshotterCatalog\":\"" + EscapeJson(catalogGuid) + "\"," +
                              "\"screenshotterCategory\":\"" + EscapeJson(job.categoryId) + "\"," +
                              "\"screenshotterRequirement\":\"" + EscapeJson(job.requirementId) + "\"," +
                              "\"screenshotterSlot\":\"" + EscapeJson(job.slotId) + "\"," +
                              "\"screenshotterKind\":\"" + (job.finalAsset ? "final" : "source") + "\"," +
                              "\"screenshotterAssetGuid\":\"" + EscapeJson(job.assetGuid) + "\"}}";
            string boundary = "Screenshotter_" + Guid.NewGuid().ToString("N");
            byte[] prefix = Encoding.UTF8.GetBytes(
                "--" + boundary + "\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n" + metadata +
                "\r\n--" + boundary + "\r\nContent-Type: image/png\r\n\r\n");
            byte[] suffix = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            byte[] body = new byte[prefix.Length + fileBytes.Length + suffix.Length];
            Buffer.BlockCopy(prefix, 0, body, 0, prefix.Length);
            Buffer.BlockCopy(fileBytes, 0, body, prefix.Length, fileBytes.Length);
            Buffer.BlockCopy(suffix, 0, body, prefix.Length + fileBytes.Length, suffix.Length);

            string url = DriveUploadEndpoint +
                         "?uploadType=multipart&fields=id,name,webViewLink,md5Checksum&supportsAllDrives=true";
            using (HttpRequestMessage request = AuthorizedRequest(HttpMethod.Post, url, accessToken))
            {
                ByteArrayContent content = new ByteArrayContent(body);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/related; boundary=" + boundary);
                request.Content = content;
                using (HttpResponseMessage response = await HttpClient.SendAsync(request))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    ThrowDriveError(response, json);
                    DriveFile file = JsonUtility.FromJson<DriveFile>(json);
                    if (file == null || string.IsNullOrWhiteSpace(file.id))
                    {
                        throw new InvalidOperationException("Google Drive uploaded the PNG but returned no file ID.");
                    }
                    return file;
                }
            }
        }

        private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string url, string accessToken)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }

        private static void ThrowDriveError(HttpResponseMessage response, string body)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "Google Drive request failed (" + (int)response.StatusCode + "): " + body);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }

        private static int ReserveLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void WriteBrowserResponse(HttpListenerContext context, bool success, string error)
        {
            string message = success
                ? "Google Drive connected. You can close this tab and return to Unity."
                : "Google Drive authorization failed: " + WebUtility.HtmlEncode(error);
            byte[] bytes = Encoding.UTF8.GetBytes(
                "<!doctype html><html><body style=\"font-family:sans-serif;padding:32px\"><h2>Screenshotter</h2><p>" +
                message + "</p></body></html>");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        private static string CreateRandomUrlToken(int byteCount)
        {
            byte[] bytes = new byte[byteCount];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return ToBase64Url(bytes);
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string GetDestinationFolderId(ScreenshotGoogleDriveProfile profile)
        {
            return string.IsNullOrWhiteSpace(profile.destinationFolderId) ? "root" : profile.destinationFolderId.Trim();
        }

        private static string GetCatalogGuid(ScreenshotCatalog catalog)
        {
            string path = AssetDatabase.GetAssetPath(catalog);
            string guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
            return string.IsNullOrEmpty(guid) ? catalog.GetInstanceID().ToString() : guid;
        }

        private static string GetProfileGuid(ScreenshotGoogleDriveProfile profile)
        {
            string path = profile != null ? AssetDatabase.GetAssetPath(profile) : null;
            string guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
            return string.IsNullOrEmpty(guid) ? profile != null ? profile.GetInstanceID().ToString() : string.Empty : guid;
        }

        private static string GetTokenPath(ScreenshotGoogleDriveProfile profile)
        {
            string profilePath = profile != null ? AssetDatabase.GetAssetPath(profile) : null;
            string key = string.IsNullOrEmpty(profilePath)
                ? profile != null ? profile.GetInstanceID().ToString() : "missing"
                : AssetDatabase.AssetPathToGUID(profilePath);
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "Screenshotter", "GoogleDrive", key + ".json");
        }

        private static StoredToken LoadToken(ScreenshotGoogleDriveProfile profile)
        {
            if (profile == null)
            {
                return null;
            }
            string path = GetTokenPath(profile);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<StoredToken>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SaveToken(ScreenshotGoogleDriveProfile profile, StoredToken token)
        {
            string path = GetTokenPath(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(token));
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string Encode(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static string EscapeDriveQuery(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            StringBuilder builder = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(character); break;
                }
            }
            return builder.ToString();
        }
    }
}
