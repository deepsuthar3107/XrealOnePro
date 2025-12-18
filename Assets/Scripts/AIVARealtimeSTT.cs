using NativeWebSocket;
using System;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class AIVARealtimeSTT : MonoBehaviour
{
    [Header("API Config")]
    [SerializeField] private string apiKey = "YOUR_API_KEY";
    [SerializeField] private string secretKey = "YOUR_SECRET_KEY";

    [Header("Audio Settings")]
    [SerializeField] private int sampleRate = 16000;
    [SerializeField] private float chunkSendInterval = 0.016f; // 16ms for lower latency

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI finalTranscriptText; // Committed/final commands only

    [Header("References")]
    public InspectionCheckList InspectionCheckList;
    public RecordingManager recordingManager;
    public AnnotationManager annotationManager;

    [Space, Header("Call Custom Events On Commands Matched")]
    public CommandData[] CommandData;

    // Core state
    private AudioClip microphoneClip;
    private int lastSamplePosition = 0;
    private bool isRecording = false;
    private float nextSendTime = 0f;
    private bool isDestroyed = false;

    // Networking
    private string accessToken;
    private WebSocket websocket;
    private bool isReconnecting = false;

    // Transcription
    private string currentLiveTranscript = "";
    private int commandCount = 0;

    // Constants
    private const string TOKEN_ENDPOINT = "https://coe-api.truvideo.co.uk/api/v1/application/generate-websocket-access-token";
    private const string WS_BASE_URL = "wss://coe-socket.truvideo.co.uk";

    // Audio processing - optimized buffers
    private int samplesPerFrame;
    private float[] audioBuffer;
    private byte[] pcmBuffer;
    private const int BUFFER_MULTIPLIER = 8; // Smaller buffer for lower latency

    private void Start()
    {
        samplesPerFrame = sampleRate / 60; // Smaller frames (16.6ms) for lower latency
        audioBuffer = new float[samplesPerFrame * BUFFER_MULTIPLIER];
        pcmBuffer = new byte[samplesPerFrame * BUFFER_MULTIPLIER * 2];

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
        if (isDestroyed || isReconnecting) return;
        isReconnecting = true;

        UpdateStatus("Initializing");
        accessToken = await GetAccessToken();

        if (isDestroyed || string.IsNullOrEmpty(accessToken))
        {
            UpdateStatus("Token Failed");
            isReconnecting = false;
            Invoke(nameof(RetryConnection), 3f);
            return;
        }

        UpdateStatus("Connecting");
        string wsUrl = $"{WS_BASE_URL}?accessToken={Uri.EscapeDataString(accessToken)}&engine=google&sampleRate={sampleRate}";

        try
        {
            if (isDestroyed) return;

            websocket = new WebSocket(wsUrl);

            websocket.OnOpen += () =>
            {
                if (isDestroyed) return;
                UpdateStatus("Connected");
                isReconnecting = false;
            };

            websocket.OnMessage += OnWebSocketMessage;

            websocket.OnError += (e) =>
            {
                if (isDestroyed) return;
                UpdateStatus("Error");
                isReconnecting = false;
            };

            websocket.OnClose += (e) =>
            {
                if (isDestroyed) return;
                UpdateStatus("Reconnecting");
                isRecording = false;
                isReconnecting = false;
                Invoke(nameof(RetryConnection), 1f);
            };

            await websocket.Connect();
        }
        catch (Exception)
        {
            if (isDestroyed) return;
            UpdateStatus("Failed");
            isReconnecting = false;
            Invoke(nameof(RetryConnection), 2f);
        }
    }

    private void RetryConnection()
    {
        if (isDestroyed || isReconnecting) return;
        if (websocket != null && websocket.State == WebSocketState.Open) return;
        _ = InitRealtimeSTT();
    }

    private async Task<string> GetAccessToken()
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
            return null;

        using (UnityWebRequest www = new UnityWebRequest(TOKEN_ENDPOINT, "POST"))
        {
            www.SetRequestHeader("Content-Type", "application/json");

            var requestBody = new TokenRequest { key = apiKey, secret = secretKey };
            byte[] bodyRaw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody));
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();

            var op = www.SendWebRequest();
            while (!op.isDone && !isDestroyed) await Task.Yield();
            if (isDestroyed) return null;

#if UNITY_2020_1_OR_NEWER
            if (www.result == UnityWebRequest.Result.Success)
#else
            if (!www.isNetworkError && !www.isHttpError)
