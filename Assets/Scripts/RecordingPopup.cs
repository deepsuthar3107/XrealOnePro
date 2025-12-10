using System.Collections;
using TMPro;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using UnityEngine.UI;

public class RecordingPopup : MonoBehaviour
{
    public CaptureExample captureExample;

    [Header("Popup Root")]
    public GameObject _RecordingPopup;
    public GameObject _RecordingUI;

    [Header("Recording UI")]
    public TextMeshProUGUI RecordingText;
    public Image recImg;

    [Header("Saving UI")]
    public TextMeshProUGUI SavingText;
    public Image saveImg;

    private CanvasGroup canvasGroup;
    private Coroutine savingRoutine;
    private Coroutine startRoutine;

    private void Start()
    {
        if (_RecordingPopup != null && _RecordingPopup.TryGetComponent(out CanvasGroup cg))
        {
            canvasGroup = cg;
            canvasGroup.alpha = 0f;
            _RecordingPopup.SetActive(false);
        }

        recImg.gameObject.SetActive(false);
        saveImg.gameObject.SetActive(false);
        RecordingText.text = "";
        SavingText.text = "";
    }

    [ContextMenu("StartRecording")]
    public void startRecording()
    {
        if (_RecordingPopup == null) return;

        // Prevent overlapping coroutines
        if (startRoutine != null)
            StopCoroutine(startRoutine);

        // UI State
        recImg.gameObject.SetActive(true);
        saveImg.gameObject.SetActive(false);
        RecordingText.text = $"Recording starts in... ";
        SavingText.text = "";

        _RecordingPopup.SetActive(true);

        startRoutine = StartCoroutine(StartRecordingPopup());
    }

    IEnumerator StartRecordingPopup()
    {
        // ---- Fade In ----
        float a = 0f;
        float target = 0.9f;

        while (a < target)
        {
            a += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Clamp01(a / target);

            yield return null;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = target;

        // ---- Countdown ----
        for (int t = 3; t > 0; t--)
        {
            RecordingText.text = $"Recording starts in... {t}";
            yield return new WaitForSeconds(1f);
        }

        // Hide popup + start recording
        _RecordingPopup.SetActive(false);
        RecordingText.text = "";
        _RecordingUI.SetActive(true);
        captureExample.RecordVideo();
    }

    [ContextMenu("stopRecording")]
    public void stopRecording()
    {
        // Prevent duplicate save coroutines
        if (savingRoutine != null)
            StopCoroutine(savingRoutine);

        recImg.gameObject.SetActive(false);
        saveImg.gameObject.SetActive(true);
        saveImg.fillAmount = 0;
        SavingText.text = "";

        _RecordingPopup.SetActive(true);
        if (canvasGroup != null)
            canvasGroup.alpha = 0.9f;


        _RecordingUI.SetActive(false);
        savingRoutine = StartCoroutine(StopRecordingPopup());
    }

    IEnumerator StopRecordingPopup()
    {
        float time = 0f;
        saveImg.fillAmount = 0f;

        while (time < 2f)
        {
            SavingText.text = "Please wait, saving your recording...";
            time += Time.deltaTime;

            // Looping fill animation
            saveImg.fillAmount += Time.deltaTime * 1.5f;
            if (saveImg.fillAmount >= 1f)
                saveImg.fillAmount = 0f;

            yield return null;
        }

        SavingText.text = "Recording saved successfully!";
        saveImg.fillAmount = 1f;

        yield return new WaitForSeconds(1f);

        captureExample.StopVideoCapture();

        // Clean up UI
        saveImg.fillAmount = 0f;
        saveImg.gameObject.SetActive(false);
        SavingText.text = "";
        _RecordingPopup.SetActive(false);
    }
}
