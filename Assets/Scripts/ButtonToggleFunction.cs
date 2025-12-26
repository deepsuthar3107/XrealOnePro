using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ButtonToggleFunction : MonoBehaviour
{
    public UnityEvent Event1;
    public UnityEvent Event2;

    bool isToggle;
    void Start()
    {
        GetComponent<Button>().onClick.AddListener(ToggleFunction);
    }

    public void ToggleFunction()
    {
        if(!isToggle)
        {
            Event1.Invoke();
            isToggle = true;
        }
        else
        {
            Event2.Invoke();
            isToggle = false;
        }
    }
}
