using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;
using Whisper;
using Whisper.Utils;

public class StreamingSampleMic : MonoBehaviour
{
    [Header("Whisper")]
    public WhisperManager whisper;
    public MicrophoneRecord microphoneRecord;

    [Header("UI")]
    public Button button;
    public Text buttonText;
    public TextMeshProUGUI text;
    public ScrollRect scroll;

    [Header("Commands")]
    public CommandData[] CommandData;

    private WhisperStream stream;

    private async void Start()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }

            await CreateNewStream();

        microphoneRecord.OnRecordStop += OnRecordStop;
        button.onClick.AddListener(OnButtonPressed);
    }

    private async System.Threading.Tasks.Task CreateNewStream()
    {
        // Stop previous stream if it exists
        if (stream != null)
            stream.StopStream();

        // Create a fresh one
        stream = await whisper.CreateStream(microphoneRecord);

        // Reassign callbacks
        stream.OnSegmentFinished += OnSegmentFinished;
        stream.OnStreamFinished += OnStreamFinished;
    }


    private async void OnButtonPressed()
    {
        if (!microphoneRecord.IsRecording)
        {
            await CreateNewStream();

            stream.StartStream();
            microphoneRecord.StartRecord();
            buttonText.text = "Stop";
        }
        else
        {
            microphoneRecord.StopRecord();
        }
    }

    private void OnRecordStop(AudioChunk recordedAudio)
    {
        buttonText.text = "Record";
    }

    private void OnSegmentFinished(WhisperResult result)
    {
        if (string.IsNullOrEmpty(result.Result)) return;

        text.text = result.Result;
        UiUtils.ScrollDown(scroll);

        HandleCommands(result.Result);
    }

    private void OnStreamFinished(string finalResult)
    {
        Debug.Log("Mic stream finished.");
    }

    // --------------------------------------------------------------
    // COMMAND HANDLING
    // --------------------------------------------------------------

    private void HandleCommands(string raw)
    {
        string spoken = Normalize(raw);

        foreach (var data in CommandData)
        {
            foreach (string cmd in data.commands)
            {
                if (string.IsNullOrEmpty(cmd))
                    continue;

                string normalizedCmd = Normalize(cmd);

                // Exact or contained match
                if (spoken.Contains(normalizedCmd))
                {
                    Debug.Log($"Command matched (exact): {cmd}");
                    data.Event?.Invoke();
                    return;
                }

                // Optional fuzzy matching (tolerates slight errors)
                if (IsFuzzyMatch(spoken, normalizedCmd))
                {
                    Debug.Log($"Command matched (fuzzy): {cmd}");
                    data.Event?.Invoke();
                    return;
                }
            }
        }
    }

    // Normalize text for consistent matching
    private string Normalize(string input)
    {
        string lower = input.ToLower();

        // Remove punctuation
        lower = Regex.Replace(lower, "[^a-z0-9 ]+", " ");

        // Remove multiple spaces
        lower = Regex.Replace(lower, " +", " ").Trim();

        return lower;
    }

    // Fuzzy matching for near-matches ("jump", "jum", "jump now", "jumping")
    private bool IsFuzzyMatch(string spoken, string command)
    {
        // Very simple rule: spoken contains a word with small edit distance to command
        string[] words = spoken.Split(' ');

        foreach (string w in words)
        {
            int dist = LevenshteinDistance(w, command);

            if (dist <= 1) return true; // allow 1-letter error
        }

        return false;
    }

    // Levenshtein Distance algorithm
    private int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b.Length;
        if (string.IsNullOrEmpty(b)) return a.Length;

        int[,] d = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;

                d[i, j] = Mathf.Min(
                    d[i - 1, j] + 1,
                    d[i, j - 1] + 1,
                    d[i - 1, j - 1] + cost
                );
            }
        }
        return d[a.Length, b.Length];
    }
}
