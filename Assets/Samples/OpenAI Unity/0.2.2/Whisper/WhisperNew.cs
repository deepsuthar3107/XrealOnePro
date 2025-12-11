using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Samples.Whisper
{
    [Serializable]
    public class CommandData
    {
        public string CommandGroupName;
        public string[] commands;
        public UnityEvent Event;
    }

    public class WhisperNew : MonoBehaviour
    {
        [SerializeField] private Button toggleListeningButton;
        [SerializeField] private TextMeshProUGUI message;
        [SerializeField] private Dropdown dropdown;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Voice Commands")]
        [SerializeField] private List<CommandData> commandDataList = new List<CommandData>();

        [Header("API Configuration")]
        [SerializeField] private string openAiApiKey = "";

        [Header("Real-time Settings")]
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private float chunkDuration = 2f;
        [SerializeField] private float silenceThreshold = 0.015f;
        [SerializeField] private int maxConcurrentRequests = 2;
        [SerializeField] private float minAudioLength = 0.5f;
        [SerializeField] private float commandCooldown = 2f;
        [SerializeField] private bool simulateInEditor = false;

        [Header("Accuracy Settings")]
        [SerializeField] private bool usePromptHints = true;
        [SerializeField] private float minConfidenceLevel = 0.1f;
        [SerializeField] private bool filterHallucinations = true;

        [Header("Android Settings")]
        [SerializeField] private bool requestMicPermissionOnStart = true;
        [SerializeField] private int androidSampleRate = 16000;

        private const string fileName = "output.wav";
        private string apiKey = "";

        private AudioClip recordingClip;
        private bool isListening;
        private float recordingTime;
        private OpenAIApi openai;
        private Queue<AudioChunk> audioQueue = new Queue<AudioChunk>();
        private int activeRequests = 0;
        private int microphoneIndex;
        private int lastSamplePosition = 0;
        private List<TranscriptionEntry> recentTranscriptions = new List<TranscriptionEntry>();
        private float lastTranscriptionTime;
        private string lastRecognizedText = "";
        private Dictionary<string, float> commandLastExecuted = new Dictionary<string, float>();
        private string lastExecutedCommand = "";
        private bool micPermissionGranted = false;
        private bool isInitialized = false;

        private class TranscriptionEntry
        {
            public string text;
            public float timestamp;
        }

        private class AudioChunk
        {
            public float[] samples;
            public float timestamp;
            public float duration;
        }

        private readonly HashSet<string> commonHallucinations = new HashSet<string>
        {
            "thanks for watching",
            "thank you for watching",
            "please subscribe",
            "like and subscribe",
            "don't forget to subscribe",
            "bye",
            "goodbye",
            "thank you",
            "you",
            ".",
            "...",
            "[BLANK_AUDIO]",
            "music",
            "[music]"
        };

        private void Start()
        {
            if (!string.IsNullOrEmpty(openAiApiKey))
            {
                apiKey = openAiApiKey;
            }
            else if (PlayerPrefs.HasKey("OPENAI_API_KEY"))
            {
                apiKey = PlayerPrefs.GetString("OPENAI_API_KEY");
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("❌ NO API KEY SET!");
                UpdateStatus("❌ No API Key");
                if (toggleListeningButton != null)
                    toggleListeningButton.interactable = false;
                return;
            }

            if (!apiKey.StartsWith("sk-") || apiKey.Length < 20)
            {
                Debug.LogError("❌ INVALID API KEY FORMAT!");
                UpdateStatus("❌ Invalid Key");
                if (toggleListeningButton != null)
                    toggleListeningButton.interactable = false;
                return;
            }

            openai = new OpenAIApi(apiKey);
            Debug.Log("✅ OpenAI API initialized");

#if UNITY_WEBGL && !UNITY_EDITOR
            dropdown.options.Add(new Dropdown.OptionData("Microphone not supported on WebGL"));
            toggleListeningButton.interactable = false;
#else
#if UNITY_ANDROID && !UNITY_EDITOR
            if (requestMicPermissionOnStart)
            {
                RequestMicrophonePermission();
            }
            else
            {
                InitializeMicrophone();
            }
#else
            InitializeMicrophone();
#endif
#endif

            UpdateStatus("✅ Ready");

#if UNITY_EDITOR
            if (simulateInEditor)
            {
                Debug.Log("🎮 Editor Simulation - Press 0-9 for commands");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void RequestMicrophonePermission()
        {
<<<<<<< Updated upstream
            Debug.Log(":iphone: Checking microphone permission...");
            UpdateStatus(":iphone: Checking permission");
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log(":iphone: Requesting microphone permission...");
                UpdateStatus(":iphone: Grant permission");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
=======
            Debug.Log("📱 Checking microphone permission...");
            UpdateStatus("📱 Checking permission");
            
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log("📱 Requesting microphone permission...");
                UpdateStatus("📱 Grant permission");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                
>>>>>>> Stashed changes
                // Start coroutine to check permission after request
                StartCoroutine(CheckPermissionAfterRequest());
            }
            else
            {
<<<<<<< Updated upstream
                Debug.Log(":white_check_mark: Microphone permission already granted");
=======
                Debug.Log("✅ Microphone permission already granted");
>>>>>>> Stashed changes
                micPermissionGranted = true;
                InitializeMicrophone();
            }
        }
<<<<<<< Updated upstream
=======

>>>>>>> Stashed changes
        private System.Collections.IEnumerator CheckPermissionAfterRequest()
        {
            // Wait a frame for the permission dialog
            yield return new WaitForSeconds(0.5f);
<<<<<<< Updated upstream
            // Check periodically if permission was granted
            int maxAttempts = 20; // Check for up to 10 seconds
            int attempts = 0;
=======
            
            // Check periodically if permission was granted
            int maxAttempts = 20; // Check for up to 10 seconds
            int attempts = 0;
            
>>>>>>> Stashed changes
            while (attempts < maxAttempts)
            {
                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
<<<<<<< Updated upstream
                    Debug.Log(":white_check_mark: Microphone permission granted");
                    micPermissionGranted = true;
                    UpdateStatus(":white_check_mark: Permission granted");
                    InitializeMicrophone();
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
                attempts++;
            }
            // Permission was denied or dialog dismissed
            Debug.LogWarning(":x: Microphone permission denied or timed out");
            UpdateStatus(":x: Permission denied");
            toggleListeningButton.interactable = false;
=======
                    Debug.Log("✅ Microphone permission granted");
                    micPermissionGranted = true;
                    UpdateStatus("✅ Permission granted");
                    InitializeMicrophone();
                    yield break;
                }
                
                yield return new WaitForSeconds(0.5f);
                attempts++;
            }
            
            // Permission was denied or dialog dismissed
            Debug.LogWarning("❌ Microphone permission denied or timed out");
            UpdateStatus("❌ Permission denied");
            toggleListeningButton.interactable = false;
            
>>>>>>> Stashed changes
            if (message != null)
            {
                message.text = "Microphone permission required. Please restart and grant permission.";
            }
        }
#endif

        private void InitializeMicrophone()
        {
            if (isInitialized) return;

            Debug.Log($"🎤 Initializing microphone...");
            Debug.Log($"🎤 Available devices: {Microphone.devices.Length}");

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("❌ No microphone detected!");
                UpdateStatus("❌ No mic");

                if (message != null)
                {
                    message.text = "No microphone found on device";
                }

                if (toggleListeningButton != null)
                {
                    toggleListeningButton.interactable = false;
                }

#if UNITY_ANDROID && !UNITY_EDITOR
                // On Android, try requesting permission again
                Debug.Log("📱 Trying to request permission again...");
                StartCoroutine(RetryMicrophoneAccess());
#endif
                return;
            }

            // List all available microphones
            Debug.Log("📱 Available microphones:");
            foreach (var device in Microphone.devices)
            {
                dropdown.options.Add(new Dropdown.OptionData(device));
                Debug.Log($"  ✓ {device}");

                // Get device capabilities
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);
                Debug.Log($"    Range: {minFreq}Hz - {maxFreq}Hz");
            }

            if (toggleListeningButton != null)
            {
                toggleListeningButton.onClick.AddListener(ToggleListening);
            }

            dropdown.onValueChanged.AddListener(ChangeMicrophone);

            microphoneIndex = PlayerPrefs.GetInt("user-mic-device-index", 0);
            dropdown.SetValueWithoutNotify(microphoneIndex);
            isInitialized = true;

