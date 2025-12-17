using System;
using System.Collections;
using TMPro;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using UnityEngine.UI;

public class RecordingManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private CaptureExample captureExample;

    [Header("UI Panels")]
    [SerializeField] private GameObject recordingPopup;
    [SerializeField] private GameObject recordingUI;

    [Header("Recording UI Elements")]
    [SerializeField] private TextMeshProUGUI recordingText;
    [SerializeField] private Image recordingImage;

    [Header("Saving UI Elements")]
    [SerializeField] private TextMeshProUGUI savingText;
    [SerializeField] private Image savingImage;

    [Header("Video State UI")]
    [SerializeField] private TextMeshProUGUI durationText;
    [SerializeField] private TextMeshProUGUI timestampText;
    [SerializeField] private Image stateIcon;
    [SerializeField] private Sprite iconRecording;
    [SerializeField] private Sprite iconPaused;

    [Header("Animation Settings")]
    [SerializeField] private float fadeInDuration = 0.9f;
    [SerializeField] private float countdownDuration = 3f;
    [SerializeField] private float savingDuration = 2f;
    [SerializeField] private float savingFillSpeed = 1.5f;

    // State
    private float elapsedTime;
    private bool isUIPaused;
    private bool isRecording;

    // Components
    private CanvasGroup canvasGroup;

    // Coroutines
    private Coroutine savingRoutine;
    private Coroutine startRoutine;

    // Constants
    private const float TARGET_ALPHA = 0.9f;

    #region Unity Lifecycle

    private void Start()
    {
        InitializeComponents();
        ResetUI();
    }

    private void Update()
    {
        if (!IsValidCaptureState()) return;

        UpdateRecordingState();
    }

    #endregion

    public void ProcessCommand(string command)
    {
        if (IsRecordingStartCommand(command))
        {
            StartRecording();
            return;
        }
        else if (IsRecordingStopCommand(command))
        {
            StopRecording();
            return;
        }
      /*  else if (IsRecordingPauseCommand(command))
        {
            PauseUI();
            return;
        }
        else if (IsRecordingResumeCommand(command))
        {
            ResumeUI();
            return;
        }*/
    }

    #region Initialization

    private void InitializeComponents()
    {
        if (recordingPopup != null && recordingPopup.TryGetComponent(out CanvasGroup cg))
        {
            canvasGroup = cg;
            canvasGroup.alpha = 0f;
            recordingPopup.SetActive(false);
        }
    }

    private void ResetUI()
    {
        SetUIElementActive(recordingImage, false);
        SetUIElementActive(savingImage, false);

        SetText(recordingText, string.Empty);
        SetText(savingText, string.Empty);
        SetText(durationText, string.Empty);
        SetText(timestampText, string.Empty);

        if (stateIcon != null)
            stateIcon.enabled = false;
    }

    #endregion

    #region Recording State Management

    private bool IsValidCaptureState()
    {
        return captureExample != null && captureExample.m_VideoCapture != null;
    }

    private void UpdateRecordingState()
    {
        if (captureExample.m_VideoCapture.IsRecording)
        {
            UpdateTimestamp();
            UpdateDurationDisplay();
        }
        else
        {
            ResetRecordingDisplay();
        }
    }

    private void UpdateTimestamp()
    {
        if (timestampText != null)
        {
            timestampText.text = DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt");
        }
    }

    private void UpdateDurationDisplay()
    {
        if (!isUIPaused)
        {
            SetStateIcon(iconRecording, true);
            elapsedTime += Time.deltaTime;
            SetText(durationText, FormatTime(elapsedTime));
        }
    }

    private void ResetRecordingDisplay()
    {
        if (stateIcon != null)
            stateIcon.enabled = false;

        elapsedTime = 0f;
        SetText(durationText, string.Empty);
        SetText(timestampText, string.Empty);
    }

    #endregion

    #region Public API

    [ContextMenu("Start Recording")]
    public void StartRecording()
    {
        if (recordingPopup == null || isRecording) return;

        StopCoroutineIfRunning(ref startRoutine);

        PrepareRecordingUI();
        ShowPopup();

        startRoutine = StartCoroutine(StartRecordingSequence());
        isRecording = true;
    }

    [ContextMenu("Stop Recording")]
    public void StopRecording()
    {
        if (!isRecording) return;

        StopCoroutineIfRunning(ref savingRoutine);

        PrepareSavingUI();
        ShowPopup();

        recordingUI.SetActive(false);
        savingRoutine = StartCoroutine(StopRecordingSequence());
    }

    public void PauseUI()
    {
        isUIPaused = true;
        SetStateIcon(iconPaused, true);
    }

    public void ResumeUI()
    {
        isUIPaused = false;
        SetStateIcon(iconRecording, true);
    }

    #endregion

    #region UI Preparation

    private void PrepareRecordingUI()
    {
        SetUIElementActive(recordingImage, true);
        SetUIElementActive(savingImage, false);
        SetText(recordingText, "Recording starts in... ");
        SetText(savingText, string.Empty);
    }

    private void PrepareSavingUI()
    {
        SetUIElementActive(recordingImage, false);
        SetUIElementActive(savingImage, true);
        SetImageFill(savingImage, 0f);
        SetText(savingText, string.Empty);
    }

    private void ShowPopup()
    {
        if (recordingPopup != null)
        {
            recordingPopup.SetActive(true);
            if (canvasGroup != null)
                canvasGroup.alpha = TARGET_ALPHA;
        }
    }

    #endregion

    #region Coroutines

    private IEnumerator StartRecordingSequence()
    {
        yield return FadeInPopup();
        yield return ShowCountdown();
        StartActualRecording();
    }

    private IEnumerator FadeInPopup()
    {
        if (canvasGroup == null) yield break;

        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }

        canvasGroup.alpha = TARGET_ALPHA;
    }

    private IEnumerator ShowCountdown()
    {
        int countdown = Mathf.FloorToInt(countdownDuration);

        for (int i = countdown; i > 0; i--)
        {
            SetText(recordingText, $"Recording starts in... {i}");
            yield return new WaitForSeconds(1f);
        }
    }

    private void StartActualRecording()
    {
        recordingPopup.SetActive(false);
        SetText(recordingText, string.Empty);
        recordingUI.SetActive(true);

        // Reset the elapsed time for new recording
        elapsedTime = 0f;

        captureExample.RecordVideo();
    }

    private IEnumerator StopRecordingSequence()
    {
        yield return AnimateSaving();
        yield return ShowSaveSuccess();
        FinalizeSaving();
    }

    private IEnumerator AnimateSaving()
    {
        float elapsed = 0f;
        SetImageFill(savingImage, 0f);

        while (elapsed < savingDuration)
        {
            SetText(savingText, "Please wait, saving your recording...");
            elapsed += Time.deltaTime;

            AnimateSavingFill(elapsed);
            yield return null;
        }
    }

    private void AnimateSavingFill(float elapsed)
    {
        if (savingImage == null) return;

        savingImage.fillAmount += Time.deltaTime * savingFillSpeed;
        if (savingImage.fillAmount >= 1f)
            savingImage.fillAmount = 0f;
    }

    private IEnumerator ShowSaveSuccess()
    {
        SetText(savingText, "Recording saved successfully!");
        SetImageFill(savingImage, 1f);
        yield return new WaitForSeconds(1f);
    }

    private void FinalizeSaving()
    {
        captureExample.StopVideoCapture();
        CleanupSavingUI();
    }

    private void CleanupSavingUI()
    {
        SetImageFill(savingImage, 0f);
        SetUIElementActive(savingImage, false);
        SetText(savingText, string.Empty);
        recordingPopup.SetActive(false);
        isRecording = false;
    }

    #endregion

    #region Helper Methods

    private void StopCoroutineIfRunning(ref Coroutine routine)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    private void SetUIElementActive(Image image, bool active)
    {
        if (image != null && image.gameObject != null)
            image.gameObject.SetActive(active);
    }

    private void SetText(TextMeshProUGUI textComponent, string text)
    {
        if (textComponent != null)
            textComponent.text = text;
    }

    private void SetImageFill(Image image, float fillAmount)
    {
        if (image != null)
            image.fillAmount = fillAmount;
    }

    private void SetStateIcon(Sprite sprite, bool enabled)
    {
        if (stateIcon != null)
        {
            stateIcon.sprite = sprite;
            stateIcon.enabled = enabled;
        }
    }

    private string FormatTime(float seconds)
    {
        int hours = (int)(seconds / 3600);
        int minutes = (int)((seconds % 3600) / 60);
        int secs = (int)(seconds % 60);
        return $"{hours:D2}:{minutes:D2}:{secs:D2}";
    }

    #endregion

    private bool IsRecordingStartCommand(string cmd) =>
       cmd == "start recording" || cmd == "begin recording" || cmd == "start inspection";

    private bool IsRecordingStopCommand(string cmd) =>
        cmd == "stop recording" || cmd == "complete inspection";

    private bool IsRecordingPauseCommand(string cmd) =>
        cmd == "pause recording";

    private bool IsRecordingResumeCommand(string cmd) =>
        cmd == "resume recording";
}