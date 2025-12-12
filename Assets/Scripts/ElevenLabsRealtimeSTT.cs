using NativeWebSocket;
using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ElevenLabsRealtimeSTT : MonoBehaviour
{
    [Header("API Config")]
    [SerializeField] private string apiKey = ""; // Set your ElevenLabs API key

    [Header("Audio Settings")]
    [SerializeField] private int sampleRate = 16000;
    [SerializeField] private float chunkSendInterval = 0.1f; // Send every 100ms

    [Header("VAD Settings - For Command Detection")]
    [SerializeField] private float commandPauseThreshold = 0.8f; // Fast commit for commands

    [Header("UI")]
    [SerializeField] private Text partialTranscriptText; // Real-time
    [SerializeField] private Text committedTranscriptText; // Final commands
    [SerializeField] private Text statusText;

    public CommandData[] CommandData;

    private AudioClip microphoneClip;
    private int lastSamplePosition = 0;
    private bool isRecording = false;
    private float nextSendTime = 0f;
    private bool isDestroyed = false;

    private string sessionToken;
    private WebSocket websocket;
    private StringBuilder allCommittedText = new StringBuilder();
    private string sessionId = "";
    private int commandCount = 0;

    private const string TOKEN_ENDPOINT = "https://api.elevenlabs.io/v1/single-use-token/realtime_scribe";
    private const string WS_BASE_URL = "wss://api.elevenlabs.io/v1/speech-to-text/realtime";
    private const int RECORDING_LENGTH = 999; // Huge buffer - never runs out

    private void Start()
    {
        Debug.Log("[STT] ========== Command Recognition System Starting ==========");
        Debug.Log("[STT] Mode: Continuous listening for voice commands");

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
        }
#endif

        _ = InitRealtimeSTT();
    }

    private async Task InitRealtimeSTT()
    {
        if (isDestroyed) return;

        UpdateStatus("Initializing...");
        Debug.Log("[STT] Getting session token...");

        sessionToken = await GetSessionToken();

        if (isDestroyed) return; // Check after async call

        if (string.IsNullOrEmpty(sessionToken))
        {
            UpdateStatus("❌ Token failed!");
            Debug.LogError("[STT] Failed to get token!");

            // Retry after 3 seconds
            if (!isDestroyed)
            {
                Invoke(nameof(RetryConnection), 3f);
            }
            return;
        }

        Debug.Log("[STT] ✅ Token received! Connecting...");
        UpdateStatus("Connecting...");

        // Optimized for COMMAND DETECTION - fast response
        string wsUrl = $"{WS_BASE_URL}" +
            $"?model_id=scribe_v2_realtime" +
            $"&audio_format=pcm_{sampleRate}" +
            $"&language_code=en" +
            $"&commit_strategy=vad" +
            $"&vad_silence_threshold_secs={commandPauseThreshold}" +
            $"&min_silence_duration_ms=600" +
            $"&vad_threshold=0.4" +
            $"&min_speech_duration_ms=200" +
            $"&include_timestamps=false" +
            $"&token={Uri.EscapeDataString(sessionToken)}";

        Debug.Log("[STT] Connecting WebSocket...");

        try
        {
            if (isDestroyed) return;

            websocket = new WebSocket(wsUrl);

            websocket.OnOpen += () => {
                if (isDestroyed) return;
                Debug.Log("[STT] ✅✅✅ CONNECTED! Ready for commands! ✅✅✅");
                UpdateStatus("✅ Ready!");
            };

            websocket.OnMessage += OnWebSocketMessage;

            websocket.OnError += (e) => {
                if (isDestroyed) return;
                Debug.LogError($"[STT] WebSocket Error: {e}");
                UpdateStatus("❌ Error");
            };

            websocket.OnClose += (e) => {
                if (isDestroyed) return;
                Debug.LogWarning($"[STT] Disconnected! Code: {e}");
                UpdateStatus("⚠️ Reconnecting...");
                isRecording = false;

                // Quick reconnect
                if (!isDestroyed)
                {
                    Invoke(nameof(RetryConnection), 1f);
                }
            };

            await websocket.Connect();
        }
        catch (Exception ex)
        {
            if (isDestroyed) return;
            Debug.LogError($"[STT] Connection failed: {ex.Message}");
            UpdateStatus("❌ Connection failed");

            // Retry
            if (!isDestroyed)
            {
                Invoke(nameof(RetryConnection), 2f);
            }
        }
    }

    private void RetryConnection()
    {
        if (isDestroyed) return;

        // Clean up old connection
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            return; // Already connected
        }

        Debug.Log("[STT] Retrying connection...");
        _ = InitRealtimeSTT();
    }

    private async Task<string> GetSessionToken()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[STT] ❌ API KEY NOT SET!");
            return null;
        }

        using (UnityWebRequest www = new UnityWebRequest(TOKEN_ENDPOINT, "POST"))
        {
            www.SetRequestHeader("xi-api-key", apiKey);
            www.SetRequestHeader("Content-Type", "application/json");

            byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();

            var op = www.SendWebRequest();
            while (!op.isDone && !isDestroyed)
            {
                await Task.Yield();
            }

            if (isDestroyed) return null;

#if UNITY_2020_1_OR_NEWER
            bool success = www.result == UnityWebRequest.Result.Success;
#else
            bool success = !www.isNetworkError && !www.isHttpError;
#endif

            if (success)
            {
                var response = JsonUtility.FromJson<TokenResponse>(www.downloadHandler.text);
                return response?.token;
            }
            else
            {
                Debug.LogError($"[STT] Token error: {www.error}");
                return null;
            }
        }
    }

    public void StartMicrophone()
    {
        if (isDestroyed) return;

        if (isRecording)
        {
            Debug.Log("[STT] Already recording");
            return;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[STT] ❌ NO MICROPHONE!");
            UpdateStatus("❌ No mic!");
            return;
        }

        string mic = Microphone.devices[0];
        Debug.Log($"[STT] 🎤 Starting mic: {mic}");
        Debug.Log($"[STT] Mode: CONTINUOUS COMMAND LISTENING");

        // Continuous recording with huge buffer
        microphoneClip = Microphone.Start(mic, true, RECORDING_LENGTH, sampleRate);
        lastSamplePosition = 0;
        isRecording = true;
        nextSendTime = Time.time + chunkSendInterval;
        commandCount = 0;

        UpdateStatus("🎤 Listening...");
        Debug.Log("[STT] 🎤 ALWAYS LISTENING - SAY YOUR COMMANDS! 🎤");
    }

    private void Update()
    {
        if (isDestroyed) return;

        // Dispatch messages
        websocket?.DispatchMessageQueue();

        // Stream audio continuously
        if (isRecording && microphoneClip != null && websocket?.State == WebSocketState.Open && Time.time >= nextSendTime)
        {
            SendAudioChunk();
            nextSendTime = Time.time + chunkSendInterval;
        }
    }

    private async void SendAudioChunk()
    {
        if (isDestroyed || websocket?.State != WebSocketState.Open) return;

        int pos = Microphone.GetPosition(null);
        if (pos < 0) return;

        // Handle wrap-around
        if (pos < lastSamplePosition)
        {
            pos += microphoneClip.samples;
        }

        int toRead = pos - lastSamplePosition;
        if (toRead <= 0) return;

        // Get samples
        float[] samples = new float[toRead];
        int startPos = lastSamplePosition % microphoneClip.samples;
        microphoneClip.GetData(samples, startPos);

        // Convert to PCM16
        byte[] pcm = ConvertToPCM16(samples);
        lastSamplePosition = pos;

        // Send chunk
        var message = new InputAudioChunkMessage
        {
            message_type = "input_audio_chunk",
            audio_base_64 = Convert.ToBase64String(pcm),
            sample_rate = sampleRate
        };

        try
        {
            await websocket.SendText(JsonUtility.ToJson(message));
        }
        catch (Exception ex)
        {
            if (!isDestroyed)
            {
                Debug.LogError($"[STT] Send error: {ex.Message}");
            }
        }
    }

    private void OnWebSocketMessage(byte[] bytes)
    {
        if (isDestroyed) return;

        string msg = Encoding.UTF8.GetString(bytes);

        try
        {
            var evt = JsonUtility.FromJson<RealtimeEvent>(msg);
            if (evt == null || string.IsNullOrEmpty(evt.message_type)) return;

            switch (evt.message_type)
            {
                case "session_started":
                    var sessionEvt = JsonUtility.FromJson<SessionStartedEvent>(msg);
                    sessionId = sessionEvt.session_id;
                    Debug.Log($"[STT] 🎉 SESSION ACTIVE: {sessionId}");
                    UpdateStatus("✅ Ready for commands!");
                    StartMicrophone();
                    break;

                case "partial_transcript":
                    if (!string.IsNullOrEmpty(evt.text) && partialTranscriptText != null)
                    {
                        Debug.Log($"[STT] 🟡 Hearing: \"{evt.text}\"");
                        partialTranscriptText.text = $"<color=yellow>🔊 {evt.text}</color>";
                    }
                    break;

                case "committed_transcript":
                case "committed_transcript_with_timestamps":
                    if (!string.IsNullOrEmpty(evt.text))
                    {
                        commandCount++;
                        Debug.Log($"[STT] ✅ COMMAND #{commandCount}: \"{evt.text}\"");

                        // Add to committed text with newline
                        allCommittedText.AppendLine(evt.text);

                        // Update UI on main thread
                        if (committedTranscriptText != null)
                        {
                            committedTranscriptText.text = allCommittedText.ToString();
                            Debug.Log($"[STT] Updated UI with text: {allCommittedText.ToString()}");
                        }

                        // Clear partial
                        if (partialTranscriptText != null)
                        {
                            partialTranscriptText.text = "<color=green>Ready...</color>";
                        }

                        UpdateStatus($"🎤 Listening ({commandCount})");

                        // HERE: Process your command!
                        ProcessCommand(evt.text);
                    }
                    break;

                case "scribe_error":
                case "scribe_auth_error":
                case "scribe_quota_exceeded_error":
                case "scribe_throttled_error":
                case "input_error":
                    var errorEvt = JsonUtility.FromJson<ScribeError>(msg);
                    string errorMsg = !string.IsNullOrEmpty(errorEvt.error_message) ? errorEvt.error_message : errorEvt.error;
                    Debug.LogError($"[STT] ❌ Error: {errorMsg}");
                    UpdateStatus($"❌ {errorMsg}");
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!isDestroyed)
            {
                Debug.LogWarning($"[STT] Parse error: {ex.Message}");
            }
        }
    }

    // YOUR COMMAND PROCESSING FUNCTION
    internal string command;
    private void ProcessCommand(string command)
    {
        // Convert to lowercase for comparison
        command = command.ToLower().Trim();

        // Check all CommandData entries
        foreach (var data in CommandData)
        {
            foreach (var cmd in data.commands)
            {
                if (string.IsNullOrEmpty(cmd)) continue;

                string lowerCmd = cmd.ToLower();

                // Match spoken text with command
                if (command.Contains(lowerCmd))
                {
                    data.Event?.Invoke();
                    return; // stop after first match
                }
            }
        }
    }

    private byte[] ConvertToPCM16(float[] samples)
    {
        byte[] pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short val = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)(val & 0xFF);
            pcm[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
        }
        return pcm;
    }

    private void UpdateStatus(string status)
    {
        if (isDestroyed) return;

        if (statusText != null)
        {
            statusText.text = status;
        }
    }

    private void OnDestroy()
    {
        Debug.Log("[STT] ========== SHUTTING DOWN ==========");
        Debug.Log($"[STT] Total commands processed: {commandCount}");

        isDestroyed = true;
        isRecording = false;

        // Cancel all invokes
        CancelInvoke();

        // Stop microphone
        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
            Debug.Log("[STT] Microphone stopped");
        }

        // Close websocket
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            try
            {
                _ = websocket.Close();
            }
            catch
            {
                // Ignore errors during shutdown
            }
            Debug.Log("[STT] WebSocket closed");
        }
    }

    private void OnApplicationQuit()
    {
        isDestroyed = true;
    }

    // === JSON Classes ===

    [Serializable]
    private class TokenResponse
    {
        public string token;
    }

    [Serializable]
    private class InputAudioChunkMessage
    {
        public string message_type;
        public string audio_base_64;
        public int sample_rate;
    }

    [Serializable]
    private class RealtimeEvent
    {
        public string message_type;
        public string text;
    }

    [Serializable]
    private class SessionStartedEvent
    {
        public string message_type;
        public string session_id;
    }

    [Serializable]
    private class ScribeError
    {
        public string message_type;
        public string error_message;
        public string error;
    }
}