using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CheckAllMic : MonoBehaviour
{
    public TextMeshProUGUI displayText;
    void Start()
    {
        PopulateDeviceList();
    }

    // Populate text and optional button list
    public void PopulateDeviceList()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO"))
        {
            UnityEngine.Android.Permission.RequestUserPermission("android.permission.RECORD_AUDIO");
        }
#endif
        string[] devices = Microphone.devices;

        if (displayText != null)
        {
            if (devices == null || devices.Length == 0)
            {
                displayText.text = "No microphone devices found.\nMake sure device connected and permission granted.";
            }
            else
            {
                displayText.text = "Available microphone devices:\n";
                for (int i = 0; i < devices.Length; i++)
                {
                    displayText.text += $"{i}: {devices[i]}\n";
                }
                displayText.text += "\nTap a device button (if created) or call SelectDevice(index).";
            }
        }
    }
}