#if UNITY_ANDROID
            if (androidSampleRate > 0)
            {
                sampleRate = androidSampleRate;
                Debug.Log($"📱 Using Android sample rate: {sampleRate}Hz");
            }
#endif

            Debug.Log("✅ Microphone initialized successfully");
            UpdateStatus("✅ Ready");

            if (toggleListeningButton != null)
            {
                toggleListeningButton.interactable = true;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private System.Collections.IEnumerator RetryMicrophoneAccess()
        {
            yield return new WaitForSeconds(2f);
            
            Debug.Log("🔄 Retrying microphone access...");
            
            // Check permission again
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log("✅ Permission now granted, reinitializing...");
                isInitialized = false;
                InitializeMicrophone();
            }
            else
            {
                Debug.LogError("❌ Still no microphone permission");
                UpdateStatus("❌ Grant permission");
            }
        }
#endif

        private void ChangeMicrophone(int index)
        {
            bool wasListening = isListening;
            if (wasListening) StopListening();
            microphoneIndex = index;
            PlayerPrefs.SetInt("user-mic-device-index", index);
            if (wasListening) StartListening();
        }

        private void ToggleListening()
        {
            if (isListening) StopListening();
            else StartListening();
        }

        public void StartListening()
        {
            if (isListening) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!micPermissionGranted) return;
