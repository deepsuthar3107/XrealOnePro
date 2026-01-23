using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class UploadConfirmAPI : MonoBehaviour
{
    private const string URL = "https://spectacles-api.prakashinfotech.com/videos/upload-confirm";
    private const string AUTH_TOKEN = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJlbWFpbCI6ImphbWVzLndpbGxpYW1zQGdtYWlsLmNvbSIsInN1YiI6ImJhNTc5YTQ4LWM4YmUtNDNkNy1hZTcyLTRjZDI1MDlhZDJkZSIsImlhdCI6MTc2OTA4MTAwMywiZXhwIjoxNzY5MTY3NDAzfQ.U0SOFLpNGg0B5ORhpSAQ8ufQnSS_Sy2SOnaa96MtHXE"; // Replace with real token

    void Start()
    {
        StartCoroutine(UploadConfirm());
    }

    IEnumerator UploadConfirm()
    {
        Debug.Log("New Code");
        // Create JSON body
        string jsonBody = @"{
            ""s3FileKeys"": [
                ""uploads/ab604076-09e6-4946-bdcf-a5cc41e1882f/1768992609950___7061d5fe-deff-48e5-b246-aa42d96e5c03.mp4""
            ],
            ""licensePlateId"": ""ab604076-09e6-4946-bdcf-a5cc41e1882f""
        }";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(URL, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
           // request.SetRequestHeader("Authorization", AUTH_TOKEN);
            request.SetRequestHeader("Authorization", "Bearer " + AUTH_TOKEN);
            request.SetRequestHeader("User-Agent", "Mozilla/5.0");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Response: " + request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"Error: {request.error}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
            }
        }
    }
}