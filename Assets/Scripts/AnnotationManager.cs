using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit;

public class AnnotationManager : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    [SerializeField] private float maxDistance = 2f;

    [Header("Annotation Prefabs")]
    [SerializeField] private GameObject squareAnnotationPrefab;
    [SerializeField] private GameObject circleAnnotationPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private float annotationCheckRadius = 0.05f;

    [Header("AR Components")]
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private XROrigin xrOrigin;

    private AnnotationShape currentAnnotation;
    private List<AnnotationShape> cachedAnnotations = new List<AnnotationShape>();
    private List<ARAnchor> spawnedAnchors = new List<ARAnchor>(); // Track anchors

    private void Start()
    {
        // Auto-find AR components if not assigned
        if (anchorManager == null)
        {
            anchorManager = FindObjectOfType<ARAnchorManager>();
            if (anchorManager == null)
            {
                Debug.LogWarning("ARAnchorManager not found! Annotations may drift on device.");
            }
        }

        if (xrOrigin == null)
        {
            xrOrigin = FindObjectOfType<XROrigin>();
        }
    }

    private void Update()
    {
        HandleAnnotationSelection();
    }

    public void ProcessCommand(string command)
    {
        if (IsCircleCommand(command))
        {
            MarkCircle();
            return;
        }
        else if (IsSquareCommand(command))
        {
            MarkSquare();
            return;
        }
        else if (IsDeleteCommand(command))
        {
            DeleteSelectedAnnotation();
            return;
        }
        else if (IsDeleteAllCommand(command))
        {
            DeleteAllAnnotations();
            return;
        }
    }

    #region Selection
    private void HandleAnnotationSelection()
    {
        Ray ray = new Ray(startPoint.position, startPoint.forward);
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, maxDistance);

        Debug.DrawRay(ray.origin, ray.direction * maxDistance, hitSomething ? Color.green : Color.red);

        if (hitSomething)
        {
            AnnotationShape newAnnotation = hit.collider.GetComponent<AnnotationShape>();

            if (newAnnotation != null)
            {
                if (currentAnnotation != null && currentAnnotation != newAnnotation)
                    currentAnnotation.DeselectAnnotation();

                currentAnnotation = newAnnotation;
                currentAnnotation.SelectAnnotation();
                return;
            }
        }

        // Deselect if nothing valid was hit
        if (currentAnnotation != null)
        {
            currentAnnotation.DeselectAnnotation();
            currentAnnotation = null;
        }
    }
    #endregion

    #region Spawning
    [ContextMenu("Mark Square")]
    public void MarkSquare()
    {
        SpawnAnnotation(squareAnnotationPrefab);
    }

    [ContextMenu("Mark Circle")]
    public void MarkCircle()
    {
        SpawnAnnotation(circleAnnotationPrefab);
    }

    private void SpawnAnnotation(GameObject annotationPrefab)
    {
        if (annotationPrefab == null)
        {
            Debug.LogWarning("Annotation prefab is null!");
            return;
        }

        if (IsAnnotationAtEndPoint())
        {
            Debug.Log("Annotation already exists here. Not spawning.");
            return;
        }

        Quaternion rotation = startPoint.forward != Vector3.zero
            ? Quaternion.LookRotation(startPoint.forward)
            : Quaternion.identity;

        // NEW: Create with AR Anchor for stability on device
        if (anchorManager != null)
        {
            SpawnWithAnchor(annotationPrefab, endPoint.position, rotation);
        }
        else
        {
            // Fallback: spawn without anchor (will work in editor but may drift on device)
            GameObject annotation = Instantiate(annotationPrefab, endPoint.position, rotation);

            // Parent to XR Origin's TrackablesParent if available
            if (xrOrigin != null)
            {
                Transform trackablesParent = xrOrigin.TrackablesParent;
                if (trackablesParent != null)
                {
                    annotation.transform.SetParent(trackablesParent);
                }
            }

            Debug.LogWarning("Spawned without AR Anchor - may drift on device!");
        }
    }

    // NEW METHOD: Spawn annotation with AR Anchor
    private void SpawnWithAnchor(GameObject annotationPrefab, Vector3 position, Quaternion rotation)
    {
        // Create a GameObject at the spawn position
        GameObject anchorObject = new GameObject("Annotation Anchor");
        anchorObject.transform.position = position;
        anchorObject.transform.rotation = rotation;

        // Add ARAnchor component (new API)
        ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();

        if (anchor != null)
        {
            // Instantiate annotation as child of anchor
            GameObject annotation = Instantiate(annotationPrefab, anchorObject.transform);
            annotation.transform.localPosition = Vector3.zero;
            annotation.transform.localRotation = Quaternion.identity;

            // Track the anchor for cleanup
            spawnedAnchors.Add(anchor);

            Debug.Log($"Annotation spawned with AR Anchor at: {position}");
        }
        else
        {
            Debug.LogError("Failed to create AR Anchor! Annotation may drift.");
            // Fallback to non-anchored spawn
            Destroy(anchorObject);
            Instantiate(annotationPrefab, position, rotation);
        }
    }

    private bool IsAnnotationAtEndPoint()
    {
        Collider[] colliders = Physics.OverlapSphere(endPoint.position, annotationCheckRadius);

        foreach (var col in colliders)
        {
            if (col.GetComponent<AnnotationShape>() != null)
                return true;
        }

        return false;
    }
    #endregion

    #region Deletion
    [ContextMenu("Delete Selected Annotation")]
    public void DeleteSelectedAnnotation()
    {
        RefreshAnnotationCache();

        foreach (var shape in cachedAnnotations)
        {
            if (shape.isSelected)
            {
                // NEW: Also destroy the anchor if it exists
                ARAnchor anchor = shape.GetComponentInParent<ARAnchor>();
                if (anchor != null)
                {
                    spawnedAnchors.Remove(anchor);
                    Destroy(anchor.gameObject); // Destroys anchor and its children
                }
                else
                {
                    Destroy(shape.gameObject);
                }

                currentAnnotation = null;
                return;
            }
        }

        Debug.Log("No selected annotation to delete.");
    }

    [ContextMenu("Delete All Annotations")]
    public void DeleteAllAnnotations()
    {
        RefreshAnnotationCache();

        foreach (var shape in cachedAnnotations)
        {
            // NEW: Check if annotation has an anchor parent
            ARAnchor anchor = shape.GetComponentInParent<ARAnchor>();
            if (anchor != null)
            {
                Destroy(anchor.gameObject); // Destroys anchor and children
            }
            else
            {
                Destroy(shape.gameObject);
            }
        }

        currentAnnotation = null;
        cachedAnnotations.Clear();
        spawnedAnchors.Clear();
    }

    private void RefreshAnnotationCache()
    {
        cachedAnnotations = new List<AnnotationShape>(FindObjectsOfType<AnnotationShape>());
    }
    #endregion

    private bool IsCircleCommand(string cmd) =>
     cmd == "mark circle";

    private bool IsSquareCommand(string cmd) =>
        cmd == "mark square";

    private bool IsDeleteCommand(string cmd) =>
        cmd == "delete" || cmd == "erase" || cmd == "clear" || cmd == "remove";

    private bool IsDeleteAllCommand(string cmd) =>
       cmd == "delete all annotation" || cmd == "remove all annotation" || cmd == "clear all annotation";
}