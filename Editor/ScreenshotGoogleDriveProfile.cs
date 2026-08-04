using UnityEngine;

namespace SkatanicStudios
{
    [CreateAssetMenu(fileName = "Screenshot Google Drive Profile", menuName = "Screenshotter/Google Drive Sync Profile")]
    public sealed class ScreenshotGoogleDriveProfile : ScriptableObject
    {
        [SerializeField] internal string applicationName = "Screenshotter";
        [SerializeField] internal string clientId;
        [SerializeField] internal string clientSecret;
        [SerializeField] internal string destinationFolderId = "root";
    }
}
