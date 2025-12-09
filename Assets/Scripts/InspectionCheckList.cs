using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InspectionCheckList : MonoBehaviour
{
    public GameObject CheckList;

    private List<GameObject> Tick = new List<GameObject>();
    private int currentCheckListNo = 0;
    bool isReady = true;

    private void Awake()
    {
        // Collect all "Tick" children
        foreach (var cl in CheckList.GetComponentsInChildren<Transform>(true))
        {
            if (cl.name == "Tick")
                Tick.Add(cl.gameObject);
        }

        // Disable all ticks
        foreach (var t in Tick)
            t.SetActive(false);

        // Start hidden
        gameObject.SetActive(false);
    }

    [ContextMenu("DoNextTick")]
    public void DoNextTick()
    {
        // Don't continue if UI hidden
        if (!gameObject.activeInHierarchy) return;

        // Safety check
        if (currentCheckListNo < 0 || currentCheckListNo >= Tick.Count)
        {
            Debug.LogWarning("No more checklist items left.");
            return;
        }
      

        if (isReady)
        {  
            // Activate current tick
            Tick[currentCheckListNo].SetActive(true);

            // Move to next
            currentCheckListNo++;

            isReady = false;
            StartCoroutine(WaitForTickOption());
        }
    }

    [ContextMenu("SkipTick")]
    public void SkipTick()
    {
        // Don't continue if UI hidden
        if (!gameObject.activeInHierarchy) return;

        // Safety check
        if (currentCheckListNo < 0 || currentCheckListNo >= Tick.Count)
        {
            Debug.LogWarning("No more checklist items left.");
            return;
        }


        if (isReady)
        {
            // Activate current tick
            Tick[currentCheckListNo+1].SetActive(true);

            // Move to next
            currentCheckListNo++;

            isReady = false;
            StartCoroutine(WaitForTickOption());
        }
    }
    IEnumerator WaitForTickOption()
    {
        yield return new WaitForSeconds(.5f);
        isReady = true;
    }

    // OPTIONAL: Reset checklist before reuse
    [ContextMenu("ResetChecklist")]
    public void ResetChecklist()
    {
        foreach (var t in Tick)
            t.SetActive(false);

        currentCheckListNo = 0;
    }
}
