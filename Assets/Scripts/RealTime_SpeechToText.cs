using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Android;
using UnityEngine.Events;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

[System.Serializable]
public class SpeechEvent : UnityEvent<string> { }

[System.Serializable]
public class KeywordEvent : UnityEvent { }

[System.Serializable]
public class Keyword
{
    [Tooltip("Command name for display purposes")]
    public string commandName;

    [Tooltip("Keywords to detect (lowercase recommended)")]
    public string[] commands;

    [Tooltip("Event triggered when keyword is detected")]
    public KeywordEvent onDetected;

    [Tooltip("Exact match required or partial match allowed")]
    public bool exactMatch = true;
}

public class RealTime_SpeechToText : MonoBehaviour
{
    [Header("UI References")]
    public Button startButton;
    public Button stopButton;
    public Button clearButton;
    public TextMeshProUGUI displayText;
    public TextMeshProUGUI statusText;

    [Header("Settings")]
    public bool autoRestart = true;
    public float restartDelay = 0.5f;
    [Tooltip("Language: en-IN (Indian English) or en-US (US English)")]
    public string language = "en-IN";

    [Header("Keyword Detection")]
    [Tooltip("Add your keywords here")]
    public List<Keyword> keywords = new List<Keyword>();

    [Tooltip("Highlight detected keywords in UI")]
    public bool highlightDetectedKeywords = true;

    [Tooltip("Play sound when keyword is detected")]
    public AudioClip keywordDetectedSound;

    private AudioSource audioSource;

    [Header("Unity Events")]
    [Tooltip("Called when speech is recognized (Final Result)")]
    public SpeechEvent OnSpeechRecognized;

    [Tooltip("Called when partial speech is detected (Real-time)")]
    public SpeechEvent OnPartialSpeechDetected;

    [Tooltip("Called when ANY keyword is detected")]
    public SpeechEvent OnAnyKeywordDetected;

    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject recognitionListener;
    private AndroidJavaObject currentActivity;
    private bool isListening = false;
    private string fullText = "";
    private bool shouldKeepListening = false;

    // Thread-safe queue for keyword events
    private Queue<KeywordDetectionData> detectedKeywordsQueue = new Queue<KeywordDetectionData>();
    private object queueLock = new object();

    private class KeywordDetectionData
    {
        public string commandName;
        public string detectedCommand;
    }

    void Start()
    {
        // Ensure UnityMainThreadDispatcher exists
        if (UnityMainThreadDispatcher.Instance() == null)
        {
            GameObject dispatcher = new GameObject("UnityMainThreadDispatcher");
            dispatcher.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(dispatcher);
        }

        RequestMicrophonePermission();
        SetupUI();
        InitializeAndroidSpeechRecognizer();
        SetupAudio();

        // Initialize events if null
        if (OnSpeechRecognized == null)
            OnSpeechRecognized = new SpeechEvent();
        if (OnPartialSpeechDetected == null)
            OnPartialSpeechDetected = new SpeechEvent();
        if (OnAnyKeywordDetected == null)
            OnAnyKeywordDetected = new SpeechEvent();

        // Initialize keyword events
        foreach (var keyword in keywords)
        {
            if (keyword.onDetected == null)
                keyword.onDetected = new KeywordEvent();
        }
    }

    void SetupAudio()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && keywordDetectedSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    void Update()
    {
        // Process keyword events on main Unity thread
        lock (queueLock)
        {
            while (detectedKeywordsQueue.Count > 0)
            {
                KeywordDetectionData data = detectedKeywordsQueue.Dequeue();
                TriggerKeywordEvent(data);
            }
        }
    }

