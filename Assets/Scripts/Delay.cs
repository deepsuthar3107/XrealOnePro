using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
public class Delay : MonoBehaviour
{
    public float Duration;
    public bool DisableAfterPerforming = false;

    [Space]
    public UnityEvent Event;
    private void OnEnable()
    {
        StartCoroutine(Wait());
    }

    IEnumerator Wait()
    {
        yield return new WaitForSeconds(Duration);
        Event.Invoke();

        if(DisableAfterPerforming)
        {
            gameObject.SetActive(false);
        }
    }
}
