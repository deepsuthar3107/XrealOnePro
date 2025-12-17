using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InspectionCheckList : MonoBehaviour
{
    #region Serialized Fields
    [Header("References")]
    [SerializeField] private GameObject checkList;
    [SerializeField] private GameObject voiceCommandGuide;
    #endregion

    #region Constants
    private const float INPUT_DELAY = 0.1f;
    private const float CHECKLIST_DISTANCE = 1f;
    private const float VOICE_GUIDE_DISTANCE = 0.8f;
    private const float VOICE_GUIDE_OFFSET = -0.1f;
    private const float CHECKLIST_OFFSET = 0.1f;
    private const float OVERLAP_THRESHOLD = 0.2f;
    #endregion

    #region Private Fields
    private List<GameObject> ticks = new List<GameObject>();
    private List<GameObject> selections = new List<GameObject>();
    private int currentIndex = 0;
    private bool isReady = true;
    private Camera mainCamera;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        mainCamera = Camera.main;
        InitializeChecklistElements();
        ResetChecklist();
        checkList.SetActive(false);
        InitializeVoiceCommandUI();
        Invoke(nameof(InitializeVoiceCommandUI), 1);
    }
    #endregion

    #region Initialization
    private void InitializeVoiceCommandUI()
    {
        voiceCommandGuide.SetActive(true);
        SetInitialVoiceCommandPosition();
    }

    private void SetInitialVoiceCommandPosition()
    {
        if (mainCamera == null) return;

        Transform camTransform = mainCamera.transform;
        voiceCommandGuide.transform.position = camTransform.position + camTransform.forward * VOICE_GUIDE_DISTANCE;

        Vector3 lookDir = voiceCommandGuide.transform.position - camTransform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude > 0.001f)
            voiceCommandGuide.transform.rotation = Quaternion.LookRotation(lookDir);
    }

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

        if (ticks.Count != selections.Count)
            Debug.LogWarning($"Mismatch: {ticks.Count} ticks vs {selections.Count} selections");
    }
    #endregion

    #region Public API
    public void ProcessCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return;

        command = command.ToLower().Trim();

        // Visibility
        if (IsShowCommand(command)) { ShowChecklist(); return; }
        if (IsHideCommand(command)) { HideChecklist(); return; }

        // Voice UI
        if (IsShowVoiceCommandUI(command)) { ShowVoiceCommandUI(); return; }
        if (IsHideVoiceCommandUI(command)) { HideVoiceCommandUI(); return; }

        // Navigation
        if (IsNextCommand(command)) { DoNextTick(); return; }
        if (IsPreviousCommand(command)) { DoPreviousTick(); return; }

        // Marking
        if (IsCheckCommand(command)) { MarkCurrentItem(); return; }
        if (IsUncheckCommand(command)) { UnmarkCurrentItem(); return; }

        // Status
        if (IsPassCommand(command)) { MarkApprove(); return; }
        if (IsWarningCommand(command)) { MarkWarning(); return; }
        if (IsFailCommand(command)) { MarkFailed(); return; }
    }

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

    [ContextMenu("Reset Checklist")]
    public void ResetChecklist()
    {
        if (!checkList.activeInHierarchy) return;

        ticks.ForEach(t => t.SetActive(false));
        selections.ForEach(s => s.SetActive(false));

        if (selections.Count > 0)
            selections[0].SetActive(true);

        currentIndex = 0;
        isReady = true;
    }

    public void MarkCurrentItem() => SetTickState(true, Color.green);
    public void UnmarkCurrentItem() => SetTickState(false, Color.green);
    public void MarkApprove() => SetTickState(true, Color.green);
    public void MarkWarning() => SetTickState(true, Color.yellow);
    public void MarkFailed() => SetTickState(true, Color.red);
    #endregion

    #region Command Recognition
    private bool IsShowCommand(string cmd) =>
        cmd == "show checklist" || cmd == "view checklist" || cmd == "open checklist" || cmd == "checklist";

    private bool IsHideCommand(string cmd) =>
        cmd == "hide checklist" || cmd == "dismiss checklist" || cmd == "close checklist";

    private bool IsShowVoiceCommandUI(string cmd) =>
        cmd == "show voice command" || cmd == "open voice command" || cmd == "open voicecommand" || cmd == "show voicecommand";

    private bool IsHideVoiceCommandUI(string cmd) =>
        cmd == "hide voice command" || cmd == "close voice command" || cmd == "hide voicecommand" || cmd == "close voicecommand" || cmd == "close";

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
        SetChecklistPosition();
    }

    private void HideChecklist() => checkList.SetActive(false);
    private void ShowVoiceCommandUI()
    {
        voiceCommandGuide.SetActive(true);
        SetPositionVoiceCommandUI();
    }
    private void HideVoiceCommandUI() => voiceCommandGuide.SetActive(false);
    #endregion

    #region Internal Logic
    private bool CanNavigate() =>
        gameObject.activeInHierarchy && isReady && selections.Count > 0;

    private bool IsValidCurrentIndex() =>
        isReady && currentIndex >= 0 && currentIndex < ticks.Count;

    private void SetTickState(bool active, Color color)
    {
        if (!IsValidCurrentIndex()) return;

        GameObject tick = ticks[currentIndex];
        tick.SetActive(active);

        Image tickImage = tick.GetComponent<Image>();
        if (tickImage != null)
            tickImage.color = color;
    }
    #endregion

    #region Positioning
    private void SetChecklistPosition()
    {
        if (mainCamera == null) return;

        Transform camTransform = mainCamera.transform;
        checkList.transform.position = camTransform.position +
            camTransform.forward * CHECKLIST_DISTANCE +
            camTransform.right * CHECKLIST_OFFSET;
        checkList.transform.rotation = Quaternion.LookRotation(camTransform.forward, Vector3.up);

        if (voiceCommandGuide.activeInHierarchy && IsOverlapping())
            SetPositionVoiceCommandUI();
    }

    private void SetPositionVoiceCommandUI()
    {
        if (mainCamera == null) return;

        Transform camTransform = mainCamera.transform;
        voiceCommandGuide.transform.position = camTransform.position +
            camTransform.forward * VOICE_GUIDE_DISTANCE +
            camTransform.right * VOICE_GUIDE_OFFSET;

        Vector3 lookDir = voiceCommandGuide.transform.position - camTransform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude > 0.001f)
            voiceCommandGuide.transform.rotation = Quaternion.LookRotation(lookDir);
    }

    private bool IsOverlapping()
    {
        Vector2 checklistPos = new Vector2(checkList.transform.position.x, checkList.transform.position.y);
        Vector2 voiceGuidePos = new Vector2(voiceCommandGuide.transform.position.x, voiceCommandGuide.transform.position.y);
        return Vector2.Distance(checklistPos, voiceGuidePos) < OVERLAP_THRESHOLD;
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