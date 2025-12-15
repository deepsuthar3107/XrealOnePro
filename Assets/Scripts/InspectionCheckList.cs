using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InspectionCheckList : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject checkList;

    [Header("Runtime State")]
    private List<GameObject> ticks = new List<GameObject>();
    private List<GameObject> selections = new List<GameObject>();
    int currentIndex = 0;
    private bool isReady = true;

    private const float INPUT_DELAY = 0.25f;
    private const float CHECKLIST_DISTANCE = 1.5f;

    #region Unity Lifecycle

    private void Awake()
    {
        InitializeChecklistElements();
        ResetChecklist();
        checkList.SetActive(false);
    }

    #endregion

    #region Initialization

    private void InitializeChecklistElements()
    {
        Transform[] children = checkList.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child.name == "Tick")
                ticks.Add(child.gameObject);
            else if (child.name == "selection")
                selections.Add(child.gameObject);
        }

        // Validate that we have matching counts
        if (ticks.Count != selections.Count)
        {
            Debug.LogWarning($"Mismatch between ticks ({ticks.Count}) and selections ({selections.Count})");
        }
    }

    #endregion

    #region Command Processing

    public void ProcessCommand(string command)
    {
        // Visibility commands
        if (IsShowCommand(command))
        {
            ShowChecklist();
            return;
        }
        else if (IsHideCommand(command))
        {
            HideChecklist();
            return;
        }
        // Navigation commands
        else if (IsNextCommand(command))
        {
            DoNextTick();
            return;
        }
        else if (IsPreviousCommand(command))
        {
            DoPreviousTick();
            return;
        }
        // Marking commands
        else if (IsCheckCommand(command))
        {
            MarkCurrentItem();
            return;
        }
        else if (IsUncheckCommand(command))
        {
            UnmarkCurrentItem();
            return;
        }
        // Status commands
        else if (IsPassCommand(command))
        {
            MarkApprove();
            return;
        }
        else if (IsWarningCommand(command))
        {
            MarkWarning();
            return;
        }
        else if (IsFailCommand(command))
        {
            MarkFailed();
            return;
        }
    }

    private bool IsShowCommand(string cmd) =>
        cmd == "show checklist" || cmd == "view checklist" || cmd == "open checklist" || cmd == "checklist";

    private bool IsHideCommand(string cmd) =>
        cmd == "hide checklist" || cmd == "dismiss checklist" || cmd == "close checklist";

    private bool IsNextCommand(string cmd) =>
        cmd == "next" || cmd == "forward" || cmd == "down";

    private bool IsPreviousCommand(string cmd) =>
        cmd == "previous" || cmd == "back" || cmd == "up" || cmd == "prev";

    private bool IsCheckCommand(string cmd) =>
        cmd == "check" || cmd == "mark" || cmd == "done";

    private bool IsUncheckCommand(string cmd) =>
        cmd == "uncheck" || cmd == "unmark" || cmd == "unmarked";

    private bool IsPassCommand(string cmd) =>
        cmd == "pass" || cmd == "green" || cmd == "good";

    private bool IsWarningCommand(string cmd) =>
        cmd == "warning" || cmd == "yellow" || cmd == "caution";

    private bool IsFailCommand(string cmd) =>
        cmd == "fail" || cmd == "red" || cmd == "bad";

    #endregion

    #region Visibility Control

    private void ShowChecklist()
    {
        checkList.SetActive(true);
        SetPosition();
    }

    private void HideChecklist()
    {
        checkList.SetActive(false);
    }

    #endregion

    #region Navigation

    [ContextMenu("Next Item")]
    public void DoNextTick()
    {
        if (!CanNavigate() || currentIndex >= selections.Count - 1) return;
        selections[currentIndex].SetActive(false);
        currentIndex++;
        selections[currentIndex].SetActive(true);

        StartCoroutine(SetReadyAfterDelay());
    }

    [ContextMenu("Previous Item")]
    public void DoPreviousTick()
    {
        if (!CanNavigate() || currentIndex <= 0) return;

        selections[currentIndex].SetActive(false);
        currentIndex--;
        selections[currentIndex].SetActive(true);

        StartCoroutine(SetReadyAfterDelay());
    }

    private bool CanNavigate()
    {
        return gameObject.activeInHierarchy && isReady && selections.Count > 0;
    }

    #endregion

    #region Item Marking

    public void MarkCurrentItem()
    {
        SetTickState(true, Color.green);
    }

    public void UnmarkCurrentItem()
    {
        SetTickState(false, Color.green);
    }

    public void MarkApprove()
    {
        SetTickState(true, Color.green);
    }

    public void MarkWarning()
    {
        SetTickState(true, Color.yellow);
    }

    public void MarkFailed()
    {
        SetTickState(true, Color.red);
    }

    private void SetTickState(bool active, Color color)
    {
        if (!IsValidCurrentIndex()) return;

        GameObject tick = ticks[currentIndex];
        tick.SetActive(active);

        Image tickImage = tick.GetComponent<Image>();
        if (tickImage != null)
            tickImage.color = color;
    }

    private bool IsValidCurrentIndex()
    {
        return isReady && currentIndex >= 0 && currentIndex < ticks.Count;
    }

    #endregion

    #region Reset

    [ContextMenu("Reset Checklist")]
    public void ResetChecklist()
    {
        if (!checkList.activeInHierarchy) return;

        foreach (GameObject tick in ticks)
            tick.SetActive(false);

        foreach (GameObject selection in selections)
            selection.SetActive(false);

        if (selections.Count > 0)
            selections[0].SetActive(true);

        currentIndex = 0;
        isReady = true;
    }

    #endregion

    #region Positioning

    private void SetPosition()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        transform.position = cam.transform.position + cam.transform.forward * CHECKLIST_DISTANCE;
        transform.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
    }

    #endregion

    #region Coroutines

    private IEnumerator SetReadyAfterDelay()
    {
        isReady = false;
        yield return new WaitForSeconds(INPUT_DELAY);
        isReady = true;
    }

    #endregion
}