    void RequestMicrophonePermission()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
        {
            Permission.RequestUserPermission(Permission.ExternalStorageWrite);
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
        {
            Permission.RequestUserPermission(Permission.ExternalStorageRead);
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
        {
            Permission.RequestUserPermission(Permission.CoarseLocation);
        }
    }

    void SetupUI()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClick);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(OnStopButtonClick);
            stopButton.interactable = false;
        }

        if (clearButton != null)
        {
            clearButton.onClick.AddListener(OnClearButtonClick);
        }

        if (displayText != null)
        {
            displayText.text = "Press START to begin speaking...";
            displayText.fontSize = 24;
            displayText.alignment = TextAlignmentOptions.TopLeft;
            displayText.enableWordWrapping = true;
            displayText.overflowMode = TextOverflowModes.Overflow;

            var scrollRect = displayText.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.vertical = true;
            }
        }

        if (statusText != null)
        {
            statusText.text = "Ready";
            statusText.color = Color.green;
            statusText.fontSize = 20;
            statusText.alignment = TextAlignmentOptions.Center;
        }
    }

    void InitializeAndroidSpeechRecognizer()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                AndroidJavaClass speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
                speechRecognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", currentActivity);
                
                Debug.Log("SpeechRecognizer initialized successfully");
            }));
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to initialize SpeechRecognizer: " + e.Message);
            UpdateStatus("Initialization Error", Color.red);
        }
#else
        Debug.Log("Speech recognition only works on Android device");
#endif
    }

    public void OnStartButtonClick()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        shouldKeepListening = true;
        StartListening();
#else
        StartCoroutine(SimulateEditorRecognition());
#endif

        if (startButton != null) startButton.interactable = false;
        if (stopButton != null) stopButton.interactable = true;
        UpdateStatus("Listening... Speak now!", Color.red);
    }

    public void OnStopButtonClick()
    {
        shouldKeepListening = false;
        StopListening();

        if (startButton != null) startButton.interactable = true;
        if (stopButton != null) stopButton.interactable = false;
        UpdateStatus("Stopped", Color.yellow);
    }

    public void OnClearButtonClick()
    {
        fullText = "";
        if (displayText != null)
        {
            displayText.text = "Text cleared. Ready to record...";
        }
    }

    void StartListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer == null || isListening) return;

        try
        {
            currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                using (AndroidJavaClass recognizerIntent = new AndroidJavaClass("android.speech.RecognizerIntent"))
                using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", 
                    recognizerIntent.GetStatic<string>("ACTION_RECOGNIZE_SPEECH")))
                {
                    intent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                        recognizerIntent.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));

                    intent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE"), language);

                    intent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_PARTIAL_RESULTS"), true);

                    intent.Call<AndroidJavaObject>("putExtra",
                        recognizerIntent.GetStatic<string>("EXTRA_MAX_RESULTS"), 1);

                    AndroidJavaProxy listenerProxy = new RecognitionListenerProxy(this);
                    speechRecognizer.Call("setRecognitionListener", listenerProxy);
                    speechRecognizer.Call("startListening", intent);
                    
                    isListening = true;
                    Debug.Log("Speech recognition started");
                }
            }));
        }
        catch (System.Exception e)
        {
            Debug.LogError("StartListening error: " + e.Message);
            UpdateStatus("Error starting recognition", Color.red);
        }
#endif
    }

    void StopListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer == null || !isListening) return;

        try
        {
            currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                speechRecognizer.Call("stopListening");
                isListening = false;
                Debug.Log("Speech recognition stopped");
            }));
        }
        catch (System.Exception e)
        {
            Debug.LogError("StopListening error: " + e.Message);
        }
