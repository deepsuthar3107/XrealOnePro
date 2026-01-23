using System;

[Serializable]
public class RecordedVideoInfo
{
    public string fileName;
    public string filePath;
    public long fileSize;
    public int duration;
}

[Serializable]
public class LoginResponse
{
    public LoginData data;
}

[Serializable]
public class LoginData
{
    public string accessToken;
}

[Serializable]
public class PresignedUrlResponse
{
    public PresignedData data;
}

[Serializable]
public class PresignedData
{
    public PresignedItem[] presignedUrls;
}

[Serializable]
public class PresignedItem
{
    public string fileName;
    public string url;
    public string s3FileKey;
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

[System.Serializable]
class ConfirmUploadRequest
{
    public string[] s3FileKeys;
    public string licensePlateId;
}