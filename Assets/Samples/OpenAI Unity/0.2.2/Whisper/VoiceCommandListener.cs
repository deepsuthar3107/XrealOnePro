using OpenAI; // adjust if your OpenAI unity SDK namespace differs
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Events;

[Serializable]
public class CommandData
{
    public string CommandGroupName;
    public string[] commands;
    public UnityEvent Event;
}

public class VoiceCommandListener : MonoBehaviour
{
    [Header("OpenAI (Whisper)")]
    [Tooltip("Set the API key securely in Inspector or load at runtime.")]
    public string apiKey = "YOUR_API_KEY";

    [Header("Commands")]
    public CommandData[] commandDatabase;

    [Header("Microphone / Chunking (latency vs cost)")]
    [Tooltip("Chunk length in seconds (shorter => lower latency, more API calls).")]
    public float chunkSeconds = 0.5f;
    [Range(0f, 0.9f)]
    public float overlapFraction = 0.5f;
    public int sampleRate = 16000;
    [Tooltip("Leave empty to auto-select first microphone")]
    public string micDeviceName = "";

    [Header("Auto-calibration")]
    public bool autoCalibrateOnStart = true;
    [Tooltip("Seconds to sample ambient noise during calibration.")]
    public float calibrationSeconds = 2.0f;

    [Header("Noise gate / VAD")]
    [Tooltip("Multiplier applied to calibrated RMS to set speech threshold.")]
    public float silenceThresholdMultiplier = 1.6f;
    [Tooltip("Minimum fraction of frames in a chunk that must be above threshold to count as speech.")]
    [Range(0f, 1f)]
    public float minSpeechFraction = 0.08f;

    [Header("Noise Removal (spectral subtraction)")]
    public bool enableNoiseRemoval = true;
    [Tooltip("FFT window size (power of two).")]
    public int fftWindowSize = 1024;
    [Range(0f, 2f)]
    public float spectralSubtractBias = 1.0f;

    [Header("Whisper language")]
    [Tooltip("Leave empty to let Whisper auto-detect language.")]
    public string language = "";

    // Minimum audio length to send to Whisper (seconds). Whisper requires >= 0.1s.
    const float MIN_SEC_TO_SEND = 0.12f;

    // Internals
    private OpenAIApi openai;
    private AudioClip micClip;
    private bool isListening = false;
    private bool shuttingDown = false;

    private int samplesPerChunk;
    private int hopSize;
    private int fftN;

    // calibration
    private float calibratedRms = 1e-6f;
    private float[] calibratedNoiseSpectrum;

    void Awake()
    {

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
        // enforce power-of-two fft
        if (!Mathf.IsPowerOfTwo(fftWindowSize))
            fftWindowSize = Mathf.ClosestPowerOfTwo(fftWindowSize);

        fftN = fftWindowSize;
        samplesPerChunk = Mathf.Max(128, Mathf.CeilToInt(sampleRate * chunkSeconds));
        hopSize = Mathf.Max(1, Mathf.CeilToInt(samplesPerChunk * (1f - overlapFraction)));

        // init OpenAI (SDK-specific initialization)
        openai = new OpenAIApi(apiKey);
    }

