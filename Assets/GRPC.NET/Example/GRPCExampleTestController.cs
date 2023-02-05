using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BestHTTP;
using BestHTTP.Logger;
using Grpc.Core;
using Grpc.Net.Client;
using TestProto;
using UnityEngine;

namespace GRPC.NET.Example
{
    public class GRPCExampleTestController : MonoBehaviour
    {
        private GRPCExampleLogger m_Logger;

        private GrpcTestCallIdFactory m_CallIdFactory;

        private GrpcTestCallFactory m_CallFactoryA;
        private GrpcTestCallFactory m_CallFactoryB;

        private void Awake()
        {
            m_Logger = gameObject.GetComponent<GRPCExampleLogger>();

            // Setup BestHTTP
            HTTPManager.Setup();
            HTTPManager.Logger.Level = Loglevels.All; // Enable all log levels

            // Shared Id call Factory
            m_CallIdFactory = new GrpcTestCallIdFactory();

            m_CallFactoryA = new GrpcTestCallFactory(m_CallIdFactory, 'A', m_Logger);
            m_CallFactoryB = new GrpcTestCallFactory(m_CallIdFactory, 'B', m_Logger);
        }

        void Update() => m_CallIdFactory.Update();

        private void RunTestCallUnary(GrpcTestCallFactory callFactory, string detail = null)
        {
            Debug.Log("RunTestCallUnary");

            var call = callFactory.CreateUnary(detail);
            call.Call();
        }

        private void RunTestCallServerStreaming(GrpcTestCallFactory callFactory)
        {
            Debug.Log("RunTestCallServerStreaming");

            var call = callFactory.CreateServerStreaming();
            call.Call();
        }

        private void RunTestCallClientStreaming(GrpcTestCallFactory callFactory)
        {
            Debug.Log("RunTestCallClientStreaming");

            var call = callFactory.CreateClientStreaming();
            call.Call();
        }

        private void RunTestCallBiStreaming(GrpcTestCallFactory callFactory)
        {
            Debug.Log("RunTestCallBiStreaming");

            var call = callFactory.CreateBiStreaming();
            call.Call();
        }

        private class UnaryGrpcTestCall : GrpcTestCall
        {
            private readonly string m_Detail;

            public UnaryGrpcTestCall(GRPCExampleLogger log, GrpcTestCallIdFactory callIdFactory, char factoryId,
                HelloWorldService.HelloWorldServiceClient client, string detail) : base(log, callIdFactory, factoryId,
                client)
            {
                m_Detail = detail;
            }

            protected override void CallImpl(CancellationToken cancelToken)
            {
                FireCallStarted("Unary");

                var asyncCall = Client.helloAsync(new HelloRequest()
                {
                    Text = "World" + (m_Detail != null ? $" [{m_Detail}]" : "")
                }, cancellationToken: cancelToken);

                void PrintStatus() => WriteLog("Status: " + asyncCall.GetStatus());

                asyncCall.ResponseHeadersAsync.ContinueWith(ContinuationWithHeaders, cancelToken);
                asyncCall.ResponseAsync.ContinueWith(ContinuationWithResponse, cancelToken);

                // If we have no response we still complete the call here
                asyncCall.GetAwaiter().OnCompleted(() => FireCallCompleted(PrintStatus));
            }
        }

        private class ServerStreamingGrpcTestCall : GrpcTestCall
        {
            public ServerStreamingGrpcTestCall(GRPCExampleLogger log, GrpcTestCallIdFactory callIdFactory, char factoryId,
                HelloWorldService.HelloWorldServiceClient client) : base(log, callIdFactory, factoryId, client)
            { }

            protected override void CallImpl(CancellationToken cancelToken)
            {
                FireCallStarted("ServerStreaming");

                var asyncCall = Client.helloServer(new HelloRequest()
                {
                    Text = "World"
                }, cancellationToken: cancelToken);

                void PrintStatus() => WriteLog("Status: " + asyncCall.GetStatus());

                asyncCall.ResponseHeadersAsync.ContinueWith(ContinuationWithHeaders, cancelToken);

                ProcessServerResponses(asyncCall.ResponseStream, PrintStatus);
            }
        }

