using System;
using UnityEngine;
using UnityEngine.Android;

namespace Unity.XR.XREAL.Samples
{
    public class PermissionManager : MonoBehaviour
    {
        private const string PERMISSION_GRANTED_KEY = "PermissionsGranted";

        // Required permissions
        private string[] requiredPermissions = new string[]
        {
            Permission.Camera,
            Permission.Microphone,
            Permission.ExternalStorageWrite,
            Permission.ExternalStorageRead
        };

        private Action onPermissionsGranted;
        private bool isCheckingPermissions = false;

        void Start()
        {
            // Check if permissions were previously granted
            if (ArePermissionsPreviouslyGranted())
            {
                Debug.Log("Permissions were previously granted, skipping request.");
                return;
            }

            // Check and request if needed
            CheckAndRequestPermissions();
        }

        /// <summary>
        /// Check if all permissions were granted in a previous session
        /// </summary>
        private bool ArePermissionsPreviouslyGranted()
        {
            // Check PlayerPrefs flag
            if (PlayerPrefs.GetInt(PERMISSION_GRANTED_KEY, 0) == 0)
            {
                return false;
            }

            // Verify all permissions are still granted
            foreach (string permission in requiredPermissions)
            {
                if (!Permission.HasUserAuthorizedPermission(permission))
                {
                    // Permission was revoked, clear the flag
                    PlayerPrefs.SetInt(PERMISSION_GRANTED_KEY, 0);
                    PlayerPrefs.Save();
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check and request permissions if not granted
        /// </summary>
        public void CheckAndRequestPermissions(Action callback = null)
        {
            if (isCheckingPermissions)
            {
                Debug.LogWarning("Already checking permissions...");
                return;
            }

            onPermissionsGranted = callback;

            // Check if all permissions are already granted
            if (AreAllPermissionsGranted())
            {
                OnAllPermissionsGranted();
                return;
            }

            // Request missing permissions
            isCheckingPermissions = true;

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionDenied += PermissionCallbacks_PermissionDenied;
            callbacks.PermissionGranted += PermissionCallbacks_PermissionGranted;
            callbacks.PermissionDeniedAndDontAskAgain += PermissionCallbacks_PermissionDeniedAndDontAskAgain;

            Permission.RequestUserPermissions(requiredPermissions, callbacks);
        }

        /// <summary>
        /// Check if all required permissions are granted
        /// </summary>
        private bool AreAllPermissionsGranted()
        {
            foreach (string permission in requiredPermissions)
            {
                if (!Permission.HasUserAuthorizedPermission(permission))
                {
                    return false;
                }
            }
            return true;
        }

        private void PermissionCallbacks_PermissionGranted(string permissionName)
        {
            Debug.Log($"Permission Granted: {permissionName}");

            // Check if all permissions are now granted
            if (AreAllPermissionsGranted())
            {
                OnAllPermissionsGranted();
            }
        }

        private void PermissionCallbacks_PermissionDenied(string permissionName)
        {
            Debug.LogWarning($"Permission Denied: {permissionName}");
            isCheckingPermissions = false;

            // You can show a message to user here
            ShowPermissionDeniedMessage(permissionName);
        }

        private void PermissionCallbacks_PermissionDeniedAndDontAskAgain(string permissionName)
        {
            Debug.LogError($"Permission Denied And Don't Ask Again: {permissionName}");
            isCheckingPermissions = false;

            // Guide user to settings
            ShowSettingsPrompt(permissionName);
        }

        private void OnAllPermissionsGranted()
        {
            Debug.Log("All permissions granted!");

            // Save the flag so we don't ask again
            PlayerPrefs.SetInt(PERMISSION_GRANTED_KEY, 1);
            PlayerPrefs.Save();

            isCheckingPermissions = false;
            onPermissionsGranted?.Invoke();
        }

        private void ShowPermissionDeniedMessage(string permission)
        {
            string message = $"Permission '{permission}' is required for recording. Please grant permission to use this feature.";
            Debug.LogWarning(message);

            // TODO: Show UI dialog to user
        }

        private void ShowSettingsPrompt(string permission)
        {
            string message = $"Permission '{permission}' was permanently denied. Please enable it in Settings.";
            Debug.LogError(message);

            // TODO: Show UI with button to open app settings
            // You can use: Application.OpenURL("app-settings:");
        }

        /// <summary>
        /// Force check permissions (useful for manual refresh)
        /// </summary>
        public void ForceCheckPermissions()
        {
            PlayerPrefs.DeleteKey(PERMISSION_GRANTED_KEY);
            PlayerPrefs.Save();
            CheckAndRequestPermissions();
        }

        /// <summary>
        /// Check if a specific permission is granted
        /// </summary>
        public bool HasPermission(string permission)
        {
            return Permission.HasUserAuthorizedPermission(permission);
        }

        /// <summary>
        /// Check if camera permission is granted
        /// </summary>
        public bool HasCameraPermission()
        {
            return Permission.HasUserAuthorizedPermission(Permission.Camera);
        }

        /// <summary>
        /// Check if microphone permission is granted
        /// </summary>
        public bool HasMicrophonePermission()
        {
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
        }
    }
}