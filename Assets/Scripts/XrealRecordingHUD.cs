using TMPro;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using UnityEngine.UI;
using System;

public class XrealRecordingHUD : MonoBehaviour
{
    public CaptureExample capture;   // Drag CaptureExample object here
    public GameObject RecordingUI;

    [Header("UI")]
    public TextMeshProUGUI duration;
    public TextMeshProUGUI timestamp;  // Add this UI element for real-time timestamp
    public Image stateIcon;
    public Sprite iconRecording;
    public Sprite iconPaused;

    private float elapsedTime = 0f;
    private bool uiPaused = false;

    private void Awake()
    {
        stateIcon.enabled = false;
        duration.text = "";
        if (timestamp != null)
            timestamp.text = "";

        RecordingUI.SetActive(false);
    }

    void Update()
    {
        if (capture == null && capture.m_VideoCapture == null) return;

        if (capture.m_VideoCapture != null && capture.m_VideoCapture.IsRecording)
        {
            if (timestamp != null)
            {
                timestamp.text = DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt");
            }

            if (!uiPaused)
            {
                stateIcon.sprite = iconRecording;
                stateIcon.enabled = true;
                elapsedTime += Time.deltaTime;
                duration.text = FormatTime(elapsedTime);
            }
            RecordingUI.SetActive(true);
        }
        else
        {
            stateIcon.enabled = false;
            elapsedTime = 0;
            duration.text = "";
            timestamp.text = "";
            RecordingUI.SetActive(false);
        }
    }

    public void PauseUI()
    {
        uiPaused = true;
        stateIcon.sprite = iconPaused;
    }

    public void ResumeUI()
    {
        uiPaused = false;
        stateIcon.sprite = iconRecording;
    }

    string FormatTime(float sec)
    {
        int h = (int)(sec / 3600);
        int m = (int)((sec % 3600) / 60);
        int s = (int)(sec % 60);
        return $"{h:00}:{m:00}:{s:00}";
    }
}