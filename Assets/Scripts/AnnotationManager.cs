using System.Collections.Generic;
using UnityEngine;

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

    private AnnotationShape currentAnnotation;
    private List<AnnotationShape> cachedAnnotations = new List<AnnotationShape>();

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

        Instantiate(annotationPrefab, endPoint.position, rotation);
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
                Destroy(shape.gameObject);
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
            Destroy(shape.gameObject);
        }

        currentAnnotation = null;
        cachedAnnotations.Clear();
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