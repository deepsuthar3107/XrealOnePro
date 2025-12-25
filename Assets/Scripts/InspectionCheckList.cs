using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InspectionCheckList : MonoBehaviour
{
    #region Serialized Fields
    [Header("References")]
    [SerializeField] private GameObject checkList;
    [SerializeField] private GameObject repairOrder;
    [SerializeField] private GameObject voiceCommandGuide;

    [Header("Two Options")]
    public GameObject OptionUI;
    public TextMeshProUGUI Count;
    #endregion

    #region Constants
    private const float INPUT_DELAY = 0.1f;
    private const float CHECKLIST_DISTANCE = 1f;
    private const float VOICE_GUIDE_DISTANCE = 1f;
    private const float VOICE_GUIDE_OFFSET = -0.2f;
    private const float CHECKLIST_OFFSET = 0.15f;
    private const float OVERLAP_THRESHOLD = 0.4f;
    #endregion

    #region Private Fields
    private List<GameObject> ticks = new List<GameObject>();
    private List<GameObject> selections = new List<GameObject>();
    public List<TextMeshProUGUI> checklistItemsText = new List<TextMeshProUGUI>();

    private int currentIndex = 0;
    private bool isReady = true;
    private Camera mainCamera;
    private Coroutine waitCoroutine;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        mainCamera = Camera.main;
        InitializeChecklistElements();
        ResetChecklist();
        checkList.SetActive(false);
        repairOrder.SetActive(false);
    }

    private void Start()
    {
        waitCoroutine = StartCoroutine(WaitForOptions());
    }
    #endregion

    #region Initialization
    IEnumerator WaitForOptions()
    {
        float countingTime = 6f;
        OptionUI.SetActive(true);

        while (countingTime > 0f)
        {
            countingTime -= Time.deltaTime;
            Count.text = Mathf.CeilToInt(countingTime).ToString();
            yield return null;
        }

        OptionUI.SetActive(false);
        yield return new WaitForSeconds(1f);
        voiceCommandGuide.SetActive(true);
    }

    public void StopCoroutineSafe()
    {
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
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

            if (child.name == "TXT" && child.GetComponent<TextMeshProUGUI>())
                checklistItemsText.Add(child.GetComponent<TextMeshProUGUI>());
        }
    }
    #endregion

    #region Public API
    public void ProcessCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        command = NormalizeText(command);


        // Visibility
        if (IsShowCommand(command)) { ShowChecklist(); return; }
        if (IsHideCommand(command)) { HideChecklist(); return; }
        if (IsShowROCommand(command)) { ShowRO(); return; }
        if (IsHideROCommand(command)) { HideRO(); return; }
        if (IsShowVoiceCommandUI(command)) { StopCoroutineSafe(); ShowVoiceCommandUI(); return; }
        if (IsHideVoiceCommandUI(command)) { HideVoiceCommandUI(); return; }

        // Navigation
        if (IsNextCommand(command)) { DoNextTick(); return; }
        if (IsPreviousCommand(command)) { DoPreviousTick(); return; }

        // 🎯 ITEM + STATUS BASED COMMANDS
        int itemIndex = FindChecklistItemIndex(command);
        if (itemIndex != -1)
        {
            SelectItem(itemIndex);

            if (IsUncheckCommand(command))
            {
                UnmarkCurrentItem();
                return;
            }

            if (IsFailCommand(command))
            {
                MarkFailed();
                return;
            }

            if (IsWarningCommand(command))
            {
                MarkWarning();
                return;
            }

            if (IsCheckCommand(command) || IsPassCommand(command))
            {
                MarkApprove();
                return;
            }
        }

        // Fallback (current selection)
        if (IsCheckCommand(command)) MarkCurrentItem();
        if (IsUncheckCommand(command)) UnmarkCurrentItem();
        if (IsPassCommand(command)) MarkApprove();
        if (IsWarningCommand(command)) MarkWarning();
        if (IsFailCommand(command)) MarkFailed();
    }
    #endregion

    #region Text Matching Helpers
    private string NormalizeText(string text)
    {
        text = Regex.Replace(text, @"\([^)]*\)", "");
        text = Regex.Replace(text, @"[^a-zA-Z0-9\s]", "");
        return Regex.Replace(text, @"\s+", " ").Trim().ToLower();
    }

    private int FindChecklistItemIndex(string command)
    {
        string cmd = NormalizeText(command);

        for (int i = 0; i < checklistItemsText.Count; i++)
        {
            string item = NormalizeText(checklistItemsText[i].text);
            if (cmd.Contains(item))
                return i;
        }
        return -1;
    }

    private void SelectItem(int index)
    {
        if (index < 0 || index >= selections.Count) return;

        selections[currentIndex].SetActive(false);
        currentIndex = index;
        selections[currentIndex].SetActive(true);
    }
    #endregion

    #region Navigation
    public void DoNextTick()
    {
        if (!CanNavigate() || currentIndex >= selections.Count - 1) return;
        selections[currentIndex].SetActive(false);
        currentIndex++;
        selections[currentIndex].SetActive(true);
        StartCoroutine(SetReadyAfterDelay());
    }

    public void DoPreviousTick()
    {
        if (!CanNavigate() || currentIndex <= 0) return;
        selections[currentIndex].SetActive(false);
        currentIndex--;
        selections[currentIndex].SetActive(true);
        StartCoroutine(SetReadyAfterDelay());
    }

    public void ResetChecklist()
    {
        ticks.ForEach(t => t.SetActive(false));
        selections.ForEach(s => s.SetActive(false));
        if (selections.Count > 0) selections[0].SetActive(true);
        currentIndex = 0;
        isReady = true;
    }
    #endregion

    #region Tick Control
    public void MarkCurrentItem() => SetTickState(true, Color.green);
    public void UnmarkCurrentItem()
    {
        if (currentIndex < 0 || currentIndex >= ticks.Count) return;
        ticks[currentIndex].SetActive(false);
    }

    public void MarkApprove() => SetTickState(true, Color.green);
    public void MarkWarning() => SetTickState(true, Color.yellow);
    public void MarkFailed() => SetTickState(true, Color.red);

    private void SetTickState(bool active, Color color)
    {
        if (currentIndex < 0 || currentIndex >= ticks.Count) return;

        GameObject tick = ticks[currentIndex];
        tick.SetActive(active);

        Image tickImage = tick.GetComponent<Image>();
        if (tickImage != null)
            tickImage.color = color;
    }

    #endregion


    #region Command Recognition (Boolean Based)
    private bool IsShowCommand(string cmd) =>
        cmd.Contains("show checklist") || cmd.Contains("view checklist") ||
        cmd.Contains("open checklist") || cmd == "checklist";

    private bool IsHideCommand(string cmd) =>
        cmd.Contains("hide checklist") || cmd.Contains("dismiss checklist") ||
        cmd.Contains("close checklist");

    private bool IsShowVoiceCommandUI(string cmd) =>
     cmd.Contains("show voice command") || cmd.Contains("open voice command") ||
     cmd.Contains("show voicecommand") || cmd.Contains("open voicecommand");

    private bool IsHideVoiceCommandUI(string cmd) =>
        cmd.Contains("hide voice command") || cmd.Contains("close voice command") ||
        cmd.Contains("hide voicecommand") || cmd.Contains("close voicecommand") || cmd == "close";

    private bool IsShowROCommand(string cmd) =>
    cmd.Contains("show repair order") || cmd.Contains("open repair order") ||
    cmd.Contains("show ro") || cmd.Contains("open ro");

    private bool IsHideROCommand(string cmd) =>
        cmd.Contains("hide repair order") || cmd.Contains("close repair order") ||
        cmd.Contains("close ro") || cmd.Contains("hide ro");

    private bool IsNextCommand(string cmd) =>
     cmd.Contains("next") || cmd.Contains("forward") || cmd.Contains("down");

    private bool IsPreviousCommand(string cmd) =>
        cmd.Contains("previous") || cmd.Contains("back") || cmd.Contains("up") || cmd.Contains("prev");

    private bool IsCheckCommand(string cmd) =>
      cmd.Contains("check") || cmd.Contains("mark") || cmd.Contains("done");

    private bool IsUncheckCommand(string cmd) =>
        cmd.Contains("uncheck") || cmd.Contains("unmark");

    private bool IsPassCommand(string cmd) =>
        cmd.Contains("pass") || cmd.Contains("green") || cmd.Contains("good");

    private bool IsWarningCommand(string cmd) =>
        cmd.Contains("warning") || cmd.Contains("yellow") || cmd.Contains("caution");

    private bool IsFailCommand(string cmd) =>
        cmd.Contains("fail") || cmd.Contains("red") || cmd.Contains("bad");

    #endregion

    #region Visibility
    private void ShowChecklist() 
    { 
        checkList.SetActive(true); 
        OptionUI.SetActive(false);

       // SetChecklistPosition();
        HideRO(); 
    }
    private void HideChecklist() => checkList.SetActive(false);
    private void ShowRO() 
    { 
        repairOrder.SetActive(true); 
        OptionUI.SetActive(false);

       // SetRepairOrdertPosition();
        HideChecklist(); 
    }
    private void HideRO() => repairOrder.SetActive(false);
    private void ShowVoiceCommandUI()
    {
        OptionUI.SetActive(false);
        voiceCommandGuide.SetActive(true);
      //  SetPositionVoiceCommandUI();
    }
    private void HideVoiceCommandUI() => voiceCommandGuide.SetActive(false);
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

        if (voiceCommandGuide.activeInHierarchy && IsOverlapping(checkList.transform, voiceCommandGuide.transform))
            SetPositionVoiceCommandUI();

        if (repairOrder.activeInHierarchy && IsOverlapping(checkList.transform, repairOrder.transform))
            HideRO();
    }
    private void SetRepairOrdertPosition()
    {
        if (mainCamera == null) return;

        Transform camTransform = mainCamera.transform;
        repairOrder.transform.position = camTransform.position +
            camTransform.forward * CHECKLIST_DISTANCE +
            camTransform.right * CHECKLIST_OFFSET;
        repairOrder.transform.rotation = Quaternion.LookRotation(camTransform.forward, Vector3.up);

        if (voiceCommandGuide.activeInHierarchy && IsOverlapping(repairOrder.transform, voiceCommandGuide.transform))
            SetPositionVoiceCommandUI();

        if (checkList.activeInHierarchy && IsOverlapping(repairOrder.transform, checkList.transform))
            HideChecklist();
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

    private bool IsOverlapping(Transform target1, Transform target2)
    {
        Vector2 T1 = new Vector2(target1.transform.position.x, target1.transform.position.y);
        Vector2 T2 = new Vector2(target2.transform.position.x, target2.transform.position.y);
        return Vector2.Distance(T1, T2) < OVERLAP_THRESHOLD;
    }
    #endregion

    #region Helpers
    private bool CanNavigate() => gameObject.activeInHierarchy && isReady && selections.Count > 0;


    private IEnumerator SetReadyAfterDelay()
    {
        isReady = false;
        yield return new WaitForSeconds(INPUT_DELAY);
        isReady = true;
    }
    #endregion
}
