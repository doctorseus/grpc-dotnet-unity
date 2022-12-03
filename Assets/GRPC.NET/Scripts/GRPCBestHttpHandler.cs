using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BestHTTP;
using BestHTTP.Connections;
using BestHTTP.Connections.HTTP2;

namespace GRPC.NET
{
    public class GRPCBestHttpHandler : HttpClientHandler
    {
        private static readonly string ContentType = "Content-Type";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage grpcRequest, CancellationToken cancellationToken)
        {
            if (grpcRequest.Method != HttpMethod.Post)
                throw new NotSupportedException("GRPC only supports POST method.");

            HTTPRequest bestRequest = new HTTPRequest(grpcRequest.RequestUri, HTTPMethods.Post);
            bestRequest.MaxRetries = 0;

            foreach (var kv in grpcRequest.Headers)
            {
                foreach (var headerItem in kv.Value) bestRequest.AddHeader(kv.Key, headerItem);
            }
            bestRequest.AddHeader(ContentType, "application/grpc");


            PushPullStream outgoingDataStream = new PushPullStream("outgoing");
            outgoingDataStream.NonBlockingRead = true;
            bestRequest.UploadStream = outgoingDataStream;
            bestRequest.StreamChunksImmediately = true;
            bestRequest.ReadBufferSizeOverride = 0;
            bestRequest.UseUploadStreamLength = false;
            grpcRequest.Content.CopyToAsync(outgoingDataStream);

            bestRequest.OnBeforeHeaderSend += _ =>
            {
                string connectionKey = BestHTTP.Core.HostDefinition.GetKeyFor(grpcRequest.RequestUri, null);
                HTTPConnection httpConnection = BestHTTP.Core.HostManager
                    .GetHost(grpcRequest.RequestUri.Host)
                    .GetHostDefinition(connectionKey).Find(c => (c as HTTPConnection)?.requestHandler != null) as HTTPConnection;
                HTTP2Handler http2Handler = httpConnection?.requestHandler as HTTP2Handler;

                outgoingDataStream.OnStreamFlushCallback += () => http2Handler?.SignalRunnerThread();

                grpcRequest.Content.ReadAsStreamAsync().ContinueWith(_ =>
                {
                    outgoingDataStream.Close();
                }, cancellationToken);
            };

            TaskCompletionSource<HttpResponseMessage> grpcResponseTask = new TaskCompletionSource<HttpResponseMessage>();

            PushPullStream incomingDataStream = new PushPullStream("incoming");
            HttpResponseMessage grpcResponseMessage = new HttpResponseMessage
            {
                RequestMessage = grpcRequest,
                Content = new ServerStreamHttpContent(incomingDataStream)
            };

            bestRequest.OnHeadersReceived += (HTTPRequest _, HTTPResponse response, Dictionary<string, List<string>> headers) =>
            {
                bool isLeadingHeader = headers.Keys.Contains(":status");

                foreach (KeyValuePair<string, List<string>> kvp in headers)
                {
                    if (isLeadingHeader)
                    {
                        grpcResponseMessage.Content.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        grpcResponseMessage.TrailingHeaders.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                }

                if (isLeadingHeader)
                {
                    grpcResponseMessage.ReasonPhrase = response.Message;
                    grpcResponseMessage.StatusCode = (HttpStatusCode) response.StatusCode;
                    grpcResponseMessage.Version = new Version(response.VersionMajor, response.VersionMinor);
                }
                else if (headers.Keys.Contains("grpc-status"))
                {
                    incomingDataStream.Close();
                }
            };

            bestRequest.OnStreamingData += (_, _, fragment, length) =>
            {
                incomingDataStream.Write(fragment, 0, length);
                incomingDataStream.Flush();

                if (!grpcResponseTask.Task.IsCompleted)
                    grpcResponseTask.SetResult(grpcResponseMessage);

                return true;
            };

            bestRequest.Send();

            return grpcResponseTask.Task;
        }
    }
}