        private class ClientStreamingGrpcTestCall : GrpcTestCall
        {
            public ClientStreamingGrpcTestCall(GRPCExampleLogger log, GrpcTestCallIdFactory callIdFactory, char factoryId,
                HelloWorldService.HelloWorldServiceClient client) : base(log, callIdFactory, factoryId, client)
            { }

            protected override void CallImpl(CancellationToken cancelToken)
            {
                FireCallStarted("ClientStreaming");

                var asyncCall = Client.helloClient(cancellationToken: cancelToken);

                void PrintStatus() => WriteLog("Status: " + asyncCall.GetStatus());

                asyncCall.ResponseHeadersAsync.ContinueWith(ContinuationWithHeaders, cancelToken);
                asyncCall.ResponseAsync.ContinueWith(ContinuationWithResponse, cancelToken);

                SendClientRequests(asyncCall.RequestStream);

                // If we have no response we still complete the call here
                asyncCall.GetAwaiter().OnCompleted(() => FireCallCompleted(PrintStatus));
            }
        }

        private class BiStreamingGrpcTestCall : GrpcTestCall
        {
            public BiStreamingGrpcTestCall(GRPCExampleLogger log, GrpcTestCallIdFactory callIdFactory, char factoryId,
                HelloWorldService.HelloWorldServiceClient client) : base(log, callIdFactory, factoryId, client)
            { }

            protected override void CallImpl(CancellationToken cancelToken)
            {
                FireCallStarted("ClientStreaming");

                var asyncCall = Client.helloBoth(cancellationToken: cancelToken);

                void PrintStatus() => WriteLog("Status: " + asyncCall.GetStatus());

                asyncCall.ResponseHeadersAsync.ContinueWith(ContinuationWithHeaders, cancelToken);

                SendClientRequests(asyncCall.RequestStream);

                ProcessServerResponses(asyncCall.ResponseStream, PrintStatus);
            }
        }

        private class GrpcTestCallIdFactory
        {
            private static Color Hex2Color(string hex) => ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.red;
            private static readonly Color[] ColorList =
            {
                Hex2Color("#db5f57"), Hex2Color("#dbc257"), Hex2Color("#91db57"), Hex2Color("#57db80"),
                Hex2Color("#57d3db"), Hex2Color("#5770db"), Hex2Color("#a157db"), Hex2Color("#db57b2"),
            };

            private int m_Inc = 0;
            private int m_ColorPointer = 0;

            private List<GrpcTestCall> m_ActiveCalls = new List<GrpcTestCall>();

            public (int, Color) CreateNext(GrpcTestCall call)
            {
                Interlocked.Increment(ref m_Inc);

                // Find unique color for this call
                var c = ColorList[m_ColorPointer];
                m_ColorPointer++;
                if (m_ColorPointer >= ColorList.Length)
                    m_ColorPointer = 0;

                // Add call to active list
                m_ActiveCalls.Add(call);

                return (m_Inc, c);
            }

            // Keep track of active calls
            public IEnumerable<GrpcTestCall> GetActiveCalls() => m_ActiveCalls;
            public void Completed(GrpcTestCall call) => m_ActiveCalls.Remove(call);

            // Timer utility callback to make delayed calls possible
            public event Action OnTimer;

            private float m_Time = 0;

            public void Update()
            {
                if (Time.realtimeSinceStartup >= m_Time)
                {
                    m_Time = Time.realtimeSinceStartup + 1.0f; // 1 sec delay
                    OnTimer?.Invoke();
                }
            }
        }

        private class GrpcTestCallFactory
        {
            public string ServerAddressInput = "https://127.0.0.1:50051";

            private readonly GrpcTestCallIdFactory m_IdFactory;
            private readonly char m_FactoryId;
            private readonly GRPCExampleLogger m_Logger;

            private GrpcChannel m_Channel;
            private HelloWorldService.HelloWorldServiceClient m_Client;

