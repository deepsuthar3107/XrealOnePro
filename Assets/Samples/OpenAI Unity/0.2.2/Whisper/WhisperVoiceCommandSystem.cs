using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Events;

[System.Serializable]
public class VoiceCommand
{
    public string commandName;
    public List<string> commandKeywords = new List<string>();
    public UnityEvent onCommandRecognized;
}

[System.Serializable]
public class CommandGroup
{
    public string groupName;
    public List<VoiceCommand> commands = new List<VoiceCommand>();
}

public class WhisperVoiceCommandSystem : MonoBehaviour
{
    [Header("Whisper API Settings")]
    [SerializeField] private string openAIApiKey = "your-api-key-here";
    [SerializeField] private string whisperModel = "whisper-1";
    [SerializeField] private bool debugMode = true;

    [Header("Voice Detection Settings")]
    [SerializeField] private float voiceThreshold = 0.02f; // Kitni volume pe recording start ho
    [SerializeField] private float silenceThreshold = 0.01f; // Kitni volume pe silence detect ho
    [SerializeField] private float silenceDuration = 1.5f; // Kitni der silence ke baad process kare
    [SerializeField] private float minRecordingDuration = 0.5f; // Minimum recording time
    [SerializeField] private float maxRecordingDuration = 10f; // Maximum recording time
    [SerializeField] private int sampleRate = 16000;

    [Header("Command Groups")]
    [SerializeField] private List<CommandGroup> commandGroups = new List<CommandGroup>();

    [Header("UI References (Optional)")]
    [SerializeField] private TMPro.TextMeshProUGUI statusText;
    [SerializeField] private TMPro.TextMeshProUGUI debugText;

    private AudioClip microphoneClip;
    private bool isProcessing = false;
    private bool isRecordingVoice = false;
    private float silenceTimer = 0f;
    private float recordingTimer = 0f;

    private List<float> recordedSamples = new List<float>();
    private int lastMicPosition = 0;
    private float[] tempBuffer;

    void Start()
    {
        // Check microphone
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("❌ No microphone detected!");
            UpdateStatus("No microphone found");
            return;
        }

        Debug.Log($"✅ Microphone found: {Microphone.devices[0]}");
        tempBuffer = new float[1024];

