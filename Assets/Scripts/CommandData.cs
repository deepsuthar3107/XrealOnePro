using System;
using UnityEngine.Events;

[Serializable]
public struct _CommandData 
{
    public string CommandGroupName;

    public string[] commands;
    public UnityEvent Event;
}