            public GrpcTestCallFactory(GrpcTestCallIdFactory idFactory, char factoryId, GRPCExampleLogger logger)
            {
                m_IdFactory = idFactory;
                m_FactoryId = factoryId;
                m_Logger = logger;
            }

            public GrpcTestCall CreateUnary(string detail = null)
            {
                EnsureClientConnection();
                return new UnaryGrpcTestCall(m_Logger, m_IdFactory, m_FactoryId, m_Client, detail);
            }

            public GrpcTestCall CreateServerStreaming()
            {
                EnsureClientConnection();
                return new ServerStreamingGrpcTestCall(m_Logger, m_IdFactory, m_FactoryId, m_Client);
            }

            public GrpcTestCall CreateClientStreaming()
            {
                EnsureClientConnection();
                return new ClientStreamingGrpcTestCall(m_Logger, m_IdFactory, m_FactoryId, m_Client);
            }

            public GrpcTestCall CreateBiStreaming()
            {
                EnsureClientConnection();
                return new BiStreamingGrpcTestCall(m_Logger, m_IdFactory, m_FactoryId, m_Client);
            }

            private void EnsureClientConnection()
            {
                // Setup GRPC channel
                GRPCBestHttpHandler httpHandler = new GRPCBestHttpHandler();
                m_Channel = GrpcChannel.ForAddress(ServerAddressInput, new GrpcChannelOptions
                {
                    HttpHandler = httpHandler
                });

                m_Client = new HelloWorldService.HelloWorldServiceClient(m_Channel);
            }
        }

        private abstract class GrpcTestCall
        {
            protected readonly GRPCExampleLogger Log;
            protected readonly GrpcTestCallIdFactory CallIdFactory;
            public readonly char FactoryId;
            public readonly int Inc;
            public readonly Color Color;

            private CancellationTokenSource m_CancelToken;

            protected readonly HelloWorldService.HelloWorldServiceClient Client;

            public GrpcTestCall(GRPCExampleLogger log, GrpcTestCallIdFactory callIdFactory, char factoryId,
                HelloWorldService.HelloWorldServiceClient client)
            {
                Log = log;
                FactoryId = factoryId;
                CallIdFactory = callIdFactory;
                (Inc, Color) = callIdFactory.CreateNext(this);
                Client = client;
            }

            protected abstract void CallImpl(CancellationToken cancelToken);

            public void Call()
            {
                m_CancelToken = new CancellationTokenSource();
                CallImpl(m_CancelToken.Token);
            }

            public void Cancel() => m_CancelToken.Cancel();

            protected void WriteLog(string msg)
            {
                var colorHex = ColorUtility.ToHtmlStringRGBA(Color);
                Log.WriteLogOutput($"<color=#{colorHex}>[{FactoryId}|{Inc}] {msg}</color>");
            }

            protected void FireCallStarted(string callName)
            {
                WriteLog($"Call {callName} <b>started</b>");
            }

            protected void FireCallException(Exception ex)
            {
                WriteLog($"Call exception: {ex.Message}");
            }

            protected void FireCallCompleted(Action printStatusFunc)
            {
                printStatusFunc();
                WriteLog("Call <b>completed</b>");
                CallIdFactory.Completed(this);
            }

            protected void ContinuationWithHeaders(Task<Metadata> task)
            {
                WriteLog("Metadata: " + task.Result);
            }

            protected void ContinuationWithResponse(Task<HelloResponse> task)
            {
                if (task.Exception != null)
                {
                    WriteLog($"Error: {task.Exception.Message}");
                }
                else
                {
                    var result = task.Result;
                    WriteLog($"Received: {result.Text}");
                }
            }

            protected async void ProcessServerResponses(IAsyncStreamReader<HelloResponse> responseStream,  Action printStatusFunc)
            {
                try
                {
                    var idx = 0;
                    while (await responseStream.MoveNext())
                    {
                        var response = responseStream.Current;
                        WriteLog($"Received({idx++}): {response.Text}");
                    }
                } catch (Exception e)
                {
                    FireCallException(e);
                }
                finally
                {
                    FireCallCompleted(printStatusFunc);
                }
            }

