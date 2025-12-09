using TMPro;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using UnityEngine.UI;
using Whisper.Utils;

namespace Whisper.Samples
{
    /// <summary>
    /// Stream transcription from microphone input.
    /// </summary>
    public class StreamingSampleMic : MonoBehaviour
    {
        public WhisperManager whisper;
        public MicrophoneRecord microphoneRecord;
    
        [Header("UI")] 
        public Button button;
        public Text buttonText;
        public TextMeshProUGUI text;
        public ScrollRect scroll;
        private WhisperStream _stream;
       
        public CommandData[] CommandData;
        private async void Start()
        {
            _stream = await whisper.CreateStream(microphoneRecord);
            _stream.OnResultUpdated += OnResult;
            _stream.OnSegmentUpdated += OnSegmentUpdated;
            _stream.OnSegmentFinished += OnSegmentFinished;
            _stream.OnStreamFinished += OnFinished;

            microphoneRecord.OnRecordStop += OnRecordStop;
            button.onClick.AddListener(OnButtonPressed);
        }

        public void OnButtonPressed()
        {
            if (!microphoneRecord.IsRecording)
            {
                _stream.StartStream();
                microphoneRecord.StartRecord();
            }
            else
                microphoneRecord.StopRecord();
        
            buttonText.text = microphoneRecord.IsRecording ? "Stop" : "Record";
        }
    
        private void OnRecordStop(AudioChunk recordedAudio)
        {
            buttonText.text = "Record";
        }

        private void OnResult(string result)
        {
            text.text = result;
            UiUtils.ScrollDown(scroll);

            if (string.IsNullOrEmpty(result)) return;

            result = result.ToLower();

            // Check all CommandData entries
            foreach (var data in CommandData)
            {
                foreach (var cmd in data.commands)
                {
                    if (string.IsNullOrEmpty(cmd)) continue;

                    string lowerCmd = cmd.ToLower();

                    // Match spoken text with command
                    if (result.Contains(lowerCmd))
                    {
                        Debug.Log($"Command matched: {data.CommandGroupName} → {cmd}");
                        data.Event?.Invoke();
                        return; // stop after first match
                    }
                }
            }
        }


        private void OnSegmentUpdated(WhisperResult segment)
        {
            print($"Segment updated: {segment.Result}");
        }
        
        private void OnSegmentFinished(WhisperResult segment)
        {
            print($"Segment finished: {segment.Result}");
        }
        
        private void OnFinished(string finalResult)
        {
            print("Stream finished!");
        }
    }
}
