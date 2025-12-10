using UnityEngine;

public class MarkAnnotation : MonoBehaviour
{
    public Transform startPoint;
    public Transform endPoint;
    public float maxDistance = 2f;
    public GameObject SquareAnnotation;
    public GameObject CircleAnnotation;
    public float annotationCheckRadius = 0.05f; // configurable radius

    private AnnotationShape currentAnnotation;

    private void Update()
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
                return; // exit Update if we hit a valid annotation
            }
        }

        // Deselect previous annotation if nothing hit or hit non-annotation
        if (currentAnnotation != null)
        {
            currentAnnotation.DeselectAnnotation();
            currentAnnotation = null;
        }
    }

    [ContextMenu("MarkSquare")]
    public void MarkSquare()
    {
        SpawnAnnotation(SquareAnnotation);
    }

    [ContextMenu("MarkCircle")]
    public void MarkCircle()
    {
        SpawnAnnotation(CircleAnnotation);
    }

    private void SpawnAnnotation(GameObject annotationPrefab)
    {
        if (!IsAnnotationAtEndPoint())
        {
            // Align with surface normal if you hit something, else use forward
            Quaternion rotation = startPoint.forward != Vector3.zero
                ? Quaternion.LookRotation(startPoint.forward)
                : Quaternion.identity;

            Instantiate(annotationPrefab, endPoint.position, rotation);
        }
        else
        {
            Debug.Log("Annotation already exists here. Not spawning.");
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
}
