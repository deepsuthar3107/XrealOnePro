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

        [Header("Real-time Settings")]
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private float chunkDuration = 2f; // 2 seconds for better accuracy
        [SerializeField] private float silenceThreshold = 0.015f;
        [SerializeField] private int maxConcurrentRequests = 2; // Limit parallel requests
        [SerializeField] private float minAudioLength = 0.5f; // Minimum audio length in seconds
        [SerializeField] private float commandCooldown = 2f; // Prevent same command multiple times
        [SerializeField] private bool simulateInEditor = false; // Test commands without microphone

        [Header("Accuracy Settings")]
        [SerializeField] private bool usePromptHints = true; // Guide Whisper with expected commands
        [SerializeField] private float minConfidenceLevel = 0.1f; // Minimum audio energy level
        [SerializeField] private bool filterHallucinations = true; // Remove common Whisper hallucinations

        [Header("Android Settings")]
        [SerializeField] private bool requestMicPermissionOnStart = true;
        [SerializeField] private int androidSampleRate = 16000; // Some Android devices need specific rates

        private const string fileName = "output.wav";
        private string apiKey = "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";

        private AudioClip recordingClip;
        private bool isListening;
        private float recordingTime;
        private OpenAIApi openai;
        private Queue<AudioChunk> audioQueue = new Queue<AudioChunk>();
        private int activeRequests = 0;
        private int microphoneIndex;
        private int lastSamplePosition = 0;
        private HashSet<string> recentTranscriptions = new HashSet<string>();
        private float lastTranscriptionTime;
        private string lastRecognizedText = "";
        private Dictionary<string, float> commandLastExecuted = new Dictionary<string, float>(); // Track command cooldowns
        private string lastExecutedCommand = ""; // Track last command to prevent duplicates
        private bool micPermissionGranted = false;
        private bool isInitialized = false;

        // Common Whisper hallucinations to filter out
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

        private class AudioChunk
        {
            public float[] samples;
            public float timestamp;
            public float duration;
        }

        private void Start()
        {
            apiKey = PlayerPrefs.GetString("OPENAI_API_KEY", apiKey);

            if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("sk-proj-"))
            {
                Debug.LogWarning("⚠️ Please set a valid OpenAI API key!");
            }

            openai = new OpenAIApi(apiKey);

#if UNITY_WEBGL && !UNITY_EDITOR
            dropdown.options.Add(new Dropdown.OptionData("Microphone not supported on WebGL"));
            toggleListeningButton.interactable = false;
#else
            // Android permission handling
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

            // Editor simulation setup
#if UNITY_EDITOR
            if (simulateInEditor)
            {
                Debug.Log("🎮 Editor Simulation Mode Enabled - Press number keys to test commands:");
                for (int i = 0; i < commandDataList.Count && i < 10; i++)
                {
                    Debug.Log($"  Press '{i}' to simulate: {commandDataList[i].CommandGroupName}");
                }
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void RequestMicrophonePermission()
        {
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log("📱 Requesting microphone permission...");
                UpdateStatus("📱 Permission needed");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                Invoke(nameof(CheckPermissionAndInitialize), 1f);
            }
            else
            {
                micPermissionGranted = true;
                InitializeMicrophone();
            }
        }

        private void CheckPermissionAndInitialize()
        {
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                micPermissionGranted = true;
                InitializeMicrophone();
            }
            else
            {
                Debug.LogError("❌ Microphone permission denied!");
                UpdateStatus("❌ Permission denied");
                toggleListeningButton.interactable = false;
            }
        }
#endif

        private void InitializeMicrophone()
        {
            if (isInitialized) return;

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("❌ No microphone detected!");
                UpdateStatus("❌ No microphone");
                toggleListeningButton.interactable = false;
                return;
            }

            // List all available microphones
            Debug.Log($"🎤 Found {Microphone.devices.Length} microphone(s):");
            foreach (var device in Microphone.devices)
            {
                dropdown.options.Add(new Dropdown.OptionData(device));
                Debug.Log($"  - {device}");

                // Get microphone capabilities
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);
                Debug.Log($"    Frequency range: {minFreq}Hz - {maxFreq}Hz");
            }

            toggleListeningButton.onClick.AddListener(ToggleListening);
            dropdown.onValueChanged.AddListener(ChangeMicrophone);

            microphoneIndex = PlayerPrefs.GetInt("user-mic-device-index", 0);
            dropdown.SetValueWithoutNotify(microphoneIndex);

            isInitialized = true;