        // Start continuous microphone
        StartMicrophone();
        UpdateStatus("🎤 Ready - Start speaking...");
    }

    void StartMicrophone()
    {
        // 30 second loop buffer
        microphoneClip = Microphone.Start(null, true, 30, sampleRate);
        lastMicPosition = 0;

        if (debugMode)
        {
            Debug.Log("🎤 Microphone started - Waiting for voice...");
        }
    }

    void Update()
    {
        if (microphoneClip == null || isProcessing) return;

        // Get current microphone position
        int currentPos = Microphone.GetPosition(null);
        if (currentPos < 0 || currentPos == lastMicPosition) return;

        // Calculate how many samples to read
        int samplesToRead = currentPos - lastMicPosition;
        if (samplesToRead < 0)
        {
            samplesToRead = microphoneClip.samples - lastMicPosition + currentPos;
        }

        // Read in chunks
        while (samplesToRead > 0)
        {
            int chunkSize = Mathf.Min(samplesToRead, tempBuffer.Length);
            microphoneClip.GetData(tempBuffer, lastMicPosition);

            // Calculate volume
            float volume = GetAverageVolume(tempBuffer, chunkSize);

            // Voice Activity Detection
            if (volume > voiceThreshold)
            {
                // Voice detected!
                if (!isRecordingVoice)
                {
                    StartRecording();
                }

                // Add to recording
                for (int i = 0; i < chunkSize; i++)
                {
                    recordedSamples.Add(tempBuffer[i]);
                }

                silenceTimer = 0f;
                recordingTimer += (float)chunkSize / sampleRate;
            }
            else if (isRecordingVoice && volume < silenceThreshold)
            {
                // Silence detected
                silenceTimer += (float)chunkSize / sampleRate;

                // Add silence to recording
                for (int i = 0; i < chunkSize; i++)
                {
                    recordedSamples.Add(tempBuffer[i]);
                }

                recordingTimer += (float)chunkSize / sampleRate;
            }

            lastMicPosition = (lastMicPosition + chunkSize) % microphoneClip.samples;
            samplesToRead -= chunkSize;
        }

        // Check if we should stop recording
        if (isRecordingVoice)
        {
            // Stop if silence duration exceeded
            if (silenceTimer >= silenceDuration && recordingTimer >= minRecordingDuration)
            {
                StopRecordingAndProcess();
            }
            // Stop if max duration reached
            else if (recordingTimer >= maxRecordingDuration)
            {
                StopRecordingAndProcess();
            }

            // Update status
            UpdateStatus($"🔴 Recording... ({recordingTimer:F1}s)");
        }
    }

    void StartRecording()
    {
        isRecordingVoice = true;
        recordedSamples.Clear();
        silenceTimer = 0f;
        recordingTimer = 0f;

        UpdateStatus("🔴 Recording...");

        if (debugMode)
        {
            Debug.Log("🔴 Voice detected - Started recording");
        }
    }

    void StopRecordingAndProcess()
    {
        if (!isRecordingVoice) return;

        isRecordingVoice = false;

        if (debugMode)
        {
            Debug.Log($"⏹️ Recording stopped - Duration: {recordingTimer:F2}s, Samples: {recordedSamples.Count}");
        }

        // Check if recording is long enough
        if (recordingTimer < minRecordingDuration)
        {
            if (debugMode)
            {
                Debug.Log("⚠️ Recording too short, skipping...");
            }
            recordedSamples.Clear();
            UpdateStatus("🎤 Ready - Start speaking...");
            return;
        }

        // Create AudioClip from recorded samples
        AudioClip recordedClip = AudioClip.Create("recorded", recordedSamples.Count, 1, sampleRate, false);
        recordedClip.SetData(recordedSamples.ToArray(), 0);

        // Process with Whisper
        StartCoroutine(ProcessAudioWithWhisper(recordedClip));

        recordedSamples.Clear();
    }

    float GetAverageVolume(float[] samples, int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            sum += Mathf.Abs(samples[i]);
        }
        return sum / count;
    }

    private IEnumerator ProcessAudioWithWhisper(AudioClip clip)
    {
        isProcessing = true;
        UpdateStatus("🔄 Processing...");

        if (debugMode)
        {
            Debug.Log($"📤 Sending to Whisper API...");
        }

        // Convert AudioClip to WAV
        byte[] wavData = ConvertAudioClipToWav(clip);

        // Create form data
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        formData.Add(new MultipartFormDataSection("model", whisperModel));
        formData.Add(new MultipartFormFileSection("file", wavData, "audio.wav", "audio/wav"));
        formData.Add(new MultipartFormDataSection("language", "en"));

        // Create request
        UnityWebRequest request = UnityWebRequest.Post("https://api.openai.com/v1/audio/transcriptions", formData);
        request.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);

        // Send request
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string jsonResponse = request.downloadHandler.text;

            // Parse response
            WhisperResponse response = JsonUtility.FromJson<WhisperResponse>(jsonResponse);

            if (!string.IsNullOrEmpty(response.text))
            {
                string recognizedText = response.text.Trim();

                // Show in UI immediately
                UpdateDebugText($"🎙️ \"{recognizedText}\"");
                Debug.Log($"✅ Recognized: {recognizedText}");

                // Match and execute command
                ProcessCommand(recognizedText.ToLower());
            }
            else
            {
                if (debugMode)
                {
                    Debug.Log("⚠️ Empty response from Whisper");
                }
            }
        }
        else
        {
            Debug.LogError($"❌ Whisper API Error: {request.error}");
            Debug.LogError($"Response Code: {request.responseCode}");
            if (!string.IsNullOrEmpty(request.downloadHandler.text))
            {
                Debug.LogError($"Response: {request.downloadHandler.text}");
            }
            UpdateDebugText($"Error: {request.error}");
        }

        UpdateStatus("🎤 Ready - Start speaking...");
        isProcessing = false;
    }

    private void ProcessCommand(string recognizedText)
    {
        bool commandFound = false;

        foreach (CommandGroup group in commandGroups)
        {
            foreach (VoiceCommand command in group.commands)
            {
                foreach (string keyword in command.commandKeywords)
                {
                    if (recognizedText.Contains(keyword.ToLower()))
                    {
                        Debug.Log($"✅ Command matched: {command.commandName}");
                        command.onCommandRecognized?.Invoke();
                        commandFound = true;
                        UpdateDebugText($"✅ {command.commandName}");
                        break;
                    }
                }
                if (commandFound) break;
            }
            if (commandFound) break;
        }

        if (!commandFound && debugMode)
        {
            Debug.Log($"⚠️ No command matched for: {recognizedText}");
        }
    }

    private byte[] ConvertAudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        short[] intData = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * 32767);
        }

        int frequency = clip.frequency;
        int channels = clip.channels;

        byte[] wavData = new byte[44 + intData.Length * 2];

        // WAV header
        int sampleCount = intData.Length;
        int byteRate = frequency * channels * 2;

        // RIFF chunk
        System.Text.Encoding.UTF8.GetBytes("RIFF").CopyTo(wavData, 0);
        BitConverter.GetBytes(36 + sampleCount * 2).CopyTo(wavData, 4);
        System.Text.Encoding.UTF8.GetBytes("WAVE").CopyTo(wavData, 8);

        // fmt chunk
        System.Text.Encoding.UTF8.GetBytes("fmt ").CopyTo(wavData, 12);
        BitConverter.GetBytes(16).CopyTo(wavData, 16);
        BitConverter.GetBytes((short)1).CopyTo(wavData, 20);
        BitConverter.GetBytes((short)channels).CopyTo(wavData, 22);
        BitConverter.GetBytes(frequency).CopyTo(wavData, 24);
        BitConverter.GetBytes(byteRate).CopyTo(wavData, 28);
        BitConverter.GetBytes((short)(channels * 2)).CopyTo(wavData, 32);
        BitConverter.GetBytes((short)16).CopyTo(wavData, 34);

        // data chunk
        System.Text.Encoding.UTF8.GetBytes("data").CopyTo(wavData, 36);
        BitConverter.GetBytes(sampleCount * 2).CopyTo(wavData, 40);

        // Audio data
        for (int i = 0; i < intData.Length; i++)
        {
            byte[] bytes = BitConverter.GetBytes(intData[i]);
            wavData[44 + i * 2] = bytes[0];
            wavData[44 + i * 2 + 1] = bytes[1];
        }

        return wavData;
    }

    private void UpdateStatus(string status)
    {
        if (statusText != null)
            statusText.text = status;
    }

    private void UpdateDebugText(string text)
    {
        if (debugText != null)
            debugText.text = text;
    }

    void OnDestroy()
    {
        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
        }
    }

    void OnApplicationQuit()
    {
        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
        }
    }

    [System.Serializable]
    private class WhisperResponse
    {
        public string text;
    }
}