    IEnumerator Start()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[VoiceCommand] No microphone devices found.");
            yield break;
        }

        if (string.IsNullOrEmpty(micDeviceName))
            micDeviceName = Microphone.devices[0];

        micClip = Microphone.Start(micDeviceName, true, 999, sampleRate);

        // wait for mic to get first samples (safe)
        float startWait = Time.time;
        while (Microphone.GetPosition(micDeviceName) <= 0)
        {
            if (Time.time - startWait > 3f)
            {
                Debug.LogWarning("[VoiceCommand] Microphone start timeout after 3s.");
                break;
            }
            yield return null;
        }

        if (autoCalibrateOnStart)
            yield return StartCoroutine(CalibrateAmbientNoiseSafely());

        isListening = true;
        StartCoroutine(CaptureLoop());
        Debug.Log("[VoiceCommand] Started listening — chunkSeconds=" + chunkSeconds + " hopSize=" + hopSize);
    }

    // -----------------------------
    // Calibration (safe; no yield in catch)
    // -----------------------------
    IEnumerator CalibrateAmbientNoiseSafely()
    {
        Debug.Log("[VoiceCommand] Calibrating for " + calibrationSeconds + "s...");

        float end = Time.time + Mathf.Max(0.5f, calibrationSeconds);
        List<float> rmsVals = new List<float>();
        int safety = 0;

        while (Time.time < end && safety < 200)
        {
            int pos = Microphone.GetPosition(micDeviceName);
            if (pos <= 0)
            {
                yield return new WaitForSeconds(0.05f);
                safety++;
                continue;
            }

            int sampleCount = Mathf.Min(sampleRate / 8, pos); // ~0.125s
            if (sampleCount <= 0)
            {
                yield return new WaitForSeconds(0.05f);
                safety++;
                continue;
            }

            float[] buff = new float[sampleCount];
            int readStart = pos - sampleCount;
            if (readStart < 0) readStart += micClip.samples;

            bool readOK = false;
            try
            {
                if (readStart + sampleCount <= micClip.samples)
                {
                    micClip.GetData(buff, readStart);
                }
                else
                {
                    int first = micClip.samples - readStart;
                    float[] p1 = new float[first];
                    micClip.GetData(p1, readStart);
                    float[] p2 = new float[sampleCount - first];
                    micClip.GetData(p2, 0);
                    Array.Copy(p1, 0, buff, 0, first);
                    Array.Copy(p2, 0, buff, first, p2.Length);
                }
                readOK = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VoiceCommand] Calibration read failed: " + ex.Message);
                readOK = false;
            }

            if (!readOK)
            {
                yield return new WaitForSeconds(0.05f);
                safety++;
                continue;
            }

            float rms = ComputeRMS(buff);
            rmsVals.Add(rms);

            yield return new WaitForSeconds(0.05f);
            safety++;
        }

        calibratedRms = (rmsVals.Count > 0) ? Mathf.Max(1e-6f, rmsVals.Average()) : 1e-6f;
        Debug.Log($"[VoiceCommand] Calibration done. RMS={calibratedRms:F6}");

        if (enableNoiseRemoval)
        {
            int want = Mathf.Clamp(Mathf.CeilToInt(sampleRate * Mathf.Max(0.5f, calibrationSeconds)), fftN, fftN * 8);
            float[] big = ReadRecentSamplesSafe(want);
            calibratedNoiseSpectrum = ComputeAverageNoiseSpectrum(big, fftN);
            Debug.Log("[VoiceCommand] Noise spectrum computed length=" + (calibratedNoiseSpectrum?.Length ?? 0));
        }
    }

    float[] ReadRecentSamplesSafe(int count)
    {
        float[] outArr = new float[count];
        int pos = Microphone.GetPosition(micDeviceName);
        if (pos < 0) return outArr;

        int start = pos - count;
        if (start < 0) start += micClip.samples;

        try
        {
            if (start + count <= micClip.samples)
            {
                micClip.GetData(outArr, start);
            }
            else
            {
                int first = micClip.samples - start;
                float[] p1 = new float[first];
                micClip.GetData(p1, start);
                float[] p2 = new float[count - first];
                micClip.GetData(p2, 0);
                Array.Copy(p1, 0, outArr, 0, first);
                Array.Copy(p2, 0, outArr, first, p2.Length);
            }
        }
        catch
        {
            return new float[count];
        }

        return outArr;
    }

    // -----------------------------
    // Capture loop: overlapping chunks
    // -----------------------------
    IEnumerator CaptureLoop()
    {
        int lastPos = Microphone.GetPosition(micDeviceName);
        if (lastPos < 0) lastPos = 0;

        while (isListening && !shuttingDown)
        {
            int pos = Microphone.GetPosition(micDeviceName);
            if (pos < 0)
            {
                yield return null;
                continue;
            }

            int available = (pos - lastPos + micClip.samples) % micClip.samples;
            if (available >= hopSize)
            {
                // build a chunk window ending at pos
                float[] window = new float[samplesPerChunk];
                int windowStart = pos - samplesPerChunk;
                if (windowStart < 0) windowStart += micClip.samples;

                if (!SafeGetData(window, windowStart))
                {
                    lastPos = pos;
                    yield return null;
                    continue;
                }

                lastPos = (lastPos + hopSize) % micClip.samples;

                // Voice activity detection
                if (!IsSpeechInWindow(window))
                {
                    // skip: silent or noise
                    yield return null;
                    continue;
                }

                // optional denoise
                float[] processed = window;
                if (enableNoiseRemoval && calibratedNoiseSpectrum != null)
                    processed = SpectralSubtractDenoise(window);

                // ensure minimum length before sending
                int minSamples = Mathf.CeilToInt(sampleRate * MIN_SEC_TO_SEND);
                if (processed.Length < minSamples)
                {
                    // If chunk is too short (shouldn't happen given chunkSeconds), skip sending
                    yield return null;
                    continue;
                }

                // convert to WAV bytes
                byte[] wav = EncodeToWav(processed, sampleRate);

                // final check: WAV duration
                float dur = (float)processed.Length / sampleRate;
                if (dur < MIN_SEC_TO_SEND)
                {
                    yield return null;
                    continue;
                }

                // non-blocking send
                StartCoroutine(SendToWhisperAndHandle(wav));
            }
            else
            {
                yield return null;
            }
        }
    }

    bool SafeGetData(float[] outBuffer, int readStart)
    {
        try
        {
            if (readStart + outBuffer.Length <= micClip.samples)
            {
                micClip.GetData(outBuffer, readStart);
            }
            else
            {
                int first = micClip.samples - readStart;
                float[] p1 = new float[first];
                micClip.GetData(p1, readStart);
                float[] p2 = new float[outBuffer.Length - first];
                micClip.GetData(p2, 0);
                Array.Copy(p1, 0, outBuffer, 0, first);
                Array.Copy(p2, 0, outBuffer, first, p2.Length);
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VoiceCommand] SafeGetData failed: " + ex.Message);
            return false;
        }
    }

    bool IsSpeechInWindow(float[] window)
    {
        int frame = Mathf.Max(64, sampleRate / 100); // ~10ms frames
        int frames = window.Length / frame;
        if (frames <= 0) return false;

        int above = 0;
        float threshold = calibratedRms * silenceThresholdMultiplier;
        if (threshold <= 0) threshold = 1e-6f;

        for (int i = 0; i < frames; i++)
        {
            int offset = i * frame;
            double sum = 0;
            for (int j = 0; j < frame; j++)
            {
                float s = window[offset + j];
                sum += s * s;
            }
            float rms = Mathf.Sqrt((float)(sum / frame));
            if (rms >= threshold) above++;
        }

        float frac = above / (float)frames;
        return frac >= minSpeechFraction;
    }

    // -----------------------------
    // Send to Whisper & handle
    // -----------------------------
    IEnumerator SendToWhisperAndHandle(byte[] wavData)
    {
        // final size/duration checks
        int minSamples = Mathf.CeilToInt(sampleRate * MIN_SEC_TO_SEND);
        int samplesInWav = wavData.Length / 2; // approx (PCM16)
        float durationSec = (float)samplesInWav / sampleRate;
        if (durationSec < MIN_SEC_TO_SEND)
        {
            Debug.Log("[VoiceCommand] Chunk too short to send: " + durationSec.ToString("F3") + "s");
            yield break;
        }

        var req = new CreateAudioTranscriptionsRequest
        {
            FileData = new FileData { Data = wavData, Name = "audio.wav" },
            Model = "whisper-1",
            Language = string.IsNullOrEmpty(language) ? null : language
        };

        var task = openai.CreateAudioTranscription(req);
        while (!task.IsCompleted) yield return null;

        if (task.Exception != null)
        {
            Debug.LogWarning("[VoiceCommand] Whisper request failed: " + task.Exception);
            yield break;
        }

        string text = task.Result.Text?.Trim();
        if (string.IsNullOrEmpty(text)) yield break;

        Debug.Log("[VoiceCommand] Transcribed: " + text);
        DetectCommands(text.ToLowerInvariant());
    }

    void DetectCommands(string msg)
    {
        if (commandDatabase == null || commandDatabase.Length == 0) return;

        foreach (var cmd in commandDatabase)
        {
            if (cmd == null || cmd.commands == null) continue;
            foreach (var phrase in cmd.commands)
            {
                if (string.IsNullOrWhiteSpace(phrase)) continue;
                if (IsMatch(msg, phrase.ToLowerInvariant()))
                {
                    Debug.Log("[VoiceCommand] Command matched: " + cmd.CommandGroupName + " (" + phrase + ")");
                    try { cmd.Event?.Invoke(); } catch (Exception ex) { Debug.LogWarning("[VoiceCommand] Event invoke failed: " + ex.Message); }
                    return;
                }
            }
        }
    }

    // fuzzy match: substring or partial-word threshold
    bool IsMatch(string user, string keyword)
    {
        if (user.Contains(keyword)) return true;
        var uw = user.Split(new[] { ' ', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var kw = keyword.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int matches = 0;
        foreach (var k in kw) if (uw.Contains(k)) matches++;
        return matches >= Mathf.CeilToInt(kw.Length * 0.6f);
    }

    // -----------------------------
    // Spectral subtraction denoise
    // -----------------------------
    float[] SpectralSubtractDenoise(float[] frame)
    {
        int N = fftN;
        float[] windowed = new float[N];
        int len = Mathf.Min(frame.Length, N);
        for (int i = 0; i < len; i++)
        {
            float w = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (N - 1)));
            windowed[i] = frame[i] * w;
        }

        Complex[] spec = FFT.RealFFT(windowed);
        int half = N / 2 + 1;
        float[] mag = new float[half];
        float[] phase = new float[half];
        for (int k = 0; k < half; k++)
        {
            float re = spec[k].Real;
            float im = spec[k].Imag;
            mag[k] = Mathf.Sqrt(re * re + im * im);
            phase[k] = Mathf.Atan2(im, re);
        }

        if (calibratedNoiseSpectrum != null)
        {
            int nlen = Mathf.Min(calibratedNoiseSpectrum.Length, mag.Length);
            for (int k = 0; k < nlen; k++)
            {
                float noiseMag = calibratedNoiseSpectrum[k] * spectralSubtractBias;
                mag[k] = Mathf.Max(0f, mag[k] - noiseMag);
            }
        }

        Complex[] newSpec = new Complex[spec.Length];
        for (int k = 0; k < half; k++)
        {
            float r = mag[k] * Mathf.Cos(phase[k]);
            float im = mag[k] * Mathf.Sin(phase[k]);
            newSpec[k] = new Complex(r, im);
        }
        for (int k = half; k < newSpec.Length; k++)
        {
            int mirror = newSpec.Length - k;
            if (mirror >= 0 && mirror < newSpec.Length)
                newSpec[k] = newSpec[mirror].Conjugate();
        }

        float[] outSignal = FFT.InverseRealFFT(newSpec);

        for (int i = 0; i < outSignal.Length; i++)
        {
            float w = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (N - 1)));
            if (Mathf.Abs(w) > 1e-6f) outSignal[i] /= w;
        }

        return outSignal;
    }

    float[] ComputeAverageNoiseSpectrum(float[] samples, int window)
    {
        if (samples == null || samples.Length < window) return null;
        int hop = window / 2;
        int frames = (samples.Length - window) / hop;
        if (frames <= 0) frames = 1;

        float[] avg = new float[window / 2 + 1];
        int count = 0;
        for (int i = 0; i + window <= samples.Length; i += hop)
        {
            float[] frame = new float[window];
            Array.Copy(samples, i, frame, 0, window);
            var mag = ComputeMagnitudeSpectrum(frame, window);
            for (int k = 0; k < mag.Length; k++) avg[k] += mag[k];
            count++;
        }
        if (count == 0) return null;
        for (int k = 0; k < avg.Length; k++) avg[k] /= count;
        return avg;
    }

    float[] ComputeMagnitudeSpectrum(float[] frame, int window)
    {
        int N = window;
        float[] w = new float[N];
        for (int n = 0; n < N; n++) w[n] = frame[n] * (0.5f * (1f - Mathf.Cos(2f * Mathf.PI * n / (N - 1))));
        var spec = FFT.RealFFT(w);
        int half = N / 2 + 1;
        float[] mag = new float[half];
        for (int k = 0; k < half; k++)
        {
            float re = spec[k].Real;
            float im = spec[k].Imag;
            mag[k] = Mathf.Sqrt(re * re + im * im);
        }
        return mag;
    }

    // -----------------------------
    // WAV encode PCM16
    // -----------------------------
    byte[] EncodeToWav(float[] samples, int sampleRate)
    {
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            int channels = 1;
            int bytesPerSample = 2;
            int subChunk2Size = samples.Length * bytesPerSample;
            int chunkSize = 36 + subChunk2Size;

            bw.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
            bw.Write(chunkSize);
            bw.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));

            bw.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * channels * bytesPerSample);
            bw.Write((short)(channels * bytesPerSample));
            bw.Write((short)(bytesPerSample * 8));

            bw.Write(System.Text.Encoding.UTF8.GetBytes("data"));
            bw.Write(subChunk2Size);

            foreach (var f in samples)
            {
                short v = (short)Mathf.Clamp(f * 32767f, -32768f, 32767f);
                bw.Write(v);
            }

            bw.Flush();
            return ms.ToArray();
        }
    }

    float ComputeRMS(float[] buffer)
    {
        double sum = 0;
        for (int i = 0; i < buffer.Length; i++) sum += buffer[i] * buffer[i];
        return (float)Math.Sqrt(sum / Math.Max(1, buffer.Length));
    }

    void OnDestroy()
    {
        shuttingDown = true;
        try
        {
            if (Microphone.IsRecording(micDeviceName)) Microphone.End(micDeviceName);
        }
        catch { }
    }

    // -----------------------------
    // FFT Implementation (iterative Cooley-Tukey)
    // -----------------------------
    struct Complex
    {
        public float Real;
        public float Imag;
        public Complex(float r, float i) { Real = r; Imag = i; }
        public Complex Conjugate() => new Complex(Real, -Imag);
    }

    static class FFT
    {
        public static Complex[] RealFFT(float[] real)
        {
            int N = real.Length;
            Complex[] data = new Complex[N];
            for (int i = 0; i < N; i++) data[i] = new Complex(real[i], 0f);
            return FFT_Complex(data);
        }

        public static float[] InverseRealFFT(Complex[] spectrum)
        {
            int N = spectrum.Length;
            Complex[] time = IFFT_Complex(spectrum);
            float[] real = new float[N];
            for (int i = 0; i < N; i++) real[i] = time[i].Real / N;
            return real;
        }

        static Complex[] FFT_Complex(Complex[] buffer)
        {
            int n = buffer.Length;
            if ((n & (n - 1)) != 0) throw new Exception("FFT length must be power of two");

            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while (j >= bit) { j -= bit; bit >>= 1; }
                j += bit;
                if (i < j)
                {
                    var tmp = buffer[i];
                    buffer[i] = buffer[j];
                    buffer[j] = tmp;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2.0 * Math.PI / len;
                float wlen_r = (float)Math.Cos(angle);
                float wlen_i = (float)Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    float wr = 1f, wi = 0f;
                    for (int j2 = 0; j2 < len / 2; j2++)
                    {
                        var u = buffer[i + j2];
                        var v = buffer[i + j2 + len / 2];
                        float vr = v.Real * wr - v.Imag * wi;
                        float vi = v.Real * wi + v.Imag * wr;

                        buffer[i + j2].Real = u.Real + vr;
                        buffer[i + j2].Imag = u.Imag + vi;
                        buffer[i + j2 + len / 2].Real = u.Real - vr;
                        buffer[i + j2 + len / 2].Imag = u.Imag - vi;

                        float tmp = wr * wlen_r - wi * wlen_i;
                        wi = wr * wlen_i + wi * wlen_r;
                        wr = tmp;
                    }
                }
            }
            return buffer;
        }

        static Complex[] IFFT_Complex(Complex[] buffer)
        {
            int n = buffer.Length;
            Complex[] conj = new Complex[n];
            for (int i = 0; i < n; i++) conj[i] = buffer[i].Conjugate();
            conj = FFT_Complex(conj);
            for (int i = 0; i < n; i++) conj[i] = conj[i].Conjugate();
            return conj;
        }
    }
}
