using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using UnityEngine.Android;

public class VoiceCommandController : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI debugText;

    [Header("Capture Example")]
    public CaptureExample captureExample;
    public CommandData[] CommandData;

    private bool isListening = false;
    private bool shouldKeepListening = false;

    private Queue<Action> mainThreadActions = new Queue<Action>();

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject currentActivity;
#endif

    void Start()
    {
        RequestMicrophonePermission();
    }

    void Update()
    {
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
                mainThreadActions.Dequeue()?.Invoke();
        }
    }

    #region Permission

    void RequestMicrophonePermission()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
            StartCoroutine(WaitForPermission());
        }
        else
        {
            InitializeRecognizer();
        }
    }

    IEnumerator WaitForPermission()
    {
        while (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            yield return null;

        yield return new WaitForSeconds(0.1f);
        InitializeRecognizer();
    }

    #endregion

    #region Init

    public void InitializeRecognizer()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

        currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            try
            {
                AndroidJavaClass recognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");

                if (!recognizerClass.CallStatic<bool>("isRecognitionAvailable", currentActivity))
                {
                    UpdateDebug("Speech recognition not available!");
                    return;
                }

                speechRecognizer = recognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", currentActivity);

                UpdateDebug("Recognizer initialized ✓");
            }
            catch (Exception e)
            {
                UpdateDebug("Init error: " + e.Message);
            }
        }));
#else
        UpdateDebug("Android only");
#endif
    }

    #endregion

    #region Listening

    public void StartListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer == null || isListening) return;

        shouldKeepListening = true;

        currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            try
            {
                AndroidJavaClass recognizerIntent = new AndroidJavaClass("android.speech.RecognizerIntent");
                AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent",
                    recognizerIntent.GetStatic<string>("ACTION_RECOGNIZE_SPEECH"));

                // Core speech settings
                intent.Call<AndroidJavaObject>("putExtra",
                    recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                    recognizerIntent.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));

                intent.Call<AndroidJavaObject>("putExtra",
                    recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE"), "en-US");

                intent.Call<AndroidJavaObject>("putExtra",
                    recognizerIntent.GetStatic<string>("EXTRA_PARTIAL_RESULTS"), true);

                // 🔇 NO BEEP TRICK 100% WORKING ON XREAL / PIXEL / SAMSUNG
                intent.Call<AndroidJavaObject>("putExtra",
                    recognizerIntent.GetStatic<string>("EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS"), 0);
                intent.Call<AndroidJavaObject>("putExtra",
                    recognizerIntent.GetStatic<string>("EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS"), 0);
                intent.Call<AndroidJavaObject>("putExtra",
                    recognizerIntent.GetStatic<string>("EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS"), 0);

                // Required for silence → no beep
                intent.Call<AndroidJavaObject>("putExtra",
                    recognizerIntent.GetStatic<string>("EXTRA_CALLING_PACKAGE"),
                    currentActivity.Call<string>("getPackageName"));

                // Listener
                RecognitionListenerProxy listener = new RecognitionListenerProxy(this);
                speechRecognizer.Call("setRecognitionListener", listener);

                speechRecognizer.Call("startListening", intent);

                isListening = true;
                UpdateDebug("Listening...");
            }
            catch (Exception e)
            {
                UpdateDebug("Start error: " + e.Message);
            }
        }));
#else
        UpdateDebug("Android only");
#endif
    }

    public void StopListening()
    {
        shouldKeepListening = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer == null || !isListening) return;

        currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            try
            {
                speechRecognizer.Call("stopListening");
                isListening = false;
                UpdateDebug("Stopped.");
            }
            catch (Exception e)
            {
                UpdateDebug("Stop error: " + e.Message);
            }
        }));
#endif
    }

    #endregion

    #region Callbacks

    public void OnRecognitionResults(string text)
    {
        EnqueueOnMainThread(() =>
        {
            UpdateDebug("Recognized: " + text);
            HandleCommands(text);
        });

        isListening = false;

        if (shouldKeepListening)
            StartCoroutine(RestartListening());
    }

    public void OnRecognitionPartialResults(string text)
    {
        EnqueueOnMainThread(() =>
        {
            UpdateDebug("Partial: " + text);
        });
    }

    public void OnRecognitionError(string errorCode)
    {
        EnqueueOnMainThread(() =>
        {
            UpdateDebug("Error: " + errorCode);
        });

        isListening = false;

        // Restart if still wanting continuous listening
        if (shouldKeepListening)
            StartCoroutine(RestartListening());
    }

    IEnumerator RestartListening()
    {
        yield return new WaitForSeconds(0.5f);
        if (shouldKeepListening)
            StartListening();
    }

    #endregion

    #region Commands

    private void HandleCommands(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        text = text.ToLower();

        if (debugText) debugText.text = text;

        // Check all CommandData entries
        foreach (var data in CommandData)
        {
            foreach (var cmd in data.commands)
            {
                if (string.IsNullOrEmpty(cmd)) continue;

                string lowerCmd = cmd.ToLower();

                // Match spoken text with command
                if (text.Contains(lowerCmd))
                {
                    Debug.Log($"Command matched: {data.CommandGroupName} → {cmd}");
                    data.Event?.Invoke();
                    return; // stop after first match
                }
            }
        }
    }

    #endregion

    #region Utils

    void UpdateDebug(string msg)
    {
        Debug.Log(msg);
        if (debugText) debugText.text = msg;
    }

    void EnqueueOnMainThread(Action action)
    {
        lock (mainThreadActions)
            mainThreadActions.Enqueue(action);
    }

    #endregion

#if UNITY_ANDROID && !UNITY_EDITOR
    private class RecognitionListenerProxy : AndroidJavaProxy
    {
        private VoiceCommandController controller;

        public RecognitionListenerProxy(VoiceCommandController c)
            : base("android.speech.RecognitionListener")
        {
            controller = c;
        }

        public void onReadyForSpeech(AndroidJavaObject data) { }
        public void onBeginningOfSpeech() { }
        public void onRmsChanged(float v) { }
        public void onBufferReceived(byte[] buffer) { }
        public void onEndOfSpeech() { }

        public void onError(int e)
        {
            controller.OnRecognitionError(e.ToString());
        }

        public void onResults(AndroidJavaObject results)
        {
            if (results == null) return;

            AndroidJavaObject list = results.Call<AndroidJavaObject>("getStringArrayList", "results_recognition");
            if (list != null && list.Call<int>("size") > 0)
            {
                string text = list.Call<string>("get", 0);
                controller.OnRecognitionResults(text);
            }
        }

        public void onPartialResults(AndroidJavaObject results)
        {
            if (results == null) return;

            AndroidJavaObject list = results.Call<AndroidJavaObject>("getStringArrayList", "results_recognition");
            if (list != null && list.Call<int>("size") > 0)
            {
                string text = list.Call<string>("get", 0);
                controller.OnRecognitionPartialResults(text);
            }
        }

        public void onEvent(int type, AndroidJavaObject data) { }
    }
#endif
}
