using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    [SerializeField] private GameObject MPI_Info;
    [SerializeField] private GameObject voiceCommandGuide;
   
    [Header("Voice Matching Settings")]
    [SerializeField] private int requiredWordMatch = 2;

    [Header("Two Options")]
    [SerializeField] private GameObject OptionUI;
    [SerializeField] private TextMeshProUGUI Count;
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
    private readonly List<GameObject> ticks = new();
    private readonly List<GameObject> selections = new();
    private readonly List<TextMeshProUGUI> checklistItemsText = new();

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
        voiceCommandGuide.SetActive(false);
    }

    private void Start()
    {
        waitCoroutine = StartCoroutine(WaitForOptions());
    }
    #endregion

    #region Initialization
    private IEnumerator WaitForOptions()
    {
        float time = 6f;
        OptionUI.SetActive(true);

        while (time > 0)
        {
            Count.text = Mathf.CeilToInt(time).ToString();
            time -= Time.deltaTime;
            yield return null;
        }

        OptionUI.SetActive(false);
        yield return new WaitForSeconds(1f);
        voiceCommandGuide.SetActive(true);
        SetPositionVoiceCommandUI();
    }

    private void StopCoroutineSafe()
    {
        if (waitCoroutine != null)
            StopCoroutine(waitCoroutine);
    }

    private void InitializeChecklistElements()
    {
        foreach (Transform child in checkList.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Tick") ticks.Add(child.gameObject);
            
            if (child.name == "selection") selections.Add(child.gameObject);
           
            if (child.name == "TXT" && child.TryGetComponent(out TextMeshProUGUI txt))
                checklistItemsText.Add(txt);
        }
    }
    #endregion

    #region Command Processing
    public void ProcessCommand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string cmd = NormalizeText(raw);

        bool hasStatus = HasAnyStatus(cmd);
        int matchedIndex = FindConfidentChecklistItemIndex(cmd);

        if (IsShowCommand(cmd)) { ShowChecklist(); return; }
        else if (IsHideCommand(cmd)) { HideChecklist(); return; }
        else if (IsShowROCommand(cmd)) { ShowRO(); return; }
        else if (IsHideROCommand(cmd)) { HideRO(); return; }
        else if (IsShowMPICommand(cmd)) { ShowMPI(); return; }
        else if (IsHideMPICommand(cmd)) { HideMPI(); return; }
        else if (IsShowVoiceCommandUI(cmd)) { StopCoroutineSafe(); ShowVoiceCommandUI(); return; }
        else if (IsHideVoiceCommandUI(cmd)) { HideVoiceCommandUI(); return; }
        else if (IsNextCommand(cmd)) { DoNextSelection(); return; }
        else if (IsPreviousCommand(cmd)) { DoPreviousSelection(); return; }
        else if (matchedIndex != -1)
        {
            SelectItem(matchedIndex);
            if (hasStatus)
                ApplyStatus(cmd);
            return;
        }
        /* else if (hasStatus && matchedIndex == -1)
         {
             ApplyStatus(cmd);
         }*/
        else if (hasStatus && IsOnlyStatusCommand(cmd))
        {
            ApplyStatus(cmd);
            return;
        }

    }
    #endregion

    #region Matching (STRONG & DETERMINISTIC)
    private int FindConfidentChecklistItemIndex(string cmd)
    {
        string[] cmdWords = cmd.Split(' ');

        for (int i = 0; i < checklistItemsText.Count; i++)
        {
            string item = NormalizeText(checklistItemsText[i].text);
            string[] itemWords = item.Split(' ');

            if (itemWords.Length == 0) continue;

     
            if (!cmdWords.Contains(itemWords[0]))
                continue;

            int matchCount = 0;

            // Count unique matching words
            foreach (string iw in itemWords)
            {
                if (cmdWords.Contains(iw))
                    matchCount++;
            }

            int required = Mathf.Min(requiredWordMatch, itemWords.Length);

            if (matchCount >= required)
                return i;
        }

        return -1;
    }

    #endregion

    #region Status
    private void ApplyStatus(string cmd)
    {
        if (IsUncheckCommand(cmd)) UnmarkCurrentItem();
        else if (IsFailCommand(cmd)) Mark(Color.red);
        else if (IsWarningCommand(cmd)) Mark(Color.yellow);
        else if (IsCheckCommand(cmd) || IsPassCommand(cmd)) Mark(Color.green);
    }

    public void changeTickColor(int colorNo)
    {
        foreach (var t in ticks)
        {
            if(!t.activeInHierarchy)
            {
                switch (colorNo)
                {
                    case 1:
                        t.GetComponent<Image>().color = Color.green;
                        break;

                    case 2:
                        t.GetComponent<Image>().color = Color.yellow;
                        break;

                    case 3:
                        t.GetComponent<Image>().color = Color.red;
                        break;
                }
            }   
        }
    }
    private void Mark(Color c)
    {
        ticks[currentIndex].SetActive(true);
        if (ticks[currentIndex].TryGetComponent(out Image img))
            img.color = c;
    }

    private void UnmarkCurrentItem()
    {
        ticks[currentIndex].SetActive(false);
    }
    #endregion

    #region Navigation
    private void SelectItem(int index)
    {
        if (index == currentIndex) return;

        selections[currentIndex].SetActive(false);
        currentIndex = index;
        selections[currentIndex].SetActive(true);
    }

    public void DoNextSelection()
    {
        if (!CanNavigate() || currentIndex >= selections.Count - 1) return;
        SelectItem(currentIndex + 1);
        StartCoroutine(InputCooldown());
    }

    public void DoPreviousSelection()
    {
        if (!CanNavigate() || currentIndex <= 0) return;
        SelectItem(currentIndex - 1);
        StartCoroutine(InputCooldown());
    }

    private IEnumerator InputCooldown()
    {
        isReady = false;
        yield return new WaitForSeconds(INPUT_DELAY);
        isReady = true;
    }

    private bool CanNavigate() =>
        gameObject.activeInHierarchy && isReady && selections.Count > 0;
    #endregion

    #region Visibility & Positioning
    public void ShowChecklist()
    {
        OptionUI.SetActive(false);
        checkList.SetActive(true);
       
        SetChecklistPosition();
        HideRO();
        HideMPI();
    }

    public void HideChecklist() => checkList.SetActive(false);

    public void ShowRO()
    {
        OptionUI.SetActive(false);
        repairOrder.SetActive(true);
       
        SetRepairOrderPosition();
        HideChecklist();
        HideMPI();
    }  
    public void HideRO() => repairOrder.SetActive(false);
    public void ShowMPI()
    {
        OptionUI.SetActive(false);
        MPI_Info.SetActive(true);

        SetMPI_Info_Position();
        HideChecklist();
        HideRO();
    }

    public void HideMPI() => MPI_Info.SetActive(false);
    public void ShowVoiceCommandUI()
    {
        OptionUI.SetActive(false);
        voiceCommandGuide.SetActive(true);
        
        SetPositionVoiceCommandUI();
    }

    public void HideVoiceCommandUI() => voiceCommandGuide.SetActive(false);
    #endregion

    #region Positioning
    private void SetChecklistPosition()
    {
        if (mainCamera == null) return;

        /*Transform camTransform = mainCamera.transform;
        checkList.transform.position = camTransform.position +
            camTransform.forward * CHECKLIST_DISTANCE +
            camTransform.right * CHECKLIST_OFFSET;
        checkList.transform.rotation = Quaternion.LookRotation(camTransform.forward, Vector3.up);*/

        if (voiceCommandGuide.activeInHierarchy && IsOverlapping(checkList.transform, voiceCommandGuide.transform))
            SetPositionVoiceCommandUI();

        /*if (repairOrder.activeInHierarchy && IsOverlapping(checkList.transform, repairOrder.transform))
            HideRO();*/
    }
    private void SetRepairOrderPosition()
    {
        if (mainCamera == null) return;

       /* Transform camTransform = mainCamera.transform;
        repairOrder.transform.position = camTransform.position +
            camTransform.forward * CHECKLIST_DISTANCE +
            camTransform.right * CHECKLIST_OFFSET;
        repairOrder.transform.rotation = Quaternion.LookRotation(camTransform.forward, Vector3.up);*/

        if (voiceCommandGuide.activeInHierarchy && IsOverlapping(repairOrder.transform, voiceCommandGuide.transform))
            SetPositionVoiceCommandUI();

       /* if (checkList.activeInHierarchy && IsOverlapping(repairOrder.transform, checkList.transform))
            HideChecklist();*/
    }
    private void SetMPI_Info_Position()
    {
        if (mainCamera == null) return;

        /* Transform camTransform = mainCamera.transform;
         MPI_Info.transform.position = camTransform.position +
             camTransform.forward * CHECKLIST_DISTANCE +
             camTransform.right * CHECKLIST_OFFSET;
         MPI_Info.transform.rotation = Quaternion.LookRotation(camTransform.forward, Vector3.up);*/

        if (voiceCommandGuide.activeInHierarchy && IsOverlapping(MPI_Info.transform, voiceCommandGuide.transform))
            SetPositionVoiceCommandUI();

        /* if (MPI_Info.activeInHierarchy && IsOverlapping(MPI_Info.transform, checkList.transform))
             HideChecklist();*/
    }
    private void SetPositionVoiceCommandUI()
    {
        if (mainCamera == null) return;
/*
        if (voiceCommandGuide.transform.parent != null) {
            voiceCommandGuide.transform.parent = null;
        }

        Transform camTransform = mainCamera.transform;
        voiceCommandGuide.transform.position = camTransform.position +
            camTransform.forward * VOICE_GUIDE_DISTANCE +
            camTransform.right * VOICE_GUIDE_OFFSET;

        Vector3 lookDir = voiceCommandGuide.transform.position - camTransform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude > 0.001f)
            voiceCommandGuide.transform.rotation = Quaternion.LookRotation(lookDir);*/
    }

    private bool IsOverlapping(Transform target1, Transform target2)
    {
        Vector2 T1 = new Vector2(target1.transform.position.x, target1.transform.position.y);
        Vector2 T2 = new Vector2(target2.transform.position.x, target2.transform.position.y);
        return Vector2.Distance(T1, T2) < OVERLAP_THRESHOLD;
    }
    #endregion


    #region NLP 
    private bool HasAnyStatus(string cmd) =>
        IsCheckCommand(cmd) || IsPassCommand(cmd) ||
        IsWarningCommand(cmd) || IsFailCommand(cmd) || IsUncheckCommand(cmd);

    private bool IsOnlyStatusCommand(string cmd)
    {
        string[] words = cmd.Split(' ');

        foreach (string w in words)
        {
            if (
                w != "mark" && w != "check" && w != "done" &&
                w != "pass" && w != "green" && w != "good" &&
                w != "fail" && w != "red" && w != "bad" && w != "bed" &&
                w != "warning" && w != "yellow" && w != "caution" &&
                w != "unmark" && w != "unmarked" && w != "uncheck"
            )
            {
                return false;
            }
        }
        return true;
    }

    private bool IsShowCommand(string cmd) =>
        cmd.Contains("show checklist") || cmd.Contains("open checklist") || cmd.Contains("view checklist") || cmd == "checklist";

    private bool IsHideCommand(string cmd) =>
        cmd.Contains("hide checklist") || cmd.Contains("close checklist") || cmd.Contains("dismiss checklist");
   
    private bool IsShowMPICommand(string cmd) =>
       cmd.Contains("show mpi") || cmd.Contains("open mpi");

    private bool IsHideMPICommand(string cmd) =>
        cmd.Contains("hide mpi") || cmd.Contains("close mpi");
  
    private bool IsShowVoiceCommandUI(string cmd) =>
        cmd.Contains("open help") || cmd.Contains("show help");

    private bool IsHideVoiceCommandUI(string cmd) =>
        cmd.Contains("close help") || cmd.Contains("hide help") || cmd == "close";

    private bool IsShowROCommand(string cmd) =>
        cmd.Contains("show repair order") || cmd.Contains("open repair order") || cmd.Contains("so repair order")
        || cmd.Contains("show ro")  || cmd.Contains("open ro") 
        || cmd.Contains("so ro") || cmd.Contains("so arrow") || cmd.Contains("open arrow")
        || cmd.Contains("so r o") || cmd.Contains("open r o");

    private bool IsHideROCommand(string cmd) =>
        cmd.Contains("hide repair order") || cmd.Contains("close repair order") 
        || cmd.Contains("hide ro")  || cmd.Contains("close ro")
        || cmd.Contains("hide arrow") || cmd.Contains("close arrow") 
        || cmd.Contains("close r o") || cmd.Contains("hide r o") ;

    private bool IsNextCommand(string cmd) =>
        cmd.Contains("next") || cmd.Contains("forward") || cmd.Contains("down");

    private bool IsPreviousCommand(string cmd) =>
        cmd.Contains("previous") || cmd.Contains("back") || cmd.Contains("up");

    private bool IsCheckCommand(string cmd) =>
        cmd.Contains("check") || cmd.Contains("mark") || cmd.Contains("done");

    private bool IsUncheckCommand(string cmd) =>
        cmd.Contains("uncheck") || cmd.Contains("unmark") || cmd.Contains("unmarked");

    private bool IsPassCommand(string cmd) =>
        cmd.Contains("pass") || cmd.Contains("green") || cmd.Contains("good");

    private bool IsWarningCommand(string cmd) =>
        cmd.Contains("warning") || cmd.Contains("yellow") || cmd.Contains("caution");

    private bool IsFailCommand(string cmd) =>
        cmd.Contains("fail") || cmd.Contains("red") || cmd.Contains("bad") || cmd.Contains("bed");
    #endregion

    #region Utils
    private string NormalizeText(string t)
    {
        if (string.IsNullOrEmpty(t)) return "";

        t = Regex.Replace(t, @"\([^)]*\)", "");
        t = Regex.Replace(t, @"[^a-zA-Z0-9\s]", "");
        t = Regex.Replace(t, @"\b&\b", "and");
        t = Regex.Replace(t, @"\bwheel\b", "will");
        t = Regex.Replace(t, @"\btire\b", "tyre");
        t = Regex.Replace(t, @"\bbreak\b", "brake");
        t = Regex.Replace(t, @"\bviper\b", "wiper");
        return Regex.Replace(t, @"\s+", " ").Trim().ToLower();
    }

    private void ResetChecklist()
    {
        foreach (var t in ticks) t.SetActive(false);
        foreach (var s in selections) s.SetActive(false);

        currentIndex = 0;
        if (selections.Count > 0)
            selections[0].SetActive(true);

        isReady = true;
    }
    #endregion
}