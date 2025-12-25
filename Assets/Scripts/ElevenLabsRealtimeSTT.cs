using NativeWebSocket;
using System;
using System.Collections;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class ElevenLabsRealtimeSTT : MonoBehaviour
{
    [Header("API Config")]
    [SerializeField] private string apiKey = ""; // Set your ElevenLabs API key

    [Header("Audio Settings")]
    [SerializeField] private int sampleRate = 16000;
    [SerializeField] private float chunkSendInterval = 0.1f; // Send every 100ms

    [Header("VAD Settings - For Command Detection")]
    [SerializeField] private float commandPauseThreshold = 0.8f; // Fast commit for commands

    [Header("Voice Indicator (REAL TIME)")]
    public GameObject VoiceIndicater;
    [SerializeField] private float voiceThreshold = 0.015f;
    [SerializeField] private float silenceDelay = 0.25f;
    [SerializeField] private float pulseMultiplier = 4f;

    [Header("Internet Indicator")]
    public GameObject InternetIndicator;

    private float lastVoiceTime;
    private bool isUserSpeaking;

    // Internet monitoring
    private bool hasInternet = true;
    private bool isSendingAudio = false;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI committedTranscriptText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("References")]
    public InspectionCheckList InspectionCheckList;
    public RecordingManager recordingManager;
    public AnnotationManager annotationManager;

    [Space, Header("Call Custom Events On Commands Matched")]
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
    private const int RECORDING_LENGTH = 999;

    private Coroutine internetRoutine;

    // ✅ CRITICAL: Cancellation token to abort async operations instantly
    private CancellationTokenSource cancellationTokenSource;

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
        cancellationTokenSource = new CancellationTokenSource();
        internetRoutine = StartCoroutine(InternetMonitorLoop());
        _ = InitRealtimeSTT();
    }

    private async Task InitRealtimeSTT()
    {
        if (!hasInternet || isDestroyed)
        {
            Debug.Log("[STT] Init cancelled - no internet or destroyed");
            return;
        }

        UpdateStatus("Initializing...");
        Debug.Log("[STT] Getting session token...");

        try
        {
            sessionToken = await GetSessionToken();
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[STT] Token request cancelled");
            return;
        }

        if (isDestroyed || !hasInternet) return;

        if (string.IsNullOrEmpty(sessionToken))
        {
            UpdateStatus("Token failed!");
            Debug.LogError("[STT] Failed to get token!");

            if (!isDestroyed && hasInternet)
            {
                Invoke(nameof(RetryConnection), 3f);
            }
            return;
        }

        Debug.Log("[STT] Token received! Connecting...");
        UpdateStatus("Connecting...");

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
            if (isDestroyed || !hasInternet) return;

            websocket = new WebSocket(wsUrl);

            websocket.OnOpen += () => {
                if (isDestroyed || !hasInternet) return;
                Debug.Log("[STT] ✅ CONNECTED! Ready for commands!");
                UpdateStatus("Ready!");

                if (!isRecording && hasInternet)
                {
                    Debug.Log("[STT] 🎤 Auto-resuming microphone...");
                    Invoke(nameof(StartMicrophone), 0.2f);
                }
            };

            websocket.OnMessage += OnWebSocketMessage;

            websocket.OnError += (e) => {
                if (isDestroyed) return;
                Debug.LogError($"[STT] WebSocket Error: {e}");
                UpdateStatus("Error");
            };

            websocket.OnClose += (e) =>
            {
                if (isDestroyed) return;

                Debug.LogWarning($"[STT] Disconnected! Code: {e}");

                PauseMicrophone();
                isRecording = false;

                if (hasInternet)
                {
                    UpdateStatus("Reconnecting...");
                    CancelInvoke(nameof(RetryConnection));
                    Invoke(nameof(RetryConnection), 1f);
                }
                else
                {
                    UpdateStatus("No Internet");
                }
            };

            await websocket.Connect();
        }
        catch (Exception ex)
        {
            if (isDestroyed) return;
            Debug.LogError($"[STT] Connection failed: {ex.Message}");
            UpdateStatus("Connection failed");

            if (!isDestroyed && hasInternet)
            {
                Invoke(nameof(RetryConnection), 2f);
            }
        }
    }

    private void RetryConnection()
    {
        if (isDestroyed || !hasInternet)
        {
            Debug.Log("[STT] Skipping retry - no internet");
            UpdateStatus("No Internet");
            return;
        }

        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            return;
        }

        Debug.Log("[STT] Retrying connection...");
        _ = InitRealtimeSTT();
    }

    private async Task<string> GetSessionToken()
    {
        if (!hasInternet || isDestroyed)
        {
            Debug.Log("[STT] Skipping token request - no internet");
            return null;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[STT] ❌ API KEY NOT SET!");
            return null;
        }

        using (UnityWebRequest www = new UnityWebRequest(TOKEN_ENDPOINT, "POST"))
        {
            www.timeout = 2; // Very short timeout
            www.SetRequestHeader("xi-api-key", apiKey);
            www.SetRequestHeader("Content-Type", "application/json");

            byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();

            var op = www.SendWebRequest();

            // ✅ Check cancellation token every frame
            while (!op.isDone && !isDestroyed && hasInternet)
            {
                if (cancellationTokenSource.Token.IsCancellationRequested)
                {
                    www.Abort();
                    throw new OperationCanceledException();
                }
                await Task.Yield();
            }

            if (isDestroyed || !hasInternet)
            {
                www.Abort();
                return null;
            }

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
                if (hasInternet)
                {
                    Debug.LogError($"[STT] Token error: {www.error}");
                }
                return null;
            }
        }
    }

    public void StartMicrophone()
    {
        if (isDestroyed) return;

        if (!hasInternet)
        {
            Debug.Log("[STT] ❌ Cannot start microphone - no internet");
            UpdateStatus("No Internet");
            return;
        }

        if (websocket?.State != WebSocketState.Open)
        {
            Debug.Log("[STT] ⏳ Waiting for WebSocket connection...");
            UpdateStatus("Connecting...");
            return;
        }

        if (isRecording)
        {
            Debug.Log("[STT] Already recording");
            return;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[STT] NO MICROPHONE!");
            UpdateStatus("No mic!");
            return;
        }

        string mic = Microphone.devices[0];
        Debug.Log($"[STT] 🎤 Starting mic: {mic}");
        Debug.Log($"[STT] Mode: CONTINUOUS COMMAND LISTENING");

        microphoneClip = Microphone.Start(mic, true, RECORDING_LENGTH, sampleRate);
        lastSamplePosition = 0;
        isRecording = true;
        nextSendTime = Time.time + chunkSendInterval;
        commandCount = 0;

        UpdateStatus("Listening...");
        Debug.Log("[STT] ✅ MICROPHONE ACTIVE - SAY YOUR COMMANDS!");
    }

    private void UpdateVoiceIndicator(float[] samples)
    {
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
            sum += Mathf.Abs(samples[i]);

        float loudness = sum / samples.Length;

        if (loudness > voiceThreshold)
        {
            lastVoiceTime = Time.time;

            if (!isUserSpeaking)
            {
                isUserSpeaking = true;
                if (VoiceIndicater != null)
                    VoiceIndicater.SetActive(true);
            }

            float scale = Mathf.Lerp(0.7f, 1.1f, loudness * pulseMultiplier);
            VoiceIndicater.transform.localScale =
                Vector3.Lerp(VoiceIndicater.transform.localScale,
                Vector3.one * scale,
                Time.deltaTime * 12f);
        }
    }

    private IEnumerator InternetMonitorLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(3f);

        while (!isDestroyed)
        {
            yield return wait;

            UnityWebRequest request = UnityWebRequest.Head("https://clients3.google.com/generate_204");
            request.timeout = 1; // ✅ SUPER SHORT - 1 second max

            var operation = request.SendWebRequest();

            float startTime = Time.time;
            // ✅ Add timeout check to prevent hanging
            while (!operation.isDone && !isDestroyed && (Time.time - startTime) < 1.5f)
            {
                yield return null;
            }

            if (isDestroyed)
            {
                request.Abort();
                request.Dispose();
                yield break;
            }

            // ✅ Force abort if taking too long
            if (!operation.isDone)
            {
                request.Abort();
            }

            bool previousState = hasInternet;
            bool connected = request.result == UnityWebRequest.Result.Success;

            request.Dispose();

            if (connected != hasInternet)
            {
                hasInternet = connected;

                if (InternetIndicator != null)
                    InternetIndicator.SetActive(!hasInternet);

                if (!previousState && hasInternet)
                {
                    Debug.Log("[STT] 🌐 Internet restored");

                    // ✅ Create new cancellation token
                    cancellationTokenSource?.Cancel();
                    cancellationTokenSource?.Dispose();
                    cancellationTokenSource = new CancellationTokenSource();

                    if (websocket != null)
                    {
                        try { websocket.Close(); } catch { }
                        websocket = null;
                    }

                    CancelInvoke(nameof(RetryConnection));
                    Invoke(nameof(RetryConnection), 1f);
                }
                else if (previousState && !hasInternet)
                {
                    Debug.Log("[STT] ❌ Internet lost - ABORTING ALL");
                    UpdateStatus("No Internet");

                    // ✅ CRITICAL: Cancel ALL async operations immediately
                    cancellationTokenSource?.Cancel();

                    // ✅ Stop everything instantly
                    CancelInvoke();

                    if (isRecording)
                    {
                        PauseMicrophone();
                    }

                    // ✅ Force close websocket without waiting
                    if (websocket != null)
                    {
                        try
                        {
                            websocket.Close();
                            websocket = null;
                        }
                        catch { }
                    }
                }
            }
        }
    }

    private void Update()
    {
        if (isDestroyed) return;

        // Only dispatch if connected
        if (hasInternet && websocket != null && websocket.State == WebSocketState.Open)
        {
            try
            {
                websocket.DispatchMessageQueue();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex);
            }
        }

        // Stream audio
        if (hasInternet &&
            isRecording &&
            microphoneClip != null &&
            websocket?.State == WebSocketState.Open &&
            Time.time >= nextSendTime)
        {
            SendAudioChunk();
            nextSendTime = Time.time + chunkSendInterval;
        }

        // Voice indicator
        if (isUserSpeaking && Time.time - lastVoiceTime > silenceDelay)
        {
            isUserSpeaking = false;
            if (VoiceIndicater != null)
                VoiceIndicater.SetActive(false);
        }
    }

    public void PauseMicrophone()
    {
        if (isRecording && Microphone.IsRecording(null))
        {
            Microphone.End(null);
            isRecording = false;
            Debug.Log("[STT] Microphone paused");
        }
    }

    public void ResumeMicrophone()
    {
        if (!isRecording && hasInternet && websocket?.State == WebSocketState.Open)
        {
            StartMicrophone();
        }
    }

    private async void SendAudioChunk()
    {
        if (isDestroyed || !hasInternet || isSendingAudio) return;
        if (websocket == null || websocket.State != WebSocketState.Open) return;

        isSendingAudio = true;

        try
        {
            int pos = Microphone.GetPosition(null);
            if (pos < 0) return;

            if (pos < lastSamplePosition)
                pos += microphoneClip.samples;

            int toRead = pos - lastSamplePosition;

            int maxSamples = sampleRate / 2;
            if (toRead > maxSamples)
            {
                lastSamplePosition = pos;
                return;
            }

            if (toRead <= 0) return;

            float[] samples = new float[toRead];
            int startPos = lastSamplePosition % microphoneClip.samples;
            microphoneClip.GetData(samples, startPos);

            UpdateVoiceIndicator(samples);

            byte[] pcm = ConvertToPCM16(samples);
            lastSamplePosition = pos;

            if (!hasInternet) return;

            var message = new InputAudioChunkMessage
            {
                message_type = "input_audio_chunk",
                audio_base_64 = Convert.ToBase64String(pcm),
                sample_rate = sampleRate
            };

            await websocket.SendText(JsonUtility.ToJson(message));
        }
        catch
        {
            // Silent - expected on disconnect
        }
        finally
        {
            isSendingAudio = false;
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
                    Debug.Log($"[STT] SESSION ACTIVE: {sessionId}");
                    UpdateStatus("Ready for commands!");
                    StartMicrophone();
                    break;

                case "partial_transcript":
                    break;

                case "committed_transcript":
                case "committed_transcript_with_timestamps":
                    if (!string.IsNullOrEmpty(evt.text))
                    {
                        Debug.Log($"Heard: " + evt.text);

                        allCommittedText.AppendLine(evt.text);

                        if (committedTranscriptText != null)
                        {
                            committedTranscriptText.text = "Listening:- " + evt.text;
                            Debug.Log($"[STT] Updated UI with text: {allCommittedText.ToString()}");
                        }

                        UpdateStatus($"Listening ({commandCount})");

                        ProcessCommand(evt.text);
                        if (VoiceIndicater != null && VoiceIndicater.activeSelf)
                            VoiceIndicater.SetActive(false);
                    }
                    break;

                case "scribe_error":
                case "scribe_auth_error":
                case "scribe_quota_exceeded_error":
                case "scribe_throttled_error":
                case "input_error":
                    var errorEvt = JsonUtility.FromJson<ScribeError>(msg);
                    string errorMsg = !string.IsNullOrEmpty(errorEvt.error_message) ? errorEvt.error_message : errorEvt.error;
                    Debug.LogError($"[STT] Error: {errorMsg}");
                    UpdateStatus($" {errorMsg}");
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

    private void ProcessCommand(string command)
    {
        command = CleanCommand(command);

        if (InspectionCheckList != null)
            InspectionCheckList.ProcessCommand(command);

        if (recordingManager != null)
            recordingManager.ProcessCommand(command);

        if (annotationManager != null)
            annotationManager.ProcessCommand(command);

        foreach (var data in CommandData)
        {
            foreach (var cmd in data.commands)
            {
                if (string.IsNullOrEmpty(cmd)) continue;

                string lowerCmd = cmd.ToLower();

                if (command.Contains(lowerCmd))
                {
                    data.Event?.Invoke();
                    return;
                }
            }
        }
    }

    private string CleanCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return "";

        command = command.ToLower();

        command = command.Replace(".", "")
                         .Replace(",", "")
                         .Replace("!", "")
                         .Replace("?", "");

        command = System.Text.RegularExpressions.Regex.Replace(command, @"\s+", " ");

        return command.Trim();
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

        // ✅ Cancel all async operations
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();

        CancelInvoke();

        if (internetRoutine != null)
        {
            StopCoroutine(internetRoutine);
        }

        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
            Debug.Log("[STT] Microphone stopped");
        }

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