using OpenAI;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using UnityEngine.Networking;

namespace Samples.Whisper
{
    public class Whisper : MonoBehaviour
    {
        [SerializeField] private Button recordButton;
        [SerializeField] private Image progressBar;
        [SerializeField] private Text message;
        [SerializeField] private Dropdown dropdown;
        [SerializeField] private AudioSource audioSource;

        // Ye text area mein apni company/portfolio details dalo
        [SerializeField][TextArea(5, 20)] private string systemPrompt = @"You are an AI assistant for a company. Here are the details:

Company Name: TechVision Solutions
Founded: 2020
Location: Mumbai, India
Services: 
- Mobile App Development
- Unity Game Development
- AI/ML Solutions
- Web Development

Portfolio Highlights:
- Developed 50+ mobile apps
- Created 20+ Unity games
- Worked with clients like XYZ Corp, ABC Industries
- Specialized in AR/VR experiences

Key Team Members:
- CEO: Rajesh Kumar
- CTO: Priya Sharma
- Lead Developer: Amit Patel

Contact: contact@techvision.com
Website: www.techvision.com

When users ask about 'my company', 'our services', 'our portfolio', or 'our team', provide information based on the above details.";

        private readonly string fileName = "output.wav";
        private readonly int duration = 5;
        private readonly string apiKey = "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";

        private AudioClip clip;
        private bool isRecording;
        private float time;
        private OpenAIApi openai;

        // Conversation history store karne ke liye
        private System.Collections.Generic.List<ChatMessage> conversationHistory;

        private void Start()
        {
            openai = new OpenAIApi(apiKey);

            // Conversation history initialize karo with system prompt
            conversationHistory = new System.Collections.Generic.List<ChatMessage>
            {
                new ChatMessage
                {
                    Role = "system",
                    Content = systemPrompt
                }
            };

#if UNITY_WEBGL && !UNITY_EDITOR
            dropdown.options.Add(new Dropdown.OptionData("Microphone not supported on WebGL"));
#else
            foreach (var device in Microphone.devices)
            {
                dropdown.options.Add(new Dropdown.OptionData(device));
            }
            recordButton.onClick.AddListener(StartRecording);
            dropdown.onValueChanged.AddListener(ChangeMicrophone);

            var index = PlayerPrefs.GetInt("user-mic-device-index");
            dropdown.SetValueWithoutNotify(index);
#endif

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        private void ChangeMicrophone(int index)
        {
            PlayerPrefs.SetInt("user-mic-device-index", index);
        }

        private void StartRecording()
        {
            isRecording = true;
            recordButton.enabled = false;
            var index = PlayerPrefs.GetInt("user-mic-device-index");

#if !UNITY_WEBGL
            clip = Microphone.Start(dropdown.options[index].text, false, duration, 44100);
#endif
        }

        private async void EndRecording()
        {
            message.text = "Transcribing...";

#if !UNITY_WEBGL
            Microphone.End(null);
#endif

            byte[] data = SaveWav.Save(fileName, clip);

            // Step 1: Speech to Text
            var req = new CreateAudioTranscriptionsRequest
            {
                FileData = new FileData() { Data = data, Name = "audio.wav" },
                Model = "whisper-1",
                Language = "en"
            };
            var transcription = await openai.CreateAudioTranscription(req);

            progressBar.fillAmount = 0;
            message.text = "User: " + transcription.Text;

            // Step 2: Get AI Reply (with context)
            message.text += "\n\nGetting AI response...";
            var chatResponse = await GetChatGPTResponse(transcription.Text);
            message.text += "\n\nAI: " + chatResponse;

            Debug.Log("AI Response: " + chatResponse);
            // Step 3: Text to Speech
            //message.text += "\n\nConverting to speech...";
            //await PlayTextToSpeech(chatResponse);

            recordButton.enabled = true;
        }

        private async System.Threading.Tasks.Task<string> GetChatGPTResponse(string userMessage)
        {
            // User message ko history mein add karo
            conversationHistory.Add(new ChatMessage
            {
                Role = "user",
                Content = userMessage
            });

            var chatRequest = new CreateChatCompletionRequest
            {
                Model = "gpt-3.5-turbo",
                Messages = conversationHistory // Puri history bhejo (system prompt included)
            };

            var response = await openai.CreateChatCompletion(chatRequest);

            if (response.Choices != null && response.Choices.Count > 0)
            {
                string aiResponse = response.Choices[0].Message.Content;

                // AI response ko bhi history mein add karo
                conversationHistory.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = aiResponse
                });

                return aiResponse;
            }

            return "Sorry, I couldn't generate a response.";
        }

        private async System.Threading.Tasks.Task PlayTextToSpeech(string text)
        {
            string url = "https://api.openai.com/v1/audio/speech";

            string escapedText = text.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

            string jsonData = $@"{{
                ""model"": ""tts-1"",
                ""input"": ""{escapedText}"",
                ""voice"": ""alloy""
            }}";

            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + apiKey);

                var operation = www.SendWebRequest();

                while (!operation.isDone)
                {
                    await System.Threading.Tasks.Task.Yield();
                }

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip audioClip = DownloadHandlerAudioClip.GetContent(www);
                    audioSource.clip = audioClip;
                    audioSource.Play();
                    message.text += "\n\n✅ Playing audio response!";
                }
                else
                {
                    Debug.LogError("TTS Error: " + www.error);
                    Debug.LogError("Response: " + www.downloadHandler.text);
                    message.text += "\n\n❌ Failed to generate speech.";
                }
            }
        }

        private void Update()
        {
            if (isRecording)
            {
                time += Time.deltaTime;
                progressBar.fillAmount = time / duration;

                if (time >= duration)
                {
                    time = 0;
                    isRecording = false;
                    EndRecording();
                }
            }
        }

        // Optional: Reset conversation button ke liye
        public void ResetConversation()
        {
            conversationHistory.Clear();
            conversationHistory.Add(new ChatMessage
            {
                Role = "system",
                Content = systemPrompt
            });
            message.text = "Conversation reset!";
        }
    }
}