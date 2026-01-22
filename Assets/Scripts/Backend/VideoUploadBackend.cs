using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine.Video;
using TMPro;

public class VideoUploadBackend : MonoBehaviour
{
    public VideoPlayer vp;

    [Header("Backend Config")]
    public string baseUrl = "https://your-api.com";
    public string licensePlateId = "ab604076-09e6-4946-bdcf-a5cc41e1882f";

    [Header("Login Credentials (Editor Testing)")]
    public string email = "james.williams@gmail.com";
    public string password = "Test@12345678";

    [Header("DEBUG UI (TMP)")]
    public TMP_Text debugText;
    public TMP_Text progressText;
    public bool showDebugUI = true;

    private const string TOKEN_KEY = "AccessToken";
    private const string TOKEN_TIME_KEY = "AccessTokenTime";
    private const int TOKEN_VALID_HOURS = 24;

    private string currentVideoPath;

    #region ================= DEBUG HELPERS =================

    void LogUI(string msg)
    {
        Debug.Log(msg);
        if (!showDebugUI || debugText == null) return;
        debugText.text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
    }

    void SetProgress(float value)
    {
        if (progressText != null)
            progressText.text = $"Upload: {(value * 100f):0.0}%";
    }

    #endregion

    [ContextMenu("TEST → Login")] 
    public void TestLogin() 
    { 
        StartCoroutine(LoginCoroutine());
    }

    [ContextMenu("TEST → GeneratePresigned")]
    public void TestGeneratePresigned() 
    { 
        RecordedVideoInfo info = new RecordedVideoInfo 
        { filePath = "C:/Users/HP/Downloads/TruVideo_1768045738397.mp4", 
            fileName = "TruVideo_1768045738397.mp4", 
            fileSize = 65, 
            duration = 10 };
        StartCoroutine(GeneratePresignedUrlCoroutine(info)); 
    }


    #region ================= ENTRY =================
    private void Start()
    {
        if (IsTokenValid())
        {
            progressText.text = "Login Successed";
            return;
        }

        StartCoroutine(LoginCoroutine());
    }
    public void UploadRecordedVideo(string originFilePath)
    {
        if (!File.Exists(originFilePath))
        {
            LogUI("❌ File not found: " + originFilePath);
            return;
        }

        currentVideoPath = originFilePath;

        vp.prepareCompleted -= OnPrepared;
        vp.source = VideoSource.Url;
        vp.url = originFilePath;
        vp.prepareCompleted += OnPrepared;

        LogUI("🎬 Preparing video...");
        vp.Prepare();
    }

    void OnPrepared(VideoPlayer videoPlayer)
    {
        videoPlayer.prepareCompleted -= OnPrepared;

        FileInfo fi = new FileInfo(currentVideoPath);

        RecordedVideoInfo info = new RecordedVideoInfo
        {
            filePath = currentVideoPath,
            fileName = fi.Name,
            fileSize = fi.Length,
            duration = (float)videoPlayer.length
        };

        LogUI($"📄 Video Ready\nName: {info.fileName}\nSize: {info.fileSize / (1024f * 1024f):0.00} MB\nDuration: {info.duration:0.0}s");

        ProcessUploadVideo(info);
    }

    void ProcessUploadVideo(RecordedVideoInfo info)
    {
        LogUI("🚀 Starting upload flow...");
        EnsureLoggedIn(() =>
        {
            StartCoroutine(GeneratePresignedUrlCoroutine(info));
        });
    }

    #endregion

    #region ================= LOGIN =================

    void EnsureLoggedIn(Action onSuccess)
    {
        if (IsTokenValid())
        {
            LogUI("🔐 Token valid, skipping login");
            onSuccess?.Invoke();
            return;
        }

        LogUI("🔐 Logging in...");
        StartCoroutine(LoginCoroutine(onSuccess));
    }

    bool IsTokenValid()
    {
        if (!PlayerPrefs.HasKey(TOKEN_KEY) || !PlayerPrefs.HasKey(TOKEN_TIME_KEY))
            return false;

        long ticks = long.Parse(PlayerPrefs.GetString(TOKEN_TIME_KEY));
        DateTime savedTime = new DateTime(ticks, DateTimeKind.Utc);

        return (DateTime.UtcNow - savedTime).TotalHours < TOKEN_VALID_HOURS;
    }

    IEnumerator LoginCoroutine(Action onSuccess = null)
    {
        string url = baseUrl + "/auth/login";
        string json = $"{{\"email\":\"{email}\",\"password\":\"{password}\"}}";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("User-Agent", "Unity/2026");

        yield return request.SendWebRequest();

        LogUI("📥 Login Response: " + request.downloadHandler.text);

        if (request.responseCode < 200 || request.responseCode >= 300)
        {
            LogUI("❌ Login failed: " + request.error);
            yield break;
        }

        LoginResponse response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);

