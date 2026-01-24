using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.XREAL.Samples.NetWork;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Unity.XR.XREAL.Samples
{
#if UNITY_ANDROID && !UNITY_EDITOR
    using GalleryDataProvider = NativeGalleryDataProvider;
#else
    using GalleryDataProvider = MockGalleryDataProvider;
#endif
    public class CaptureExample : MonoBehaviour
    {
        public enum ResolutionLevel
        {
            High,
            Middle,
            Low,
        }

        public VideoUploadBackend videoUploadBackend;

        [Header("Backend Settings")]
        [SerializeField] private bool autoUploadToBackend = true;

        [SerializeField] private Button m_VideoButton;
        [SerializeField] private Button m_PhotoButton;

        [SerializeField] private Slider m_SliderMic;
        [SerializeField] private Text m_TextMic;
        [SerializeField] private Slider m_SliderApp;
        [SerializeField] private Text m_TextApp;

        [SerializeField] private Dropdown m_QualityDropDown;
        [SerializeField] private Dropdown m_RenderModeDropDown;
        [SerializeField] private Dropdown m_AudioStateDropDown;
        [SerializeField] private Dropdown m_CaptureSideDropDown;
        [SerializeField] private Toggle m_UseGreenBGToggle;

        [SerializeField] private RawImage m_PreviewRawImage;

        List<string> _ResolutionOptions = new List<string>() {
            ResolutionLevel.High.ToString(),
            ResolutionLevel.Middle.ToString(),
            ResolutionLevel.Low.ToString()
        };
        List<string> _RendermodeOptions = new List<string>() {
            BlendMode.Blend.ToString(),
            BlendMode.CameraOnly.ToString(),
            BlendMode.VirtualOnly.ToString()
        };
        List<string> _AudioStateOptions = new List<string>() {
            AudioState.MicAudio.ToString(),
            AudioState.ApplicationAudio.ToString(),
            AudioState.ApplicationAndMicAudio.ToString(),
            AudioState.None.ToString()
        };
        List<string> _CaptureSideOptions = new List<string>() {
            CaptureSide.Single.ToString(),
            CaptureSide.Both.ToString()
        };

        public BlendMode blendMode = BlendMode.Blend;
        public ResolutionLevel resolutionLevel = ResolutionLevel.High;
        public AudioState audioState = AudioState.ApplicationAudio;
        public CaptureSide captureside = CaptureSide.Single;
        public LayerMask m_CullingMask;
        public LayerMask m_StreamingCullingMask;
        public bool useGreenBackGround = false;

        /// <summary> Save the video to Application.persistentDataPath. </summary>
        /// <value> The full pathname of the video save file. </value>
        public string VideoSavePath
        {
            get
            {
                string timeStamp = Time.time.ToString().Replace(".", "").Replace(":", "");
                string filename = string.Format("TruVideo{0}.mp4", timeStamp);
                return Path.Combine(Application.persistentDataPath, filename);
            }
        }

        GalleryDataProvider galleryDataTool;

        [HideInInspector]
        public XREALVideoCapture m_VideoCapture = null;

        private XREALPhotoCapture m_PhotoCapture;

        private Resolution m_CameraResolution;
        private bool isOnPhotoProcess = false;

        #region Streaming Variables
        private XREALVideoCapture m_StreamCapture = null;
        private NetWorkBehaviour m_NetWorker;
        private string m_ServerIP;
        public string RTPPath
        {
            get
            {
                return string.Format(@"rtp://{0}:5555", m_ServerIP);
            }
        }
        private bool m_IsStreamLock = false;
        private bool m_IsStreamStarted = false;

        #endregion

        // Auto-stream management
        private bool isStreamAutoStarted = false;
        private bool shouldAutoStream = true;

        void Awake()
        {
            m_QualityDropDown.options.Clear();
            m_QualityDropDown.AddOptions(_ResolutionOptions);
            int default_quality_index = 0;
            for (int i = 0; i < _ResolutionOptions.Count; i++)
            {
                if (_ResolutionOptions[i].Equals(resolutionLevel.ToString()))
                {
                    default_quality_index = i;
                }
            }
            m_QualityDropDown.value = default_quality_index;
            m_QualityDropDown.onValueChanged.AddListener((index) =>
            {
                Enum.TryParse<ResolutionLevel>(_ResolutionOptions[index],
                    out resolutionLevel);
            });

            m_RenderModeDropDown.options.Clear();
            m_RenderModeDropDown.AddOptions(_RendermodeOptions);
            int default_blendmode_index = 0;
            for (int i = 0; i < _RendermodeOptions.Count; i++)
            {
                if (_RendermodeOptions[i].Equals(blendMode.ToString()))
                {
                    default_blendmode_index = i;
                }
            }
            m_RenderModeDropDown.value = default_blendmode_index;
            m_RenderModeDropDown.onValueChanged.AddListener((index) =>
            {
                Enum.TryParse<BlendMode>(_RendermodeOptions[index],
                    out blendMode);
            });

            m_AudioStateDropDown.options.Clear();
            m_AudioStateDropDown.AddOptions(_AudioStateOptions);
            int default_audiostate_index = 0;
            for (int i = 0; i < _AudioStateOptions.Count; i++)
            {
                if (_AudioStateOptions[i].Equals(audioState.ToString()))
                {
                    default_audiostate_index = i;
                }
            }
            m_AudioStateDropDown.value = default_audiostate_index;
            m_AudioStateDropDown.onValueChanged.AddListener((index) =>
            {
                Enum.TryParse<AudioState>(_AudioStateOptions[index],
                    out audioState);
            });

            m_CaptureSideDropDown.options.Clear();
            m_CaptureSideDropDown.AddOptions(_CaptureSideOptions);
            int default_captureside_index = 0;
            for (int i = 0; i < _CaptureSideOptions.Count; i++)
            {
                if (_CaptureSideOptions[i].Equals(captureside.ToString()))
                {
                    default_captureside_index = i;
                }
            }
            m_CaptureSideDropDown.value = default_captureside_index;
            m_CaptureSideDropDown.onValueChanged.AddListener((index) =>
            {
                Enum.TryParse<CaptureSide>(_CaptureSideOptions[index],
                    out captureside);
            });

            m_UseGreenBGToggle.isOn = useGreenBackGround;
            m_UseGreenBGToggle.onValueChanged.AddListener((val) =>
            {
                useGreenBackGround = val;
            });

            if (m_SliderMic != null)
            {
                m_SliderMic.maxValue = 5.0f;
                m_SliderMic.minValue = 0.1f;
                m_SliderMic.value = 1;
                m_SliderMic.onValueChanged.AddListener(OnSlideMicValueChange);
            }

            if (m_SliderApp != null)
            {
                m_SliderApp.maxValue = 5.0f;
                m_SliderApp.minValue = 0.1f;
                m_SliderApp.value = 1;
                m_SliderApp.onValueChanged.AddListener(OnSlideAppValueChange);
            }

            m_VideoButton.onClick.AddListener(RecordVideo);
            m_PhotoButton.onClick.AddListener(TakeAPhoto);

            RefreshUIState();
        }

        private void Update()
        {
            // Auto-start streaming when not recording
            if (shouldAutoStream && !isStreamAutoStarted)
            {
                bool isRecording = m_VideoCapture != null && m_VideoCapture.IsRecording;

                if (!isRecording && !m_IsStreamStarted && !m_IsStreamLock)
                {
                    Debug.Log("[AutoStream] Starting auto-stream");
                    OnStream();
                    isStreamAutoStarted = true;
                }
            }

            // Auto-stop streaming when recording starts
            if (isStreamAutoStarted && m_IsStreamStarted)
            {
                bool isRecording = m_VideoCapture != null && m_VideoCapture.IsRecording;

                if (isRecording)
                {
                    Debug.Log("[AutoStream] Stopping auto-stream because recording started");
                    StopVideoCaptureStream();
                    m_IsStreamStarted = false;
                    m_IsStreamLock = false;
                    isStreamAutoStarted = false;
                }
            }
        }

        void OnSlideMicValueChange(float val)
        {
            if (m_VideoCapture != null)
            {
                VideoEncoder encoder = m_VideoCapture.GetContext().GetEncoder() as VideoEncoder;
                if (encoder != null)
                    encoder.AdjustVolume(RecorderIndex.REC_MIC, val);
            }
            RefreshUIState();
        }

        void OnSlideAppValueChange(float val)
        {
            if (m_VideoCapture != null)
            {
                VideoEncoder encoder = m_VideoCapture.GetContext().GetEncoder() as VideoEncoder;
                if (encoder != null)
                    encoder.AdjustVolume(RecorderIndex.REC_APP, val);
            }
            RefreshUIState();
        }

        #region Stream Handle
        public void OnStream()
        {
            if (m_IsStreamLock)
            {
                return;
            }

            m_IsStreamLock = true;
            if (m_NetWorker == null)
            {
                m_NetWorker = new NetWorkBehaviour();
                m_NetWorker.Listen();
            }

            if (!m_IsStreamStarted)
            {
                LocalServerSearcher.CreateSingleton().Search((result) =>
                {
                    Debug.LogFormat("[FPStreammingCast] Get the server result:{0} ip:{1}:{2}", result.isSuccess, result.endPoint?.Address, result.endPoint.Port);
                    if (result.isSuccess)
                    {
                        string ip = result.endPoint.Address.ToString();
                        int port = result.endPoint.Port;
                        m_NetWorker.CheckServerAvailable(ip, port, (isAvailable) =>
                        {
                            Debug.LogFormat("[FPStreammingCast] Is the server {0}:{1} ok? {2}", ip, result.endPoint.Port, isAvailable);
                            if (isAvailable)
                            {
                                m_ServerIP = ip;
                                m_IsStreamStarted = true;
                                CreateAndStartStream();
                            }
                            m_IsStreamLock = false;
                        });
                    }
                    else
                    {
                        m_IsStreamLock = false;
                        Debug.LogError("[FPStreammingCast] Can not find the server...");
                    }
                });
            }
            else
            {
                StopVideoCaptureStream();
                m_IsStreamStarted = false;
                m_IsStreamLock = false;
            }
        }

        private void CreateAndStartStream()
        {
            CreateStreamCapture(delegate ()
            {
                Debug.LogFormat("[FPStreammingCast] Start video capture for streaming.");
                StartStream();
            });
        }

        private void CreateStreamCapture(Action callback)
        {
            if (m_StreamCapture != null)
            {
                callback?.Invoke();
                return;
            }

            XREALVideoCaptureUtility.CreateAsync(false, delegate (XREALVideoCapture videoCapture)
            {
                if (videoCapture != null)
                {
                    m_StreamCapture = videoCapture;
                    callback?.Invoke();
                }
                else
                {
                    Debug.LogError("[FPStreammingCast] Failed to create VideoCapture Instance for streaming!");
                    m_IsStreamLock = false;
                }
            });
        }

        public void StartStream()
        {
            if (m_StreamCapture == null || m_StreamCapture.IsRecording)
            {
                Debug.LogWarning("Can not start video capture for streaming!");
                return;
            }

            CameraParameters cameraParameters = new CameraParameters();
            Resolution cameraResolution = GetResolutionByLevel(resolutionLevel);
            cameraParameters.cameraType = CameraType.RGB;
            cameraParameters.hologramOpacity = 1f;
            cameraParameters.frameRate = NativeConstants.RECORD_FPS_DEFAULT;
            cameraParameters.cameraResolutionWidth = cameraResolution.width;
            cameraParameters.cameraResolutionHeight = cameraResolution.height;
            cameraParameters.pixelFormat = CapturePixelFormat.BGRA32;
            cameraParameters.blendMode = blendMode;
            cameraParameters.audioState = audioState;
            cameraParameters.monophonic = true;

            if (m_NetWorker != null)
            {
                LitJson.JsonData json = new LitJson.JsonData();
                json["useAudio"] = (cameraParameters.audioState != AudioState.None);
                m_NetWorker.SendMsg(json, (response) =>
                {
                    bool result;
                    if (bool.TryParse(response["success"].ToString(), out result) && result)
                    {
                        m_StreamCapture.StartVideoModeAsync(cameraParameters, OnStartedVideoCaptureModeStream, true);
                    }
                    else
                    {
                        Debug.LogError("[FPStreammingCast] Can not received response from server.");
                    }
                });
            }
        }

        void OnStartedVideoCaptureModeStream(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Debug.LogFormat("[FPStreammingCast] Started Video Capture Mode Failed!");
                return;
            }

            Debug.LogFormat("[FPStreammingCast] Started Video Capture Mode for streaming!");
            m_StreamCapture.StartRecordingAsync(RTPPath, OnStartedRecordingVideoStream);
            m_StreamCapture.GetContext().GetBehaviour().CaptureCamera.cullingMask = m_StreamingCullingMask.value;
        }

        void OnStartedRecordingVideoStream(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Debug.Log("Started Recording Video Stream Failed!");
                return;
            }
            Debug.Log("Started Recording Video Stream!");
        }

        public void StopVideoCaptureStream()
        {
            Debug.LogFormat("[FPStreammingCast] Stop Video Capture Stream!");
            if (m_StreamCapture != null && m_StreamCapture.IsRecording)
            {
                m_StreamCapture.StopRecordingAsync(OnStoppedRecordingVideoStream);
            }
            else if (m_StreamCapture != null)
            {
                // Stream capture exists but not recording, just dispose it
                m_StreamCapture.Dispose();
                m_StreamCapture = null;
            }
        }

        void OnStoppedRecordingVideoStream(XREALVideoCapture.VideoCaptureResult result)
        {
            Debug.LogFormat("[FPStreammingCast] Stopped Recording Video Stream!");
            if (m_StreamCapture != null)
            {
                m_StreamCapture.StopVideoModeAsync(OnStoppedVideoCaptureModeStream);
            }
        }

        void OnStoppedVideoCaptureModeStream(XREALVideoCapture.VideoCaptureResult result)
        {
            Debug.LogFormat("[FPStreammingCast] Stopped Video Capture Mode Stream!");

            if (m_StreamCapture != null)
            {
                m_StreamCapture.Dispose();
                m_StreamCapture = null;
            }

            if (m_NetWorker != null)
            {
                m_NetWorker?.Close();
                m_NetWorker = null;
            }
        }

        #endregion

        #region Video Capture Handle

        public void RecordVideo()
        {
            // Stop streaming if active (both auto and manual)
            if (m_IsStreamStarted)
            {
                Debug.Log("[RecordVideo] Stopping active stream before recording");
                StopVideoCaptureStream();
                m_IsStreamStarted = false;
                m_IsStreamLock = false;
                isStreamAutoStarted = false;
            }

            if (m_VideoCapture == null || !m_VideoCapture.IsRecording)
            {
                CreateVideoCapture(() =>
                {
                    StartVideoCapture();
                });
            }
            else
            {
                StopVideoCapture();
            }
        }

        private void CreateVideoCapture(Action callback)
        {
            Debug.LogFormat("[VideoCapture] Creating VideoCapture Instance!");
            if (m_VideoCapture != null)
            {
                callback?.Invoke();
                return;
            }

            XREALVideoCaptureUtility.CreateAsync(false, delegate (XREALVideoCapture videoCapture)
            {
                if (videoCapture != null)
                {
                    m_VideoCapture = videoCapture;
                    callback?.Invoke();
                }
                else
                {
                    Debug.LogError("[VideoCapture] Failed to create VideoCapture Instance!");
                }
            });
        }

        public void StartVideoCapture()
        {
            if (m_VideoCapture == null || m_VideoCapture.IsRecording)
            {
                Debug.LogWarning("Can not start video capture!");
                return;
            }

            CameraParameters cameraParameters = new CameraParameters();
            Resolution cameraResolution = GetResolutionByLevel(resolutionLevel);
            cameraParameters.cameraType = CameraType.RGB;
            cameraParameters.hologramOpacity = 0.0f;
            cameraParameters.frameRate = NativeConstants.RECORD_FPS_DEFAULT;
            cameraParameters.cameraResolutionWidth = cameraResolution.width;
            cameraParameters.cameraResolutionHeight = cameraResolution.height;
            cameraParameters.pixelFormat = CapturePixelFormat.PNG;
            cameraParameters.blendMode = blendMode;
            cameraParameters.audioState = audioState;
            cameraParameters.captureSide = captureside;
            cameraParameters.backgroundColor = useGreenBackGround ? Color.green : Color.black;

            m_VideoCapture.StartVideoModeAsync(cameraParameters, OnStartedVideoCaptureMode, true);
        }

        void OnStartedVideoCaptureMode(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Debug.Log("Started Video Capture Mode failed!");
                return;
            }

            Debug.Log("Started Video Capture Mode!");
            if (m_SliderMic != null && m_SliderApp != null)
            {
                float volumeMic = m_SliderMic.value;
                float volumeApp = m_SliderApp.value;
                m_VideoCapture.StartRecordingAsync(VideoSavePath, OnStartedRecordingVideo, volumeMic, volumeApp);
            }
            else
            {
                m_VideoCapture.StartRecordingAsync(VideoSavePath, OnStartedRecordingVideo);
            }

            m_VideoCapture.GetContext().GetBehaviour().CaptureCamera.cullingMask = m_CullingMask.value;

            if (m_PreviewRawImage != null)
                m_PreviewRawImage.texture = m_VideoCapture.PreviewTexture;
        }

        void OnStartedRecordingVideo(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Debug.Log("Started Recording Video Failed!");
                return;
            }

            Debug.Log("Started Recording Video!");
            RefreshUIState();
        }

        public void StopVideoCapture()
        {
            if (m_VideoCapture == null || !m_VideoCapture.IsRecording)
            {
                Debug.LogWarning("Can not stop video capture!");
                return;
            }

            Debug.Log("Stop Video Capture!");
            m_VideoCapture.StopRecordingAsync(OnStoppedRecordingVideo);
        }

        void OnStoppedRecordingVideo(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Debug.Log("Stopped Recording Video Failed!");
                return;
            }

            Debug.Log("Stopped Recording Video!");
            m_VideoCapture.StopVideoModeAsync(OnStoppedVideoCaptureMode);
        }

        void OnStoppedVideoCaptureMode(XREALVideoCapture.VideoCaptureResult result)
        {
            Debug.Log("Stopped Video Capture Mode!");

            // Get the actual output path from the encoder
            var encoder = m_VideoCapture.GetContext().GetEncoder() as VideoEncoder;
            string actualOutputPath = encoder.EncodeConfig.outPutPath;

            Debug.LogFormat("[VideoCapture] Actual output path: {0}", actualOutputPath);

            string filename = string.Format("TruVideo_{0}.mp4", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

            // Save to gallery using the actual output path
            StartCoroutine(DelayInsertVideoToGallery(actualOutputPath, filename, "Record"));

            // Release video capture resource
            m_VideoCapture.Dispose();
            m_VideoCapture = null;

            RefreshUIState();

            // Reset auto-stream flag so it can restart after recording finishes
            isStreamAutoStarted = false;
        }

        IEnumerator DelayInsertVideoToGallery(string originFilePath, string displayName, string folderName)
        {
            yield return new WaitForSeconds(0.1f);
            InsertVideoToGallery(originFilePath, displayName, folderName);
        }

        public void InsertVideoToGallery(string originFilePath, string displayName, string folderName)
        {
            Debug.LogFormat("InsertVideoToGallery: {0}, {1} => {2}", displayName, originFilePath, folderName);

            // Verify file exists before trying to insert
            if (!File.Exists(originFilePath))
            {
                Debug.LogError($"[VideoCapture] Video file not found at: {originFilePath}");
                return;
            }

            if (galleryDataTool == null)
            {
                galleryDataTool = new GalleryDataProvider();
            }

            galleryDataTool.InsertVideo(originFilePath, displayName, folderName);

            StartCoroutine(UploadVideo(displayName));
        }

        IEnumerator UploadVideo(string displayName)
        {
            yield return new WaitForSeconds(1);
            // Upload to backend if enabled
            string actualPath = "/storage/emulated/0/Movies/Record";
            string new_path = Path.Combine(actualPath, Path.GetFileName(displayName));

            if (autoUploadToBackend && videoUploadBackend != null)
            {
                videoUploadBackend.UploadRecordedVideo(new_path);
            }
        }
        #endregion

        void RefreshUIState()
        {
            var recordText = m_VideoButton.transform.Find("Text");
            if (recordText)
            {
                bool notStarted = m_VideoCapture == null || !m_VideoCapture.IsRecording;
                recordText.GetComponent<Text>().text = notStarted ? "Start Record" : "Stop Record";
            }

            if (m_TextMic != null && m_SliderMic != null)
                m_TextMic.text = m_SliderMic.value.ToString("F1");
            if (m_TextApp != null && m_SliderApp != null)
                m_TextApp.text = m_SliderApp.value.ToString("F1");
        }

        private Resolution GetResolutionByLevel(ResolutionLevel level)
        {
            var resolutions = XREALVideoCaptureUtility.SupportedResolutions.OrderByDescending((res) => res.width * res.height);
            Resolution resolution = new Resolution();
            switch (level)
            {
                case ResolutionLevel.High:
                    resolution = resolutions.ElementAt(0);
                    break;
                case ResolutionLevel.Middle:
                    resolution = resolutions.ElementAt(1);
                    break;
                case ResolutionLevel.Low:
                    resolution = resolutions.ElementAt(2);
                    break;
                default:
                    break;
            }
            return resolution;
        }

        void OnDestroy()
        {
            // Release video capture resource
            m_VideoCapture?.Dispose();
            m_VideoCapture = null;

            // Release stream capture resource
            m_StreamCapture?.Dispose();
            m_StreamCapture = null;

            // Release photo capture resource
            m_PhotoCapture?.Dispose();
            m_PhotoCapture = null;

            // Close network
            m_NetWorker?.Close();
            m_NetWorker = null;
        }

        void CreatePhotoCapture(Action<XREALPhotoCapture> onCreated)
        {
            if (m_PhotoCapture != null)
            {
                Debug.Log("[TakePicture] CreatePhotoCapture: The XREALPhotoCapture has already been created.");
                return;
            }

            XREALPhotoCapture.CreateAsync(false, delegate (XREALPhotoCapture captureObject)
            {
                m_CameraResolution = XREALPhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height).First();

                if (captureObject == null)
                {
                    Debug.LogError("Can not get a captureObject.");
                    return;
                }

                m_PhotoCapture = captureObject;

                CameraParameters cameraParameters = new CameraParameters();
                Resolution cameraResolution = GetResolutionByLevel(resolutionLevel);
                cameraParameters.cameraType = CameraType.RGB;
                cameraParameters.hologramOpacity = 0.0f;
                cameraParameters.frameRate = NativeConstants.RECORD_FPS_DEFAULT;
                cameraParameters.cameraResolutionWidth = cameraResolution.width;
                cameraParameters.cameraResolutionHeight = cameraResolution.height;
                cameraParameters.pixelFormat = CapturePixelFormat.PNG;
                cameraParameters.blendMode = blendMode;
                cameraParameters.audioState = AudioState.None;
                cameraParameters.captureSide = captureside;
                cameraParameters.backgroundColor = useGreenBackGround ? Color.green : Color.black;

                m_PhotoCapture.StartPhotoModeAsync(cameraParameters, delegate (XREALPhotoCapture.PhotoCaptureResult result)
                {
                    Debug.Log("Start PhotoMode Async");
                    if (result.success)
                    {
                        onCreated?.Invoke(m_PhotoCapture);
                    }
                    else
                    {
                        isOnPhotoProcess = false;
                        this.ClosePhotoCapture();
                        Debug.LogError("[TakePicture] CreatePhotoCapture: Start PhotoMode failed." + result.resultType);
                    }
                }, true);
            });
        }

        void TakeAPhoto()
        {
            if (isOnPhotoProcess)
            {
                Debug.LogWarning("[TakePicture] TakeAPhoto: Currently in the process of taking pictures, Can not take photo .");
                return;
            }

            isOnPhotoProcess = true;

            if (m_PhotoCapture == null)
            {
                this.CreatePhotoCapture((capture) =>
                {
                    capture.TakePhotoAsync(OnCapturedPhotoToMemory);
                });
            }
            else
            {
                m_PhotoCapture.TakePhotoAsync(OnCapturedPhotoToMemory);
            }
        }

        void OnCapturedPhotoToMemory(XREALPhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame photoCaptureFrame)
        {
            Debug.Log("[TakePicture] OnCapturedPhotoToMemory");
            var targetTexture = new Texture2D(m_CameraResolution.width, m_CameraResolution.height);
            photoCaptureFrame.UploadImageDataToTexture(targetTexture);

            SaveTextureAsPNG(photoCaptureFrame);
            SaveTextureToGallery(photoCaptureFrame);
            this.ClosePhotoCapture();
        }

        void SaveTextureAsPNG(PhotoCaptureFrame photoCaptureFrame)
        {
            Debug.Log("[TakePicture] SaveTextureAsPNG");
            if (photoCaptureFrame.TextureData == null)
            {
                Debug.LogError("[TakePicture] SaveTextureAsPNG: TextureData is null");
                return;
            }
            try
            {
                string filename = string.Format("Xreal_Shot_{0}.png", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                string path = string.Format("{0}/XrealShots", Application.persistentDataPath);
                string filePath = string.Format("{0}/{1}", path, filename);

                byte[] _bytes = photoCaptureFrame.TextureData;
                Debug.LogFormat("[TakePicture] Photo capture: {0}Kb was saved to [{1}]", _bytes.Length / 1024, filePath);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                File.WriteAllBytes(string.Format("{0}/{1}", path, filename), _bytes);
            }
            catch (Exception e)
            {
                Debug.LogError($"Save picture failed! {e}");
                throw e;
            }
        }

        void ClosePhotoCapture()
        {
            if (m_PhotoCapture == null)
            {
                Debug.LogError("The XREALPhotoCapture has not been created.");
                return;
            }
            m_PhotoCapture.StopPhotoModeAsync(OnStoppedPhotoMode);
        }

        void OnStoppedPhotoMode(XREALPhotoCapture.PhotoCaptureResult result)
        {
            m_PhotoCapture?.Dispose();
            m_PhotoCapture = null;
            isOnPhotoProcess = false;
        }

        public void SaveTextureToGallery(PhotoCaptureFrame photoCaptureFrame)
        {
            Debug.Log("[TakePicture] SaveTextureToGallery");
            if (photoCaptureFrame.TextureData == null)
                return;
            try
            {
                string filename = string.Format("Xreal_Shot_{0}.png", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                byte[] _bytes = photoCaptureFrame.TextureData;
                Debug.Log(_bytes.Length / 1024 + "Kb was saved as: " + filename);
                if (galleryDataTool == null)
                {
                    galleryDataTool = new GalleryDataProvider();
                }

                galleryDataTool.InsertImage(_bytes, filename, "Screenshots");
            }
            catch (Exception e)
            {
                Debug.LogError("[TakePicture] Save picture failed!");
                throw e;
            }
        }
    }
}