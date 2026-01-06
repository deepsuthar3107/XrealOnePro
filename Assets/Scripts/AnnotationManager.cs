using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

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
    private List<AnnotationShape> cachedAnnotations = new();
    private List<ARAnchor> spawnedAnchors = new();

    private void Start()
    {
        if (anchorManager == null)
            anchorManager = FindObjectOfType<ARAnchorManager>();

        if (xrOrigin == null)
            xrOrigin = FindObjectOfType<XROrigin>();

        if (anchorManager == null)
            Debug.LogError("ARAnchorManager NOT found – annotations WILL drift on device!");
    }

    private void Update()
    {
        HandleAnnotationSelection();
    }

    #region Commands
    public void ProcessCommand(string command)
    {
        command = command.ToLower();
        if (IsCircleCommand(command)) MarkCircle();
        else if (IsSquareCommand(command)) MarkSquare();
        else if (IsDeleteCommand(command)) DeleteSelectedAnnotation();
        else if (IsDeleteAllCommand(command)) DeleteAllAnnotations();
    }
    #endregion

    #region Selection
    private void HandleAnnotationSelection()
    {
        Ray ray = new(startPoint.position, startPoint.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            AnnotationShape shape = hit.collider.GetComponent<AnnotationShape>();

            if (shape != null)
            {
                if (currentAnnotation != shape)
                {
                    if (currentAnnotation != null)
                        currentAnnotation?.DeselectAnnotation();

                    currentAnnotation = shape;
                    currentAnnotation.SelectAnnotation();
                }
                return;
            }
        }

        if(currentAnnotation != null)
        {
            currentAnnotation?.DeselectAnnotation();
            currentAnnotation = null;
        }    
    }
    #endregion

    #region Spawning
    [ContextMenu("MarkSquare")]
    public void MarkSquare() => SpawnAnnotation(squareAnnotationPrefab);

    [ContextMenu("MarkCircle")]
    public void MarkCircle() => SpawnAnnotation(circleAnnotationPrefab);

    private void SpawnAnnotation(GameObject prefab)
    {
        if (prefab == null || anchorManager == null)
            return;

        if (IsAnnotationAtEndPoint())
            return;

        Pose pose = new Pose(
            endPoint.position,
            Quaternion.identity 
        );

        SpawnWithAnchor(prefab, pose);
    }

    private void SpawnWithAnchor(GameObject prefab, Pose pose)
    {
        if (anchorManager == null || xrOrigin == null)
        {
            Debug.LogError("Missing ARAnchorManager or XROrigin");
            return;
        }

        // 1️⃣ Create anchor GameObject
        GameObject anchorGO = new GameObject("AnnotationAnchor");

        // 2️⃣ Parent to TrackablesParent (CRITICAL)
        anchorGO.transform.SetParent(xrOrigin.TrackablesParent, false);

        // 3️⃣ Set world pose ONCE
        anchorGO.transform.SetPositionAndRotation(
            pose.position,
            pose.rotation
        );

        // 4️⃣ Add ARAnchor component (modern API)
        ARAnchor anchor = anchorGO.AddComponent<ARAnchor>();

        if (anchor.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.None)
        {
            Debug.LogWarning("Anchor tracking not ready yet");
        }

        // 5️⃣ Instantiate annotation as child
        GameObject annotation = Instantiate(prefab, anchor.transform);
        annotation.transform.localPosition = Vector3.zero;

        // Face camera ONCE at spawn
        Vector3 directionToCamera = Camera.main.transform.position - anchor.transform.position;

        // Zero out Y if you want upright only
        directionToCamera.y = 0f;

        if (directionToCamera.sqrMagnitude > 0f)
            annotation.transform.rotation = Quaternion.LookRotation(directionToCamera);
        else
            annotation.transform.rotation = Quaternion.identity;


        spawnedAnchors.Add(anchor);
    }


    private bool IsAnnotationAtEndPoint()
    {
        Collider[] hits = Physics.OverlapSphere(endPoint.position, annotationCheckRadius);

        foreach (Collider col in hits)
            if (col.GetComponent<AnnotationShape>() != null)
                return true;

        return false;
    }
    #endregion

    #region Deletion
    [ContextMenu("DeleteSelectedAnnotation")]
    public void DeleteSelectedAnnotation()
    {
        RefreshAnnotationCache();

        foreach (var shape in cachedAnnotations)
        {
            if (!shape.isSelected)
                continue;

            ARAnchor anchor = shape.GetComponentInParent<ARAnchor>();

            if (anchor != null)
            {
                spawnedAnchors.Remove(anchor);
                Destroy(anchor.gameObject);
            }
            else
            {
                Destroy(shape.gameObject);
            }

            currentAnnotation = null;
            return;
        }
    }

    public void DeleteAllAnnotations()
    {
        foreach (ARAnchor anchor in spawnedAnchors)
        {
            if (anchor != null)
                Destroy(anchor.gameObject);
        }

        spawnedAnchors.Clear();
        cachedAnnotations.Clear();
        currentAnnotation = null;
    }

    private void RefreshAnnotationCache()
    {
        cachedAnnotations = new List<AnnotationShape>(FindObjectsOfType<AnnotationShape>());
    }
    #endregion

    private bool IsCircleCommand(string cmd) =>
    cmd.Contains("mark circle");

    private bool IsSquareCommand(string cmd) =>
        cmd.Contains("mark square");

    private bool IsDeleteCommand(string cmd) =>
        cmd.Contains("delete") || cmd.Contains("erase") || cmd.Contains("clear") || cmd.Contains("remove");

    private bool IsDeleteAllCommand(string cmd) =>
       cmd.Contains("delete all annotation") || cmd.Contains("remove all annotation") || cmd.Contains("clear all annotation");
}
