using UnityEngine;
using System.IO;

public class CameraRenderToPNG : MonoBehaviour
{
    public Camera targetCamera;
    public int width = 1024;
    public int height = 1024;
    public bool transparent = true;

    [ContextMenu("capture")]
    public void Capture()
    {
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        targetCamera.targetTexture = rt;

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        if (transparent)
            targetCamera.clearFlags = CameraClearFlags.SolidColor;

        targetCamera.backgroundColor = transparent ? Color.clear : Color.black;
        targetCamera.Render();

        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        byte[] png = tex.EncodeToPNG();
        string path = Path.Combine(Application.persistentDataPath, "Render.png");
        File.WriteAllBytes(path, png);
        Debug.Log("Render saved at: " + path);

        // Cleanup
        targetCamera.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(rt);
        DestroyImmediate(tex);

        Debug.Log("Render saved!");
    }
}