#endif
    }

    // KEYWORD DETECTION LOGIC - FIXED VERSION
    void CheckForKeywords(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Remove square brackets and clean text
        string cleanedText = text.Replace("[", "").Replace("]", "");
        string lowerText = cleanedText.ToLower().Trim();

        Debug.Log($"🎤 Original: '{text}' | Cleaned: '{lowerText}' | Total Keywords: {keywords.Count}");

        foreach (var keyword in keywords)
        {
            // Check if commands array exists and has elements
            if (keyword.commands == null || keyword.commands.Length == 0)
            {
                Debug.LogWarning($"⚠️ Keyword '{keyword.commandName}' has no commands defined!");
                continue;
            }

            // Check each command in the array
            foreach (string cmd in keyword.commands)
            {
                if (string.IsNullOrEmpty(cmd))
                {
                    Debug.LogWarning($"⚠️ Empty command found in '{keyword.commandName}'!");
                    continue;
                }

                string lowerCommand = cmd.ToLower().Trim();
                bool detected = false;

                Debug.Log($"🔍 Comparing: '{lowerText}' with command '{lowerCommand}' (Exact: {keyword.exactMatch})");

                if (keyword.exactMatch)
                {
                    // Exact match - whole word must match
                    string[] words = lowerText.Split(new char[] { ' ', ',', '.', '!', '?' },
                        System.StringSplitOptions.RemoveEmptyEntries);
                    detected = words.Contains(lowerCommand);

                    if (!detected)
                    {
                        Debug.Log($"❌ '{lowerCommand}' NOT found in words: [{string.Join(", ", words)}]");
                    }
                }
                else
                {
                    // Partial match - command can appear anywhere in text
                    detected = lowerText.Contains(lowerCommand);

                    if (!detected)
                    {
                        Debug.Log($"❌ '{lowerCommand}' NOT found in text");
                    }
                }

                if (detected)
                {
                    Debug.Log($"✅✅✅ KEYWORD DETECTED: '{keyword.commandName}' (Command: '{cmd}') ✅✅✅");

                    // Add to queue for main thread processing
                    lock (queueLock)
                    {
                        detectedKeywordsQueue.Enqueue(new KeywordDetectionData
                        {
                            commandName = keyword.commandName,
                            detectedCommand = cmd
                        });
                    }

                    // Only detect once per keyword (don't check other commands if one matches)
                    break;
                }
            }
        }
    }

    // This runs on Unity main thread (called from Update)
    void TriggerKeywordEvent(KeywordDetectionData data)
    {
        var keyword = keywords.FirstOrDefault(k => k.commandName == data.commandName);
        if (keyword == null) return;

        Debug.Log($"🎯 Triggering event for keyword: '{data.commandName}' (Command: '{data.detectedCommand}')");

        // Play sound if available
        if (audioSource != null && keywordDetectedSound != null)
        {
            audioSource.PlayOneShot(keywordDetectedSound);
            Debug.Log("🔊 Playing detection sound");
        }

        // Trigger keyword-specific event
        try
        {
            if (keyword.onDetected != null && keyword.onDetected.GetPersistentEventCount() > 0)
            {
                Debug.Log($"🎯 Invoking OnDetected event for '{keyword.commandName}' with {keyword.onDetected.GetPersistentEventCount()} listeners");
                keyword.onDetected?.Invoke();
            }
            else
            {
                Debug.LogWarning($"⚠️ No listeners attached to keyword '{keyword.commandName}' OnDetected event!");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error invoking event for '{data.commandName}': {e.Message}");
        }

        // Trigger general keyword detection event
        try
        {
            OnAnyKeywordDetected?.Invoke(data.commandName);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error invoking OnAnyKeywordDetected: {e.Message}");
        }

        // Update status
        UpdateStatus($"✅ Keyword: {data.commandName}", Color.cyan);
    }

    public void OnRecognitionResults(string results)
    {
        if (!string.IsNullOrEmpty(results))
        {
            // Remove square brackets before processing
            string cleanResults = results.Replace("[", "").Replace("]", "");

            Debug.Log($"📱 Raw Result: '{results}' → Cleaned: '{cleanResults}'");

            // Check for keywords (queues them for main thread)
            CheckForKeywords(cleanResults);

            // Update UI on main thread
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                fullText += cleanResults + " ";
                UpdateDisplayText(fullText);
                OnSpeechRecognized?.Invoke(cleanResults);
            });
        }

        isListening = false;

        if (shouldKeepListening && autoRestart)
        {
            StartCoroutine(RestartRecognition());
        }
    }

    public void OnRecognitionPartialResults(string results)
    {
        if (!string.IsNullOrEmpty(results))
        {
            // Remove square brackets before processing
            string cleanResults = results.Replace("[", "").Replace("]", "");

            // Check for keywords in partial results too (queues them)
            CheckForKeywords(cleanResults);

            // Update UI on main thread
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                string tempText = fullText + cleanResults;
                UpdateDisplayText(tempText + " [...]");
                Debug.Log($"📱 Partial - Raw: '{results}' → Cleaned: '{cleanResults}'");
                OnPartialSpeechDetected?.Invoke(cleanResults);
            });
        }
    }

    public void OnRecognitionError(string errorCode)
    {
        Debug.LogWarning("Recognition Error: " + errorCode);
        isListening = false;

        if (shouldKeepListening && autoRestart)
        {
            StartCoroutine(RestartRecognition());
        }
        else
        {
            UpdateStatus("Error: " + errorCode, Color.red);
        }
    }

    IEnumerator RestartRecognition()
    {
        yield return new WaitForSeconds(restartDelay);

        if (shouldKeepListening)
        {
            StartListening();
        }
    }

    void UpdateDisplayText(string text)
    {
        if (displayText != null)
        {
            displayText.text = text;
        }
    }

    void UpdateStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
    }

    // Editor Simulation
    IEnumerator SimulateEditorRecognition()
    {
        string[] samples = { "Hello", "Namaste", "test keyword", "Speech to text", "hello world" };
        shouldKeepListening = true;

        while (shouldKeepListening)
        {
            string sample = samples[UnityEngine.Random.Range(0, samples.Length)];

            // Check keywords in simulation
            CheckForKeywords(sample);

            fullText += sample + " ";
            UpdateDisplayText(fullText);
            OnSpeechRecognized?.Invoke(sample);

            yield return new WaitForSeconds(2f);
        }
    }

    void OnDestroy()
    {
        StopListening();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer != null)
        {
            try
            {
                speechRecognizer.Call("destroy");
            }
            catch { }
        }
