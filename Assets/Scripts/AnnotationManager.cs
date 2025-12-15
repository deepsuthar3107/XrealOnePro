using System.Collections.Generic;
using UnityEngine;

public class AnnotationManager : MonoBehaviour
{
    private List<AnnotationShape> annotationShape = new List<AnnotationShape>();
    private AnnotationShape activeAnnotation;

    public void deleteAnnotation()
    {
        // Reset
        activeAnnotation = null;

        // Get all annotation shapes in scene
        annotationShape = new List<AnnotationShape>(FindObjectsOfType<AnnotationShape>());

        foreach (var shape in annotationShape)
        {
            if (shape.isSelected)
            {
                activeAnnotation = shape;
                break; // Found the selected one, no need to continue
            }
        }

        // Delete if found
        if (activeAnnotation != null)
        {
            Destroy(activeAnnotation.gameObject);
            activeAnnotation = null;
        }
    }

    public void deleteAnnotationAll()
    {
        // Reset
        activeAnnotation = null;

        // Get all annotation shapes in scene
        annotationShape = new List<AnnotationShape>(FindObjectsOfType<AnnotationShape>());

        foreach (var shape in annotationShape)
        {
            Destroy(shape.gameObject);
        }
    }
}
