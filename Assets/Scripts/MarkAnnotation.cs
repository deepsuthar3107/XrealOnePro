using UnityEngine;
public class MarkAnnotation : MonoBehaviour
{
    public Transform startPoint;
    public Transform endPoint;
    public float maxDistance = 2f;
    public GameObject SquareAnnotation;
    public GameObject CircleAnnotation;

    private AnnotationShape currentAnnotation;

    private void Update()
    {
        Ray ray = new Ray(startPoint.position, startPoint.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.green);

            AnnotationShape newAnnotation = hit.collider.GetComponent<AnnotationShape>();

            if (newAnnotation != null)
            {
                // Deselect previous annotation if different
                if (currentAnnotation != null && currentAnnotation != newAnnotation)
                    currentAnnotation.DeselectAnnotation();

                currentAnnotation = newAnnotation;
                currentAnnotation.SelectAnnotation();
                return;
            }
        }
        else
        {
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.red);
        }

        // Deselect previous annotation if nothing hit
        if (currentAnnotation != null)
        {
            currentAnnotation.DeselectAnnotation();
            currentAnnotation = null;
        }
    }

    [ContextMenu("MarkSquare")]
    public void MarkSquare()
    {
        if (!IsAnnotationAtEndPoint())
        {
            Instantiate(
                SquareAnnotation,
                endPoint.position,
                Quaternion.LookRotation(startPoint.forward)
            );
        }
        else
        {
            Debug.Log("Annotation already exists here. Not spawning.");
        }
    }

    [ContextMenu("MarkCircle")]
    public void MarkCircle()
    {
        if (!IsAnnotationAtEndPoint())
        {
            Instantiate(
                CircleAnnotation,
                endPoint.position,
                Quaternion.LookRotation(startPoint.forward)
            );
        }
        else
        {
            Debug.Log("Annotation already exists here. Not spawning.");
        }
    }

    // Check if there's already an annotation at the spawn point
    private bool IsAnnotationAtEndPoint()
    {
        Collider[] colliders = Physics.OverlapSphere(endPoint.position, 0.05f); // small radius
        foreach (var col in colliders)
        {
            if (col.GetComponent<AnnotationShape>() != null)
                return true;
        }
        return false;
    }
}