#if UNITY_ANDROID
            // Use Android-specific sample rate if specified
            if (androidSampleRate > 0)
            {
                sampleRate = androidSampleRate;
                Debug.Log($"📱 Using Android sample rate: {sampleRate}Hz");
            }
#endif
        }

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
            if (isListening)
            {
                StopListening();
            }
            else
            {
                StartListening();
            }
        }

        public void StartListening()
        {
            if (isListening) return;

            isListening = true;
            recordingTime = 0f;
            lastSamplePosition = 0;
            lastTranscriptionTime = Time.time;
            recentTranscriptions.Clear();
            lastRecognizedText = "";

            UpdateStatus("🎤 Listening...");
            message.text = "Speak now...";

#if !UNITY_WEBGL
            string deviceName = dropdown.options[microphoneIndex].text;
            recordingClip = Microphone.Start(deviceName, true, 60, sampleRate);

            if (recordingClip == null)
            {
                Debug.LogError("❌ Failed to start microphone!");
                UpdateStatus("❌ Mic failed");
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
                Debug.Log("🛑 Microphone stopped");
            }
#endif

            // Clear pending queue
            audioQueue.Clear();

            // Destroy recording clip to free memory
            if (recordingClip != null)
            {
                Destroy(recordingClip);
                recordingClip = null;
            }
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Editor simulation mode
            if (simulateInEditor)
            {
                HandleEditorSimulation();
            }
#endif

            if (!isListening || recordingClip == null) return;

            recordingTime += Time.deltaTime;

            // Capture audio chunks
            if (recordingTime >= chunkDuration)
            {
                CaptureAudioChunk();
                recordingTime = 0f;
            }

            // Process queue with concurrency limit
            while (audioQueue.Count > 0 && activeRequests < maxConcurrentRequests)
            {
                ProcessNextInQueue();
            }

            // Show queue status
            if (audioQueue.Count > 0)
            {
                UpdateStatus($"🎤 Processing ({activeRequests}/{maxConcurrentRequests})");
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

            // Handle wraparound
            if (currentPosition < lastSamplePosition)
            {
                lastSamplePosition = 0;
            }

            int samplesDiff = currentPosition - lastSamplePosition;
            if (samplesDiff < samplesNeeded * 0.5f) return;

            // Ensure we don't exceed clip length
            int samplesToRead = Mathf.Min(samplesNeeded, samplesDiff);
            float[] samples = new float[samplesToRead];

            recordingClip.GetData(samples, lastSamplePosition);
            lastSamplePosition = (lastSamplePosition + samplesToRead) % recordingClip.samples;

            // Check audio quality
            float avgAmplitude = samples.Average(s => Mathf.Abs(s));
            float maxAmplitude = samples.Max(s => Mathf.Abs(s));

            // More strict audio validation to avoid hallucinations
            if (avgAmplitude > silenceThreshold && maxAmplitude > minConfidenceLevel)
            {
                // Only queue if audio seems valid
                float duration = (float)samplesToRead / sampleRate;
                if (duration >= minAudioLength)
                {
                    audioQueue.Enqueue(new AudioChunk
                    {
                        samples = samples,
                        timestamp = Time.time,
                        duration = duration
                    });
                }
            }
            else
            {
                // Audio too weak - likely to cause hallucinations
                Debug.Log($"⚠️ Audio too weak (avg: {avgAmplitude:F4}, max: {maxAmplitude:F4}) - skipping to avoid hallucinations");
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
                // Create temporary AudioClip
                AudioClip tempClip = AudioClip.Create("chunk", chunk.samples.Length, 1, sampleRate, false);
                tempClip.SetData(chunk.samples, 0);

                byte[] data = SaveWav.Save(fileName, tempClip);

                // Build prompt with expected commands to guide Whisper
                string promptHint = "";
                if (usePromptHints)
                {
                    promptHint = BuildPromptFromCommands();
                }

                var req = new CreateAudioTranscriptionsRequest
                {
                    FileData = new FileData() { Data = data, Name = "audio.wav" },
                    Model = "whisper-1",
                    Language = "en", // Specify language for better accuracy
                    Temperature = 0f, // Most deterministic
                    ResponseFormat = AudioResponseFormat.Json,
                    Prompt = promptHint // Guide Whisper with expected words
                };

                var response = await openai.CreateAudioTranscription(req);
                string text = response.Text?.Trim();

                if (!string.IsNullOrEmpty(text) && text.Length > 1)
                {
                    // Normalize text
                    text = text.ToLower().Trim();

                    // Filter hallucinations
                    if (filterHallucinations && IsHallucination(text))
                    {
                        Debug.Log($"🚫 Filtered hallucination: '{text}'");
                        Destroy(tempClip);
                        return;
                    }

                    // Check if this is new content
                    if (!IsDuplicateOrSimilar(text))
                    {
                        lastRecognizedText = text;
                        lastTranscriptionTime = Time.time;

                        message.text = text;
                        CheckAndExecuteCommand(text);

                        // Add to recent transcriptions with timestamp cleanup
                        recentTranscriptions.Add(text);
                        CleanupOldTranscriptions();

                        Debug.Log($"📝 Transcribed: {text}");
                    }
                    else
                    {
                        Debug.Log($"⏭️ Skipped duplicate: '{text}'");
                    }
                }

                Destroy(tempClip);
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Transcription error: {e.Message}");
                UpdateStatus("⚠️ Error - Check API key");
            }
            finally
            {
                activeRequests--;
            }
        }

        private bool IsDuplicateOrSimilar(string text)
        {
            // Exact match
            if (recentTranscriptions.Contains(text))
            {
                return true;
            }

            // Check similarity with recent transcriptions
            foreach (var recent in recentTranscriptions)
            {
                float similarity = CalculateSimilarity(text, recent);
                if (similarity > 0.85f) // 85% similarity threshold
                {
                    return true;
                }
            }

            // Check if it's a subset/superset of last recognized text
            if (!string.IsNullOrEmpty(lastRecognizedText))
            {
                if (text.Contains(lastRecognizedText) || lastRecognizedText.Contains(text))
                {
                    if (CalculateSimilarity(text, lastRecognizedText) > 0.7f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsHallucination(string text)
        {
            // Check exact matches
            if (commonHallucinations.Contains(text))
            {
                return true;
            }

            // Check if text contains only common hallucination phrases
            foreach (var hallucination in commonHallucinations)
            {
                if (text == hallucination || text.StartsWith(hallucination) || text.EndsWith(hallucination))
                {
                    return true;
                }
            }

            // Check for very short transcriptions that are likely hallucinations
            if (text.Length < 3 && !IsValidCommand(text))
            {
                return true;
            }

            return false;
        }

        private bool IsValidCommand(string text)
        {
            // Check if text matches any of our expected commands
            foreach (var commandData in commandDataList)
            {
                foreach (var command in commandData.commands)
                {
                    if (text.Contains(command.ToLower().Trim()))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private string BuildPromptFromCommands()
        {
            // Build a hint string with all expected commands
            // This guides Whisper to recognize these words more accurately
            HashSet<string> allCommands = new HashSet<string>();

            foreach (var commandData in commandDataList)
            {
                foreach (var command in commandData.commands)
                {
                    allCommands.Add(command.ToLower().Trim());
                }
            }

            if (allCommands.Count == 0)
            {
                return "";
            }

            // Create a natural prompt that includes our commands
            string prompt = "Voice commands: " + string.Join(", ", allCommands);

            // Whisper prompt should be under 224 tokens
            if (prompt.Length > 200)
            {
                prompt = prompt.Substring(0, 200);
            }

            Debug.Log($"🎯 Using prompt hint: {prompt}");
            return prompt;
        }

        private void CleanupOldTranscriptions()
        {
            // Keep only the 10 most recent transcriptions
            if (recentTranscriptions.Count > 10)
            {
                var toRemove = recentTranscriptions.Take(recentTranscriptions.Count - 10).ToList();
                foreach (var item in toRemove)
                {
                    recentTranscriptions.Remove(item);
                }
            }
        }

        private float CalculateSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0f;

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
                    d[i, j] = Mathf.Min(
                        Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }

            return d[a.Length, b.Length];
        }

        private void CheckAndExecuteCommand(string text)
        {
            foreach (var commandData in commandDataList)
            {
                foreach (var command in commandData.commands)
                {
                    string normalizedCommand = command.ToLower().Trim();

                    // Exact match or contains
                    if (text == normalizedCommand || text.Contains(normalizedCommand))
                    {
                        // Check cooldown to prevent multiple executions
                        if (IsCommandOnCooldown(normalizedCommand))
                        {
                            Debug.Log($"⏳ Command '{command}' on cooldown, ignoring duplicate");
                            return;
                        }

                        // Check if this is the same command as last executed (additional safety)
                        if (lastExecutedCommand == normalizedCommand && Time.time - commandLastExecuted.GetValueOrDefault(normalizedCommand, 0) < commandCooldown * 0.5f)
                        {
                            Debug.Log($"⏳ Duplicate command '{command}' detected, ignoring");
                            return;
                        }

                        Debug.Log($"✅ Command executed: '{command}' from group '{commandData.CommandGroupName}'");
                        message.text = $"✅ {command}";
                        UpdateStatus($"✅ {command}");

                        // Update cooldown tracking
                        commandLastExecuted[normalizedCommand] = Time.time;
                        lastExecutedCommand = normalizedCommand;

                        commandData.Event?.Invoke();
                        return;
                    }
                }
            }
        }

        private bool IsCommandOnCooldown(string command)
        {
            if (commandLastExecuted.TryGetValue(command, out float lastTime))
            {
                return (Time.time - lastTime) < commandCooldown;
            }
            return false;
        }

#if UNITY_EDITOR
        private void HandleEditorSimulation()
        {
            // Simulate commands with number keys (0-9)
            for (int i = 0; i < 10 && i < commandDataList.Count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                {
                    var commandData = commandDataList[i];
                    if (commandData.commands != null && commandData.commands.Length > 0)
                    {
                        string simulatedCommand = commandData.commands[0].ToLower();

                        // Check cooldown
                        if (IsCommandOnCooldown(simulatedCommand))
                        {
                            Debug.Log($"⏳ Simulated command '{simulatedCommand}' on cooldown");
                            UpdateStatus($"⏳ Cooldown: {commandData.CommandGroupName}");
                            return;
                        }

                        Debug.Log($"🎮 SIMULATED: '{commandData.CommandGroupName}' - {simulatedCommand}");
                        message.text = $"🎮 SIMULATED: {simulatedCommand}";
                        UpdateStatus($"🎮 Simulated: {commandData.CommandGroupName}");

                        // Update cooldown
                        commandLastExecuted[simulatedCommand] = Time.time;
                        lastExecutedCommand = simulatedCommand;

                        commandData.Event?.Invoke();
                    }
                }
            }
        }
#endif

        private void UpdateStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }
        }

        private void OnDestroy()
        {
            StopListening();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && isListening)
            {
                Debug.Log("📱 App paused - stopping microphone");
                StopListening();
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            else if (!pauseStatus && isInitialized)
            {
                // Re-check permission when returning from pause
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
                    micPermissionGranted = false;
                    Debug.LogWarning("⚠️ Microphone permission lost");
                    UpdateStatus("❌ Permission lost");
                }
            }
#endif
        }

        private void OnApplicationQuit()
        {
            StopListening();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android-specific: stop when losing focus
            if (!hasFocus && isListening)
            {
                Debug.Log("📱 App lost focus - stopping microphone");
                StopListening();
            }
#endif
        }
    }
}