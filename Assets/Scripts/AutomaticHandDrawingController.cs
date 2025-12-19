using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity;

/// <summary>
/// Stable pinch-based hand drawing using MediaPipe annotations
/// Optimized, jitter-free, pinch-safe, and resolution/device independent
/// </summary>
public class AutomaticHandDrawingController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HandLandmarkerResultAnnotationController handAnnotationController;
    [SerializeField] private Camera arCamera;

    [Header("Drawing Settings")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private float lineWidth = 0.01f;
    [SerializeField] private float minDrawDistance = 0.01f;
    [SerializeField] private Color drawingColor = Color.white;

    [Header("Pinch Settings")]
    [SerializeField] private float pinchThreshold = 0.05f;
    [SerializeField] private int pinchSmoothingFrames = 3;

    [Header("Position Smoothing")]
    [SerializeField, Range(0.01f, 0.5f)] private float smoothTime = 0.1f;

    [Header("Clear")]
    [SerializeField] private KeyCode clearKey = KeyCode.C;

    // State
    private LineRenderer currentLine;
    private readonly List<Vector3> linePoints = new();
    private Vector3 lastDrawPosition;
    private Vector3 smoothedPosition;
    private Vector3 velocity;

    private Queue<bool> pinchHistory = new();
    public bool isPinching;

    // Landmark indices
    private const int INDEX_TIP = 8;
    private const int THUMB_TIP = 4;

    private void Awake()
    {
        if (arCamera == null)
            arCamera = Camera.main;

        if (lineMaterial == null)
        {
            lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lineMaterial.color = drawingColor;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(clearKey))
            ClearDrawing();

        ProcessHandTracking();
    }

    private void ProcessHandTracking()
    {
        if (handAnnotationController == null) return;

        var multiHand = handAnnotationController.GetComponentInChildren<MultiHandLandmarkListAnnotation>();
        if (multiHand == null || multiHand.transform.childCount == 0)
        {
            SafeRelease();
            return;
        }

        HandLandmarkListAnnotation hand = GetFirstActiveHand(multiHand);
        if (hand == null)
        {
            SafeRelease();
            return;
        }

        var indexTip = hand[INDEX_TIP];
        var thumbTip = hand[THUMB_TIP];

        if (indexTip == null || !indexTip.gameObject.activeInHierarchy ||
            thumbTip == null || !thumbTip.gameObject.activeInHierarchy)
        {
            SafeRelease();
            return;
        }

        // Use normalized screen space distance for pinch detection
        Vector3 indexScreen = arCamera.WorldToScreenPoint(indexTip.transform.position);
        Vector3 thumbScreen = arCamera.WorldToScreenPoint(thumbTip.transform.position);

        bool pinchDetected = Vector2.Distance(indexScreen, thumbScreen) <= pinchThreshold * UnityEngine.Screen.width;
        Debug.Log(Vector2.Distance(indexScreen, thumbScreen));

        // Temporal smoothing of pinch state
        pinchHistory.Enqueue(pinchDetected);
        if (pinchHistory.Count > pinchSmoothingFrames)
            pinchHistory.Dequeue();

        int trueCount = 0;
        foreach (var b in pinchHistory) if (b) trueCount++;
        bool currentlyPinching = trueCount > pinchHistory.Count / 2;

        // Detect pinch start
        if (currentlyPinching && !isPinching)
            StartDrawing(indexTip.transform.position);

        // Detect pinch end
        if (!currentlyPinching && isPinching)
            StopDrawing();

        isPinching = currentlyPinching;

        // Smooth position
        Vector3 rawWorldPos = indexTip.transform.position;
        smoothedPosition = Vector3.SmoothDamp(smoothedPosition, rawWorldPos, ref velocity, smoothTime);

        if (currentlyPinching && currentLine != null)
            ContinueDrawing(smoothedPosition);
    }

    private HandLandmarkListAnnotation GetFirstActiveHand(MultiHandLandmarkListAnnotation multiHand)
    {
        foreach (Transform child in multiHand.transform)
        {
            var hand = child.GetComponent<HandLandmarkListAnnotation>();
            if (hand != null && child.gameObject.activeInHierarchy)
                return hand;
        }
        return null;
    }

    private void StartDrawing(Vector3 startPos)
    {
        isPinching = true;

        GameObject lineObj = new("DrawLine");
        lineObj.transform.SetParent(transform);

        currentLine = lineObj.AddComponent<LineRenderer>();
        currentLine.material = lineMaterial;
        currentLine.startWidth = lineWidth;
        currentLine.endWidth = lineWidth;
        currentLine.useWorldSpace = true;
        currentLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        currentLine.receiveShadows = false;

        linePoints.Clear();
        AddPoint(startPos);
        lastDrawPosition = startPos;
        smoothedPosition = startPos;
        velocity = Vector3.zero;
    }

    private void ContinueDrawing(Vector3 position)
    {
        if ((position - lastDrawPosition).sqrMagnitude < minDrawDistance * minDrawDistance)
            return;

        AddPoint(position);
        lastDrawPosition = position;
    }

    private void AddPoint(Vector3 point)
    {
        linePoints.Add(point);
        currentLine.positionCount = linePoints.Count;
        currentLine.SetPosition(linePoints.Count - 1, point);
    }

    private void StopDrawing()
    {
        isPinching = false;
        currentLine = null;
        linePoints.Clear();
    }

    private void SafeRelease()
    {
        if (isPinching)
            StopDrawing();
    }

    public void ClearDrawing()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        SafeRelease();
    }
}
