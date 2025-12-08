using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InspectionCheckList : MonoBehaviour
{
    public GameObject CheckList;
    List<GameObject> Tick = new List<GameObject>();
    int currentCheckListNo;

    private void Awake()
    {
        foreach(var cl in CheckList.GetComponentsInChildren<Transform>(true))
        {
            if(cl.name == "Tick")
            {
                Tick.Add(cl.gameObject);
            }
        }

        foreach(var t  in Tick)
        {
            if (t != null)
                t.SetActive(false);
        }
        gameObject.SetActive(false);
    }

    [ContextMenu("DoNextTick")]
    public void DoNextTick()
    {
        if (!gameObject.activeInHierarchy) return;
        Tick[currentCheckListNo].SetActive(true);
        if (currentCheckListNo < Tick.Count - 1)
        {
            currentCheckListNo++;
        }
    }
}
