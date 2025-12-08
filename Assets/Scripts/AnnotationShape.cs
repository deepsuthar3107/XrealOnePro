using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnnotationShape : MonoBehaviour
{
    public Color SelectionColor;
    public float selectionScaleThresold;

    Vector3 defaultScale;
    Color defaultColor;
    
    private void Awake()
    {
        defaultScale = transform.localScale;
        defaultColor = GetComponent<SpriteRenderer>().color;
    }

    [ContextMenu("SelectAnnotation")]
    public void SelectAnnotation()
    {
        transform.localScale = defaultScale * selectionScaleThresold;
        GetComponent<SpriteRenderer>().color = SelectionColor;
    }

    [ContextMenu("DeselectAnnotation")]
    public void DeselectAnnotation()
    {
        transform.localScale = defaultScale;
        GetComponent <SpriteRenderer>().color = defaultColor;
    }
}