#endif

        if (startButton != null) startButton.onClick.RemoveAllListeners();
        if (stopButton != null) stopButton.onClick.RemoveAllListeners();
        if (clearButton != null) clearButton.onClick.RemoveAllListeners();
    }

    // PUBLIC METHODS - Add/remove keywords from code
    public void AddKeyword(string commandName, string[] commands, UnityAction action, bool exactMatch = true)
    {
        var keyword = new Keyword
        {
            commandName = commandName,
            commands = commands,
            exactMatch = exactMatch,
            onDetected = new KeywordEvent()
        };
        keyword.onDetected.AddListener(action);
        keywords.Add(keyword);
    }

    public void RemoveKeyword(string commandName)
    {
        keywords.RemoveAll(k => k.commandName == commandName);
    }

    public void ClearAllKeywords()
    {
        keywords.Clear();
    }
}

// ============================================
// RecognitionListener Proxy for Android
// ============================================
#if UNITY_ANDROID && !UNITY_EDITOR
public class RecognitionListenerProxy : AndroidJavaProxy
{
    private RealTime_SpeechToText controller;

    public RecognitionListenerProxy(RealTime_SpeechToText controller) 
        : base("android.speech.RecognitionListener")
    {
        this.controller = controller;
    }

    void onResults(AndroidJavaObject results)
    {
        AndroidJavaObject arrayList = results.Call<AndroidJavaObject>("getStringArrayList", "results_recognition");
        if (arrayList != null)
        {
            int size = arrayList.Call<int>("size");
            if (size > 0)
            {
                string result = arrayList.Call<string>("get", 0);
                controller.OnRecognitionResults(result);
            }
        }
    }

    void onPartialResults(AndroidJavaObject results)
    {
        AndroidJavaObject arrayList = results.Call<AndroidJavaObject>("getStringArrayList", "results_recognition");
        if (arrayList != null)
        {
            int size = arrayList.Call<int>("size");
            if (size > 0)
            {
                string result = arrayList.Call<string>("get", 0);
                controller.OnRecognitionPartialResults(result);
            }
        }
    }

    void onError(int error)
    {
        string errorMsg = "Error Code: " + error;
        controller.OnRecognitionError(errorMsg);
    }

    void onReadyForSpeech(AndroidJavaObject @params) { }
    void onBeginningOfSpeech() { }
    void onRmsChanged(float rmsdB) { }
    void onBufferReceived(byte[] buffer) { }
    void onEndOfSpeech() { }
    void onEvent(int eventType, AndroidJavaObject @params) { }
}
#endif

// ============================================
// Unity Main Thread Dispatcher
// ============================================
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private readonly Queue<Action> _executionQueue = new Queue<Action>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            var obj = new GameObject("UnityMainThreadDispatcher");
            _instance = obj.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(obj);
        }
        return _instance;
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue()?.Invoke();
            }
        }
    }

    public void Enqueue(Action action)
    {
        if (action == null) return;

        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}