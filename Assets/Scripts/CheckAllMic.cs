using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CheckAllMic : MonoBehaviour
{
    public TextMeshProUGUI debugText;
    void Start()
    {
      
    }

    // Update is called once per frame
    void Update()
    {
        foreach (var m in Microphone.devices)
        {
            debugText.text = "Mic List:" + m.ToString();
        }
    }
}