#endif

            isListening = true;
            recordingTime = 0f;
            lastSamplePosition = 0;
            recentTranscriptions.Clear();

            UpdateStatus("🎤 Listening...");
            message.text = "Speak now...";

#if !UNITY_WEBGL
            string deviceName = dropdown.options[microphoneIndex].text;
            recordingClip = Microphone.Start(deviceName, true, 60, sampleRate);

            if (recordingClip == null)
            {
                Debug.LogError("❌ Mic failed!");
                isListening = false;
            }
#endif
        }

        private void StopListening()
        {
            if (!isListening) return;
            isListening = false;
            UpdateStatus("⏸️ Stopped");

#if !UNITY_WEBGL
            string deviceName = dropdown.options[microphoneIndex].text;
            if (Microphone.IsRecording(deviceName))
            {
                Microphone.End(deviceName);
            }
#endif

            audioQueue.Clear();
            if (recordingClip != null)
            {
                Destroy(recordingClip);
                recordingClip = null;
            }
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (simulateInEditor) HandleEditorSimulation();
#endif

            if (!isListening || recordingClip == null) return;

            recordingTime += Time.deltaTime;

            if (recordingTime >= chunkDuration)
            {
                CaptureAudioChunk();
                recordingTime = 0f;
            }

            while (audioQueue.Count > 0 && activeRequests < maxConcurrentRequests)
            {
                ProcessNextInQueue();
            }
        }

        private void CaptureAudioChunk()
        {
#if !UNITY_WEBGL
            string deviceName = dropdown.options[microphoneIndex].text;
            if (!Microphone.IsRecording(deviceName)) return;

            int currentPosition = Microphone.GetPosition(deviceName);
            if (currentPosition < 0 || recordingClip == null) return;

            int samplesNeeded = Mathf.FloorToInt(chunkDuration * sampleRate);

            if (currentPosition < lastSamplePosition) lastSamplePosition = 0;

            int samplesDiff = currentPosition - lastSamplePosition;
            if (samplesDiff < samplesNeeded * 0.5f) return;

            int samplesToRead = Mathf.Min(samplesNeeded, samplesDiff);
            float[] samples = new float[samplesToRead];

            recordingClip.GetData(samples, lastSamplePosition);
            lastSamplePosition = (lastSamplePosition + samplesToRead) % recordingClip.samples;

            float avgAmplitude = samples.Average(s => Mathf.Abs(s));
            float maxAmplitude = samples.Max(s => Mathf.Abs(s));

            if (avgAmplitude > silenceThreshold && maxAmplitude > minConfidenceLevel)
            {
                float duration = (float)samplesToRead / sampleRate;
                if (duration >= minAudioLength)
                {
                    audioQueue.Enqueue(new AudioChunk { samples = samples, timestamp = Time.time, duration = duration });
                }
            }
#endif
        }

        private async void ProcessNextInQueue()
        {
            if (audioQueue.Count == 0) return;

            activeRequests++;
            AudioChunk chunk = audioQueue.Dequeue();

            try
            {
                AudioClip tempClip = AudioClip.Create("chunk", chunk.samples.Length, 1, sampleRate, false);
                tempClip.SetData(chunk.samples, 0);

                byte[] data = SaveWav.Save(fileName, tempClip);

                string promptHint = usePromptHints ? BuildPromptFromCommands() : "";

                var req = new CreateAudioTranscriptionsRequest
                {
                    FileData = new FileData() { Data = data, Name = "audio.wav" },
                    Model = "whisper-1",
                    Language = "en",
                    Temperature = 0f,
                    ResponseFormat = AudioResponseFormat.Json,
                    Prompt = promptHint
                };

                var response = await openai.CreateAudioTranscription(req);
                string text = response.Text?.Trim();

                if (!string.IsNullOrEmpty(text) && text.Length > 1)
                {
                    text = text.ToLower().Trim();

                    if (filterHallucinations && IsHallucination(text))
                    {
                        Debug.Log($"🚫 Filtered: '{text}'");
                        Destroy(tempClip);
                        return;
                    }

                    if (!IsDuplicateFromSameAudio(text))
                    {
                        message.text = text;
                        CheckAndExecuteCommand(text);

                        recentTranscriptions.Add(new TranscriptionEntry { text = text, timestamp = Time.time });
                        CleanupOldTranscriptions();

                        Debug.Log($"📝 Transcribed: {text}");
                    }
                }

                Destroy(tempClip);
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error: {e.Message}");
            }
            finally
            {
                activeRequests--;
            }
        }

        private bool IsDuplicateFromSameAudio(string text)
        {
            float duplicateWindow = 3f;
            float currentTime = Time.time;

            foreach (var entry in recentTranscriptions)
            {
                if (currentTime - entry.timestamp < duplicateWindow)
                {
                    if (entry.text == text) return true;

                    float similarity = CalculateSimilarity(text, entry.text);
                    if (similarity > 0.9f) return true;
                }
            }
            return false;
        }

        private bool IsHallucination(string text)
        {
            if (commonHallucinations.Contains(text)) return true;

            foreach (var h in commonHallucinations)
            {
                if (text == h || text.StartsWith(h) || text.EndsWith(h)) return true;
            }

            if (text.Length < 3 && !IsValidCommand(text)) return true;

            return false;
        }

        private bool IsValidCommand(string text)
        {
            foreach (var cd in commandDataList)
            {
                foreach (var c in cd.commands)
                {
                    if (text.Contains(c.ToLower().Trim())) return true;
                }
            }
            return false;
        }

        private string BuildPromptFromCommands()
        {
            HashSet<string> allCommands = new HashSet<string>();

            foreach (var cd in commandDataList)
            {
                foreach (var c in cd.commands)
                {
                    allCommands.Add(c.ToLower().Trim());
                }
            }

            if (allCommands.Count == 0) return "";

            string prompt = "Voice commands: " + string.Join(", ", allCommands);
            if (prompt.Length > 200) prompt = prompt.Substring(0, 200);

            return prompt;
        }

        private void CleanupOldTranscriptions()
        {
            float maxAge = 10f;
            float currentTime = Time.time;
            recentTranscriptions.RemoveAll(e => currentTime - e.timestamp > maxAge);
        }

        private float CalculateSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;
            int distance = LevenshteinDistance(a, b);
            int maxLen = Mathf.Max(a.Length, b.Length);
            return 1f - (float)distance / maxLen;
        }

        private int LevenshteinDistance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }

        private void CheckAndExecuteCommand(string text)
        {
            foreach (var cd in commandDataList)
            {
                foreach (var c in cd.commands)
                {
                    string nc = c.ToLower().Trim();

                    if (text == nc || text.Contains(nc))
                    {
                        if (lastExecutedCommand == nc && Time.time - commandLastExecuted.GetValueOrDefault(nc, 0) < 1f)
                        {
                            Debug.Log($"⏳ Too soon: '{c}'");
                            return;
                        }

                        Debug.Log($"✅ Command: '{c}'");
                        message.text = $"✅ {c}";
                        UpdateStatus($"✅ {c}");

                        commandLastExecuted[nc] = Time.time;
                        lastExecutedCommand = nc;

                        cd.Event?.Invoke();
                        return;
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void HandleEditorSimulation()
        {
            for (int i = 0; i < 10 && i < commandDataList.Count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                {
                    var cd = commandDataList[i];
                    if (cd.commands != null && cd.commands.Length > 0)
                    {
                        string sc = cd.commands[0].ToLower();

                        if (lastExecutedCommand == sc && Time.time - commandLastExecuted.GetValueOrDefault(sc, 0) < commandCooldown)
                        {
                            Debug.Log($"⏳ Cooldown: {sc}");
                            return;
                        }

                        Debug.Log($"🎮 SIMULATED: {sc}");
                        message.text = $"🎮 {sc}";

                        commandLastExecuted[sc] = Time.time;
                        lastExecutedCommand = sc;

                        cd.Event?.Invoke();
                    }
                }
            }
        }
#endif

        private void UpdateStatus(string status)
        {
            if (statusText != null) statusText.text = status;
        }

        private void OnDestroy()
        {
            StopListening();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && isListening) StopListening();
        }

        private void OnApplicationQuit()
        {
            StopListening();
        }

        public void SetApiKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith("sk-")) return;
            apiKey = key;
            openAiApiKey = key;
            openai = new OpenAIApi(apiKey);
            if (toggleListeningButton != null) toggleListeningButton.interactable = true;
        }

        // Public method to manually retry microphone initialization
        public void RetryMicrophoneSetup()
        {
            Debug.Log("🔄 Manual retry of microphone setup...");

#if UNITY_ANDROID && !UNITY_EDITOR
            isInitialized = false;
            micPermissionGranted = false;
            RequestMicrophonePermission();
#else
            isInitialized = false;
            InitializeMicrophone();
#endif
        }

        // Public method to check current status
        public void CheckMicrophoneStatus()
        {
            Debug.Log("=== MICROPHONE STATUS ===");
            Debug.Log($"Platform: {Application.platform}");
            Debug.Log($"Available devices: {Microphone.devices.Length}");

            foreach (var device in Microphone.devices)
            {
                Debug.Log($"  Device: {device}");
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            bool hasPermission = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
            Debug.Log($"Has microphone permission: {hasPermission}");
            Debug.Log($"Permission flag: {micPermissionGranted}");
#endif

            Debug.Log($"Initialized: {isInitialized}");
            Debug.Log($"Is Listening: {isListening}");
            Debug.Log("========================");
        }
    }
}