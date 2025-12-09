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
        if (!gameObject.activeInHierarchy) return;

        if (!isReady || currentCheckListNo >= Tick.Count)
        {
            Debug.LogWarning("No more checklist items.");
            return;
        }

        Tick[currentCheckListNo].SetActive(true);
        currentCheckListNo++;

        StartCoroutine(SetReadyAfterDelay());
    }

    [ContextMenu("DoPreviousTick")]
    public void DoPreviousTick()
    {
        if (!gameObject.activeInHierarchy) return;

        if (!isReady || currentCheckListNo <= 0)
        {
            Debug.LogWarning("Already at beginning.");
            return;
        }

        // Move back one
        currentCheckListNo--;
        Tick[currentCheckListNo].SetActive(false);

        StartCoroutine(SetReadyAfterDelay());
    }

    [ContextMenu("SkipTick")]
    public void SkipTick()
    {
        if (!gameObject.activeInHierarchy) return;

        if (!isReady || currentCheckListNo >= Tick.Count - 1)
        {
            Debug.LogWarning("Cannot skip, at end.");
            return;
        }

        // Skip current and activate next
        currentCheckListNo++;
        Tick[currentCheckListNo].SetActive(true);

        currentCheckListNo++;  // move pointer forward again

        StartCoroutine(SetReadyAfterDelay());
    }

    IEnumerator SetReadyAfterDelay()
    {
        isReady = false;
        yield return new WaitForSeconds(0.5f);
        isReady = true;
    }

    [ContextMenu("ResetChecklist")]
    public void ResetChecklist()
    {
        foreach (var t in Tick)
            t.SetActive(false);

        currentCheckListNo = 0;
        isReady = true;
    }
}
