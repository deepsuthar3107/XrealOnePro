using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InspectionCheckList : MonoBehaviour
{
    public GameObject CheckList;

    private List<GameObject> ticks = new List<GameObject>();
    private int currentIndex = 0;
    private bool isReady = true;

    private void Awake()
    {
        // Collect all children named "Tick"
        foreach (var child in CheckList.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Tick")
                ticks.Add(child.gameObject);
        }

        // Disable all ticks
        foreach (var t in ticks)
            t.SetActive(false);

        // Checklist object hidden initially
        gameObject.SetActive(false);
    }

    // ----------------------------
    //       NEXT
    // ----------------------------
    [ContextMenu("DoNextTick")]
    public void DoNextTick()
    {
        if (!gameObject.activeInHierarchy) return;
        if (!isReady || currentIndex >= ticks.Count) return;

        ticks[currentIndex].SetActive(true);
        currentIndex++;

        StartCoroutine(SetReadyAfterDelay());
    }

    // ----------------------------
    //     PREVIOUS
    // ----------------------------
    [ContextMenu("DoPreviousTick")]
    public void DoPreviousTick()
    {
        if (!gameObject.activeInHierarchy) return;
        if (!isReady || currentIndex <= 0) return;

        currentIndex--;
        ticks[currentIndex].SetActive(false);

        StartCoroutine(SetReadyAfterDelay());
    }

    // ----------------------------
    //         SKIP
    // ----------------------------
    [ContextMenu("SkipTick")]
    public void SkipTick()
    {
        if (!gameObject.activeInHierarchy) return;
        if (!isReady || currentIndex >= ticks.Count - 1) return;

        // Skip ONE element properly
        currentIndex++; // move to next
        ticks[currentIndex].SetActive(true); // tick the next item
        currentIndex++; // move pointer forward

        StartCoroutine(SetReadyAfterDelay());
    }

    // ----------------------------
    //        RESET
    // ----------------------------
    [ContextMenu("ResetChecklist")]
    public void ResetChecklist()
    {
        foreach (var t in ticks)
            t.SetActive(false);

        currentIndex = 0;
        isReady = true;
    }

    // ----------------------------
    //  DELAY HANDLER
    // ----------------------------
    IEnumerator SetReadyAfterDelay()
    {
        isReady = false;
        yield return new WaitForSeconds(0.5f);
        isReady = true;
    }

    // ----------------------------
    //  POSITION IN FRONT OF CAMERA
    // ----------------------------
    public void setPosition()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // Position 1.5m in front of camera
        transform.position = cam.transform.position + cam.transform.forward * 1.5f;

        // Rotate to face the camera properly
        transform.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
    }
}
