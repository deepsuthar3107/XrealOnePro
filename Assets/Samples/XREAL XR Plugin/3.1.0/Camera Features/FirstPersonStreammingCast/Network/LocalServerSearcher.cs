using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Unity.XR.XREAL.Samples.NetWork
{
    public class LocalServerSearcher : SingletonMonoBehaviour<LocalServerSearcher>
    {
        public struct ServerInfoResult
        {
            public bool isSuccess;
            public IPEndPoint endPoint;
        }
        public delegate void OnGetSearchResult(ServerInfoResult result);
        private UdpClient client;
        private IPEndPoint endpoint;
        private Thread m_ReceiveThread = null;
        private const string SEARCHSERVERIP = "FIND-SERVER";
        private const int BroadCastPort = 6001;
        private static float TimeoutWaittingTime = 3f;
        private IPEndPoint m_LocalServer;
        private Queue<OnGetSearchResult> m_Tasks = new Queue<OnGetSearchResult>();
        private Coroutine m_TimeOutCoroutine = null;
        protected override void Awake()
        {
            base.Awake();

            client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            endpoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), BroadCastPort);
        }

      /*  public LocalServerSearcher()
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            endpoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), BroadCastPort);
        }*/

        public void Search(OnGetSearchResult callback)
        {
            lock (m_Tasks)
            {
                m_Tasks.Enqueue(callback);
            }

            if (m_ReceiveThread == null)
            {
                m_ReceiveThread = new Thread(new ThreadStart(RecvThread));
                m_ReceiveThread.IsBackground = true;
                m_ReceiveThread.Start();
            }

            RequestForServerIP();
            TryStopTimeOutCoroutine();
            m_TimeOutCoroutine = StartCoroutine(TimeOut());
        }

        private void TryStopTimeOutCoroutine()
        {
            if (m_TimeOutCoroutine != null)
            {
                Debug.Log("[LocalServerSearcher] StopTimeOutCoroutine");
                StopCoroutine(m_TimeOutCoroutine);
                m_TimeOutCoroutine = null;
            }
        }

        private void RequestForServerIP()
        {
            Debug.Log("[LocalServerSearcher] RequestForServerIP");
            byte[] buf = Encoding.Default.GetBytes(SEARCHSERVERIP);
            client.Send(buf, buf.Length, endpoint);
        }

        private void RecvThread()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, BroadCastPort);

            while (true)
            {
                try
                {
                    byte[] buf = client.Receive(ref endpoint);
                    string data = Encoding.Default.GetString(buf);

                    if (!string.IsNullOrEmpty(data))
                    {
                        string[] param = data.Split(':');
                        if (param.Length == 2)
                        {
                            var server = new IPEndPoint(
                                IPAddress.Parse(param[0]),
                                int.Parse(param[1])
                            );

                            XREALMainThreadDispatcher.Singleton.QueueOnMainThread(() =>
                            {
                                m_LocalServer = server;
                                Response(m_LocalServer);
                                TryStopTimeOutCoroutine();
                            });
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }


        private IEnumerator TimeOut()
        {
            float time_last = 0f;
            while (true)
            {
                yield return new WaitForEndOfFrame();
                time_last += Time.deltaTime;
                if (time_last > TimeoutWaittingTime)
                {
                    Debug.Log("[LocalServerSearcher] Get the server TimeOut");
                    Response(null);
                    TryStopTimeOutCoroutine();
                }
            }
        }

        private void Response(IPEndPoint endpoint)
        {
            if (m_Tasks.Count == 0)
                return;

            if (XREALMainThreadDispatcher.Singleton == null)
                return;

            XREALMainThreadDispatcher.Singleton.QueueOnMainThread(() =>
            {
                ServerInfoResult result = new ServerInfoResult
                {
                    endPoint = endpoint,
                    isSuccess = endpoint != null
                };

                lock (m_Tasks)
                {
                    if (m_Tasks.Count == 0)
                        return;

                    var callback = m_Tasks.Dequeue();
                    callback?.Invoke(result);
                }
            });
        }
        protected override void OnDestroy()
        {
            // Stop receive thread safely
            if (m_ReceiveThread != null)
            {
                m_ReceiveThread.Abort();
                m_ReceiveThread = null;
            }

            // Close UDP client
            client?.Close();
            client = null;

            base.OnDestroy();
        }


    }
}