        PlayerPrefs.SetString(TOKEN_KEY, response.data.accessToken);
        PlayerPrefs.SetString(TOKEN_TIME_KEY, DateTime.UtcNow.Ticks.ToString());
        PlayerPrefs.Save();

        progressText.text = "Login Success";
        LogUI("✅ Login success");
        onSuccess?.Invoke();
    }

    #endregion

    #region ================= PRESIGNED URL =================

    IEnumerator GeneratePresignedUrlCoroutine(RecordedVideoInfo info)
    {
        LogUI("📝 Requesting presigned URL...");
        string token = PlayerPrefs.GetString(TOKEN_KEY);
        if (string.IsNullOrEmpty(token))
        {
            Debug.LogError("Login required first.");
            yield break;
        }

        string url = baseUrl + "/videos/generate-presigned-url";

        string json = $@"{{
            ""fileDetails"": [{{
                ""fileName"": ""{info.fileName}"",
                ""duration"": {info.duration},
                ""fileSize"": {info.fileSize}
            }}],
            ""licensePlateId"": ""{licensePlateId}""
        }}";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.SetRequestHeader("User-Agent", "Unity/2026");

        yield return request.SendWebRequest();

        LogUI("📥 Presigned Response: " + request.downloadHandler.text);

        if (request.downloadHandler.text.StartsWith("<!DOCTYPE html>") || request.downloadHandler.text.Contains("Just a moment"))
        {
            Debug.LogError("Request blocked by Cloudflare. Use a server-side proxy.");
            yield break;
        }

        if (request.responseCode < 200 || request.responseCode >= 300)
        {
            LogUI("❌ Presigned URL request failed");
            yield break;
        }

        PresignedUrlResponse response = JsonUtility.FromJson<PresignedUrlResponse>(request.downloadHandler.text);

        if (response.data.presignedUrls.Length == 0)
        {
            LogUI("❌ No presigned URLs returned");
            yield break;
        }

        PresignedItem item = response.data.presignedUrls[0];
        StartCoroutine(UploadFileToS3(item.url, info.filePath, item.s3FileKey));
    }

    #endregion

    #region ================= S3 UPLOAD =================

    IEnumerator UploadFileToS3(string presignedUrl, string filePath, string s3FileKey)
    {
        LogUI("⬆ Uploading video to S3...");
        SetProgress(0);

        byte[] fileData = File.ReadAllBytes(filePath);

        UnityWebRequest request = new UnityWebRequest(presignedUrl, "PUT");
        request.uploadHandler = new UploadHandlerRaw(fileData);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "video/mp4");

        request.SendWebRequest();

        while (!request.isDone)
        {
            SetProgress(request.uploadProgress);
            yield return null;
        }

        if (request.result == UnityWebRequest.Result.Success)
        {
            SetProgress(1f);
            LogUI("✅ S3 upload completed");
            StartCoroutine(UploadConfirmCoroutine(s3FileKey));
        }
        else
        {
            LogUI("❌ S3 upload failed: " + request.error);
        }
    }

    #endregion

    #region ================= UPLOAD CONFIRM =================

    IEnumerator UploadConfirmCoroutine(string s3FileKey)
    {
        LogUI("📡 Confirming upload with backend...");

        if (string.IsNullOrEmpty(s3FileKey))
        {
            LogUI("❌ s3FileKey is null or empty");
            yield break;
        }

        string token = PlayerPrefs.GetString(TOKEN_KEY);
        string url = baseUrl + "/videos/upload-confirm";

        UploadConfirmRequest confirmRequest = new UploadConfirmRequest
        {
            s3FileKeys = new string[] { s3FileKey }
        };

        string json = JsonUtility.ToJson(confirmRequest);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + token);

        yield return request.SendWebRequest();

        string responseText = request.downloadHandler.text;
        LogUI("📥 Upload Confirm Response: " + responseText);

        if (request.responseCode >= 200 && request.responseCode < 300)
        {
            try
            {
                UploadConfirmResponse resp = JsonUtility.FromJson<UploadConfirmResponse>(responseText);
                if (resp.success && resp.data.uploaded.Length > 0)
                    LogUI("🎉 Upload confirmed successfully: " + string.Join(", ", resp.data.uploaded));
                else
                    LogUI("❌ Upload confirm failed: " + resp.message + " | Failed files: " + string.Join(", ", resp.data.failed));
            }
            catch (Exception e)
            {
                LogUI("❌ Failed to parse upload confirm response: " + e);
            }
        }
        else
        {
            LogUI($"❌ Upload confirm failed: {request.responseCode} {request.error}");
        }
    }

    [Serializable]
    public class UploadConfirmRequest
    {
        public string[] s3FileKeys;
    }

    [Serializable]
    public class UploadConfirmResponseData
    {
        public string[] uploaded;
        public string[] failed;
    }

    [Serializable]
    public class UploadConfirmResponse
    {
        public bool success;
        public string message;
        public UploadConfirmResponseData data;
    }

    #endregion
}