#endif
            {
                var response = JsonUtility.FromJson<TokenResponse>(www.downloadHandler.text);
                return response?.responseData?.token?.accessToken;
            }
            return null;
        }
    }

    public void StartMicrophone()
    {
        if (isDestroyed || isRecording || Microphone.devices.Length == 0) return;

        microphoneClip = Microphone.Start(Microphone.devices[0], true, 10, sampleRate);
        lastSamplePosition = 0;
        _ = WaitForMicStart();
    }

    private async Task WaitForMicStart()
    {
        int timeout = 0;
        while (Microphone.GetPosition(null) <= 0 && timeout < 50 && !isDestroyed)
        {
            timeout++;
            await Task.Delay(100);
        }

        if (Microphone.GetPosition(null) > 0)
            isRecording = true;
    }

    public void StopMicrophone()
    {
        if (!isRecording) return;
        isRecording = false;
        if (Microphone.IsRecording(null)) Microphone.End(null);
        UpdateStatus("Stopped");
    }

    private void Update()
    {
        if (isDestroyed) return;

        // Process WebSocket messages immediately
        websocket?.DispatchMessageQueue();

        // Send audio chunks at optimized intervals
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

        // Handle microphone position wrapping
        int actualPos = pos < lastSamplePosition ? pos + microphoneClip.samples : pos;
        int toRead = actualPos - lastSamplePosition;
        if (toRead < samplesPerFrame) return;

        // Send multiple frames at once if available (reduces overhead)
        int framesToSend = Mathf.Min(toRead / samplesPerFrame, BUFFER_MULTIPLIER);
        int samplesToSend = framesToSend * samplesPerFrame;

        // Dynamic buffer resizing if needed
        if (samplesToSend > audioBuffer.Length)
        {
            audioBuffer = new float[samplesToSend];
            pcmBuffer = new byte[samplesToSend * 2];
        }

        int startPos = lastSamplePosition % microphoneClip.samples;
        microphoneClip.GetData(audioBuffer, startPos);

        // Inline PCM conversion for speed
        int byteCount = samplesToSend * 2;
        for (int i = 0; i < samplesToSend; i++)
        {
            short val = (short)(Mathf.Clamp(audioBuffer[i], -1f, 1f) * 32767f);
            int idx = i * 2;
            pcmBuffer[idx] = (byte)(val & 0xFF);
            pcmBuffer[idx + 1] = (byte)((val >> 8) & 0xFF);
        }

        lastSamplePosition = (lastSamplePosition + samplesToSend) % microphoneClip.samples;

        // Send without creating new array
        await websocket.Send(new ArraySegment<byte>(pcmBuffer, 0, byteCount).ToArray());
    }

    private void OnWebSocketMessage(byte[] bytes)
    {
        if (isDestroyed) return;

        try
        {
            var evt = JsonUtility.FromJson<AIVAEvent>(Encoding.UTF8.GetString(bytes));
            if (evt == null || string.IsNullOrEmpty(evt.@event)) return;

            switch (evt.@event)
            {
                case "stt_started":
                    UpdateStatus("Listening");
                    StartMicrophone();
                    break;

                case "transcript":
                    if (evt.data != null)
                    {
                        // Update live transcript immediately
                        currentLiveTranscript = evt.data.transcript;
                        UpdateLiveTranscript(currentLiveTranscript);

                        // Process final transcript
                        if (evt.data.isFinal)
                        {
                            commandCount++;
                            UpdateFinalTranscript(evt.data.transcript);
                            ProcessCommand(evt.data.transcript);

                            // Clear live transcript after finalizing
                            currentLiveTranscript = "";
                            UpdateLiveTranscript("");
                        }
                    }
                    break;

                case "stt_queued":
                    UpdateStatus("Queued");
                    break;
            }
        }
        catch { /* Ignore malformed messages */ }
    }

    private void ProcessCommand(string command)
    {
        command = CleanCommand(command);

        InspectionCheckList?.ProcessCommand(command);
        recordingManager?.ProcessCommand(command);
        annotationManager?.ProcessCommand(command);

        // Check custom commands
        foreach (var data in CommandData)
        {
            if (data.commands == null) continue;

            foreach (var cmd in data.commands)
            {
                if (!string.IsNullOrEmpty(cmd) && command.Contains(cmd.ToLower()))
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
        command = command.ToLower()
            .Replace(".", "")
            .Replace(",", "")
            .Replace("!", "")
            .Replace("?", "")
            .Replace("  ", " ")
            .Trim();
        return command;
    }

    private void UpdateLiveTranscript(string text)
    {
       
    }

    private void UpdateFinalTranscript(string text)
    {
        if (finalTranscriptText != null)
            finalTranscriptText.text = text;
    }

    private void UpdateStatus(string status)
    {
        
    }

    private void OnDestroy()
    {
        isDestroyed = true;
        isRecording = false;
        CancelInvoke();
        if (Microphone.IsRecording(null)) Microphone.End(null);
        if (websocket != null && websocket.State == WebSocketState.Open)
            _ = websocket.Close();
    }

    private void OnApplicationQuit() => isDestroyed = true;

    // JSON Classes
    [Serializable] private class TokenRequest { public string key; public string secret; }
    [Serializable] private class TokenResponse { public ResponseData responseData; }
    [Serializable] private class ResponseData { public TokenData token; }
    [Serializable] private class TokenData { public string accessToken; }
    [Serializable] private class AIVAEvent { public string @event; public EventData data; }
    [Serializable] private class EventData { public string transcript; public bool isFinal; public bool speaking; }
}