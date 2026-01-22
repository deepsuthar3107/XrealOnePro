using LitJson;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Unity.XR.XREAL.Samples.NetWork
{
    /// <summary> An observer view net worker. </summary>
    public class NetWorkBehaviour
    {
        protected NetWorkClient m_NetWorkClient;

        private const float limitWaittingTime = 5f;

        private bool m_IsConnected = false;
        private bool m_IsJoninSuccess = false;
        private bool m_IsClosed = false;

        private Coroutine checkServerAvailableCoroutine = null;

        private Dictionary<ulong, Action<JsonData>> _ResponseEvents =
            new Dictionary<ulong, Action<JsonData>>();

        // ===================== RECONNECT CONFIG =====================
        private bool m_EnableAutoReconnect = true;
        private int m_ReconnectRetryCount = 0;
        private const int MaxReconnectRetry = 15;
        private const float ReconnectDelay = 2f;

        private string m_LastIP;
        private int m_LastPort;
        // ============================================================

        public virtual void Listen()
        {
            if (m_NetWorkClient != null) return;

            m_NetWorkClient = new NetWorkClient();
            m_NetWorkClient.OnDisconnect += OnDisconnect;
            m_NetWorkClient.OnConnect += OnConnected;
            m_NetWorkClient.OnJoinRoomResult += OnJoinRoomResult;
            m_NetWorkClient.OnMessageResponse += OnMessageResponse;
        }

        public bool IsNetworkConnected => m_IsConnected;

        #region Message Handling

        private void OnMessageResponse(byte[] data)
        {
            ulong msgid = BitConverter.ToUInt64(data, 0);

            if (!_ResponseEvents.TryGetValue(msgid, out var callback))
            {
                Debug.LogWarning("[NetWorkBehaviour] Unknown msgid: " + msgid);
                return;
            }

            byte[] result = new byte[data.Length - sizeof(ulong)];
            Array.Copy(data, sizeof(ulong), result, 0, result.Length);

            string json = Encoding.UTF8.GetString(result);
            callback?.Invoke(JsonMapper.ToObject(json));
            _ResponseEvents.Remove(msgid);
        }

        #endregion

        #region Connection Logic

        public void CheckServerAvailable(string ip, int port, Action<bool> callback)
        {
            if (string.IsNullOrEmpty(ip))
            {
                callback?.Invoke(false);
                return;
            }

            m_LastIP = ip;
            m_LastPort = port;

            if (checkServerAvailableCoroutine != null)
            {
                XREALMainThreadDispatcher.Singleton
                    .StopCoroutine(checkServerAvailableCoroutine);
            }

            checkServerAvailableCoroutine =
                XREALMainThreadDispatcher.Singleton.StartCoroutine(
                    CheckServerAvailableCoroutine(ip, port, callback));
        }

        private IEnumerator CheckServerAvailableCoroutine(
            string ip, int port, Action<bool> callback)
        {
            Debug.Log($"[NetWorkBehaviour] Connecting {ip}:{port}");

            Listen();
            m_NetWorkClient.Connect(ip, port);

            float time = 0;
            while (!m_IsConnected)
            {
                if (time > limitWaittingTime || m_IsClosed)
                {
                    callback?.Invoke(false);
                    yield break;
                }

                time += Time.deltaTime;
                yield return null;
            }

            m_NetWorkClient.EnterRoomRequest();

            time = 0;
            while (!m_IsJoninSuccess)
            {
                if (time > limitWaittingTime || m_IsClosed)
                {
                    callback?.Invoke(false);
                    yield break;
                }

                time += Time.deltaTime;
                yield return null;
            }

            callback?.Invoke(true);
        }

        #endregion

        #region Send Message

        public void SendMsg(JsonData data, Action<JsonData> onResponse, float timeout = 3)
        {
            XREALMainThreadDispatcher.Singleton
                .StartCoroutine(SendMessage(data, onResponse, timeout));
        }

        private IEnumerator SendMessage(JsonData data,
            Action<JsonData> onResponse, float timeout)
        {
            if (data == null)
            {
                Debug.LogError("[NetWorkBehaviour] data is null");
                yield break;
            }

            ulong msgid = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            byte[] json = Encoding.UTF8.GetBytes(data.ToJson());

            byte[] packet = new byte[json.Length + sizeof(ulong)];
            Array.Copy(BitConverter.GetBytes(msgid), 0, packet, 0, sizeof(ulong));
            Array.Copy(json, 0, packet, sizeof(ulong), json.Length);

            if (onResponse != null)
            {
                AsyncTask<JsonData> task =
                    new AsyncTask<JsonData>(out var callback);

                _ResponseEvents[msgid] = callback;
                m_NetWorkClient.SendMessage(packet);

                XREALMainThreadDispatcher.Singleton
                    .StartCoroutine(SendMsgTimeout(msgid, timeout));

                yield return task.WaitForCompletion();
                onResponse?.Invoke(task.Result);
            }
            else
            {
                m_NetWorkClient.SendMessage(packet);
            }
        }

        private IEnumerator SendMsgTimeout(ulong id, float timeout)
        {
            yield return new WaitForSeconds(timeout);

            if (_ResponseEvents.TryGetValue(id, out var callback))
            {
                JsonData fail = new JsonData();
                fail["success"] = false;
                callback?.Invoke(fail);
                _ResponseEvents.Remove(id);
            }
        }

        #endregion

        #region Network Events

        private void OnConnected()
        {
            Debug.Log("[NetWorkBehaviour] Connected");
            m_IsConnected = true;
            m_ReconnectRetryCount = 0;
        }

        private void OnDisconnect()
        {
            Debug.Log("[NetWorkBehaviour] Disconnected");
            m_IsConnected = false;
            m_IsJoninSuccess = false;

            if (!m_IsClosed && m_EnableAutoReconnect)
            {
                TryReconnect();
            }
        }

        private void OnJoinRoomResult(bool result)
        {
            Debug.Log("[NetWorkBehaviour] JoinRoom: " + result);
            m_IsJoninSuccess = result;

            if (!result)
                OnDisconnect();
        }

        #endregion

        #region Reconnect Logic

        private void TryReconnect()
        {
            if (m_ReconnectRetryCount >= MaxReconnectRetry)
            {
                Debug.LogError("[NetWorkBehaviour] Max reconnect retries reached");
                return;
            }

            m_ReconnectRetryCount++;
            Debug.Log($"[NetWorkBehaviour] Reconnect attempt {m_ReconnectRetryCount}");

            XREALMainThreadDispatcher.Singleton
                .StartCoroutine(ReconnectCoroutine());
        }

        private IEnumerator ReconnectCoroutine()
        {
            yield return new WaitForSeconds(ReconnectDelay);

            m_NetWorkClient?.Dispose();
            m_NetWorkClient = null;

            m_IsClosed = false;
            m_IsConnected = false;
            m_IsJoninSuccess = false;

            CheckServerAvailable(m_LastIP, m_LastPort, success =>
            {
                if (!success)
                    TryReconnect();
            });
        }

        #endregion

        #region Close

        public virtual void Close()
        {
            Debug.Log("[NetWorkBehaviour] Closed intentionally");

            m_IsClosed = true;
            m_EnableAutoReconnect = false;
            m_ReconnectRetryCount = 0;

            if (checkServerAvailableCoroutine != null)
            {
                XREALMainThreadDispatcher.Singleton
                    .StopCoroutine(checkServerAvailableCoroutine);
            }

            m_NetWorkClient?.ExitRoomRequest();
            m_NetWorkClient?.Dispose();
            m_NetWorkClient = null;
        }

        #endregion
    }
}