            protected void SendClientRequests(IClientStreamWriter<HelloRequest> requestStream)
            {
                var idx = 0;

               async void OnTimerFunc()
                {
                    try
                    {
                        if (idx < 5)
                        {
                            var msg = new HelloRequest() { Text = $"World {idx}" };
                            WriteLog($"Send({idx}): {msg.Text}");
                            await requestStream.WriteAsync(msg);
                            idx += 1;
                        }
                        else
                        {
                            await requestStream.CompleteAsync();
                            CallIdFactory.OnTimer -= OnTimerFunc;
                        }
                    }
                    catch (Exception e)
                    {
                        WriteLog($"Error Sending: {e.Message}");
                        CallIdFactory.OnTimer -= OnTimerFunc;
                    }
                }

                CallIdFactory.OnTimer += OnTimerFunc;
            }
        }

        void GUITestButtonArea(GrpcTestCallFactory callFactory)
        {
            GUILayout.BeginVertical();
            if (GUILayout.Button("Test Unary call"))
            {
                RunTestCallUnary(callFactory);
            }
            if (GUILayout.Button("Test Server-Streaming Call"))
            {
                RunTestCallServerStreaming(callFactory);
            }
            if (GUILayout.Button("Test Client-Streaming Call"))
            {
                RunTestCallClientStreaming(callFactory);
            }
            if (GUILayout.Button("Test Bidirectional-Streaming Call"))
            {
                RunTestCallBiStreaming(callFactory);
            }
            if (GUILayout.Button("Test Unary call (throw ex after resp)"))
            {
                RunTestCallUnary(callFactory, "exception-after");
            }
            if (GUILayout.Button("Test Unary call (throw ex before resp)"))
            {
                RunTestCallUnary(callFactory, "exception-before");
            }
            if (GUILayout.Button("Test Unary call (no response)"))
            {
                RunTestCallUnary(callFactory, "no-response");
            }
            GUILayout.EndVertical();
        }

        void GUICancelButtonArea(GrpcTestCallIdFactory callFactory)
        {
            GUILayout.BeginVertical();
            foreach(var call in callFactory.GetActiveCalls()) // FIXME: modified while reading...
            {
                var colorHex = ColorUtility.ToHtmlStringRGBA(call.Color);
                if (GUILayout.Button($"<color=#{colorHex}>[{call.FactoryId}|{call.Inc}] Cancel</color>"))
                {
                    call.Cancel();
                }
            }
            GUILayout.EndVertical();
        }

        void OnGUI()
        {
            int pad = 10;
            int screenWidth = Screen.width - pad * 2;

            m_CallFactoryA.ServerAddressInput = GUI.TextField(new Rect(pad, pad, screenWidth * 0.25f, 20), m_CallFactoryA.ServerAddressInput, 400);
            m_CallFactoryB.ServerAddressInput = GUI.TextField(new Rect(pad + screenWidth * 0.25f, pad, screenWidth * 0.25f, 20), m_CallFactoryB.ServerAddressInput, 400);

            var buttonAreaA = new Rect(pad, pad + 24, screenWidth * 0.25f, Screen.height);
            GUILayout.BeginArea(buttonAreaA);
            GUITestButtonArea(m_CallFactoryA);
            GUILayout.EndArea();

            var buttonAreaB = new Rect(pad + screenWidth * 0.25f, pad + 24, screenWidth * 0.25f, Screen.height);
            GUILayout.BeginArea(buttonAreaB);
            GUITestButtonArea(m_CallFactoryB);
            GUILayout.EndArea();

            var cancelArea = new Rect(pad + screenWidth * 0.25f * 3, pad, screenWidth * 0.25f, Screen.height);
            GUILayout.BeginArea(cancelArea);
            GUICancelButtonArea(m_CallIdFactory);
            GUILayout.EndArea();
        }

    }
}
