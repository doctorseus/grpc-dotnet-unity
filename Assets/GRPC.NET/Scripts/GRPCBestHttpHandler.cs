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

        /**
         * This function is called by gRPC when establishing a new channel to a gRPC server.
         * We are mapping HttpRequestMessage and HttpResponseMessage to its BestHTTP equivalent.
         */
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage grpcRequest, CancellationToken cancellationToken)
        {
            if (grpcRequest.Method != HttpMethod.Post)
                throw new NotSupportedException("gRPC only supports POST method.");

            //
            // Create outgoing HTTP2 request
            //
            HTTPRequest bestRequest = new HTTPRequest(grpcRequest.RequestUri, HTTPMethods.Post);

            // Disable internal retry
            bestRequest.MaxRetries = 0;

            //
            // Prepare outgoing HEADER and HttpContent
            //

            // Copy over all request headers
            foreach (var kv in grpcRequest.Headers)
            {
                foreach (var headerItem in kv.Value) bestRequest.AddHeader(kv.Key, headerItem);
            }

            // Contained in grpcRequest.Content.Headers but we set it hardcoded here
            bestRequest.AddHeader(ContentType, "application/grpc");


            // Create outgoing data stream
            PushPullStream outgoingDataStream = new PushPullStream("outgoing");

            // BestHTTP does not perform blocking reads. Instead it will expect -1 to be returned if no data is yet
            // available. Each time the internal loop is triggered it will try to read from the stream again to check
            // if there is new data available.
            // This is why we have to trigger Http2Handler on each new DATA package when the stream was flushed.
            outgoingDataStream.NonBlockingRead = true;
            bestRequest.UploadStream = outgoingDataStream;

            // StreamFragmentSize will be ignored and downloaded chunks will be sent immediately.
            bestRequest.StreamChunksImmediately = true;
            bestRequest.ReadBufferSizeOverride = 0;
            bestRequest.UseUploadStreamLength = false; // avoid sending content-length=0 (EOF)

            // CopyToAsync can replace the underlying Stream of a HttpContent object as long as no write() call
            // was yet initiated/completed on it. This will allow us to provide our own Stream to gRPC on which
            // it then performs its writes on, allowing us to act on these calls.
            grpcRequest.Content.CopyToAsync(outgoingDataStream);

            // Each time gRPC flushes the stream the Http2Handler will have to be triggered so it writes the available
            // DATA package to the wire. But to get the http2Handler object we have to have an active HTTP2 connection
            // available first so we wait for the headers to be sent to set the OnStreamFlushCallback.
            bestRequest.OnBeforeHeaderSend += _ =>
            {
                string connectionKey = BestHTTP.Core.HostDefinition.GetKeyFor(grpcRequest.RequestUri, null);
                HTTPConnection httpConnection = BestHTTP.Core.HostManager
                    .GetHost(grpcRequest.RequestUri.Host)
                    .GetHostDefinition(connectionKey).Find(c => (c as HTTPConnection)?.requestHandler != null) as HTTPConnection;
                HTTP2Handler http2Handler = httpConnection?.requestHandler as HTTP2Handler;

                // Signal Http2Handler each time a new DATA package should be written to the wire
                outgoingDataStream.OnStreamFlushCallback += () => http2Handler?.SignalRunnerThread();

                // This will complete when we reached EOS of the gRPC request
                grpcRequest.Content.ReadAsStreamAsync().ContinueWith(_ =>
                {
                    outgoingDataStream.Close();
                }, cancellationToken);
            };


            //
            // Prepare HttpResponseMessage mapping incoming HEADER and DATA to forward to gRPC
            //
            TaskCompletionSource<HttpResponseMessage> grpcResponseTask = new TaskCompletionSource<HttpResponseMessage>();

            PushPullStream incomingDataStream = new PushPullStream("incoming");
            HttpResponseMessage grpcResponseMessage = new HttpResponseMessage
            {
                RequestMessage = grpcRequest,
                // HttpContent wrapper around incoming DATA package stream
                Content = new ServerStreamHttpContent(incomingDataStream)
            };

            // Write incoming headers OR trailing headers
            bool isHeader = true;
            bestRequest.OnHeadersReceived += (HTTPRequest _, HTTPResponse response, Dictionary<string, List<string>> headers) =>
            {
                // If we haven't received headers yet and grpc-status is included then its a trailers only response.
                bool trailersOnly = isHeader && headers.Keys.Contains("grpc-status");

                // https://github.com/grpc/grpc-dotnet/blob/master/src/Grpc.Net.Client/Internal/GrpcCall.cs#L311
                foreach (KeyValuePair<string, List<string>> kvp in headers)
                {
                    // Content.Headers is used for content-type and other well known headers, always populate them
                    grpcResponseMessage.Content.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);

                    // Trailer only responses have all the headers in both, metadata and trailers.
                    // If we add them to Headers, gRPC will take care of that for us. In any other case add them
                    // to the TrailingHeaders.
                    if (isHeader || trailersOnly)
                    {
                        // Add headers
                        grpcResponseMessage.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        grpcResponseMessage.TrailingHeaders.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                }

                // Copy HTTP status fields
                if (isHeader)
                {
                    grpcResponseMessage.ReasonPhrase = response.Message;
                    grpcResponseMessage.StatusCode = (HttpStatusCode) response.StatusCode;
                    grpcResponseMessage.Version = new Version(response.VersionMajor, response.VersionMinor);
                }

                // Once we received the trailer (or if its a trailers only response) we close
                if (trailersOnly || !isHeader)
                {
                    incomingDataStream.Close();
                }

                // Complete Response on first HEADER package (before DATA arrived) to trigger gRPC
                if (!grpcResponseTask.Task.IsCompleted)
                    grpcResponseTask.SetResult(grpcResponseMessage);

                // From now on everything we get are trailers
                isHeader = false;
            };


            // For each incoming DATA package we write data trough to gRPC
            bestRequest.OnStreamingData += (_, _, fragment, length) =>
            {
                // Write incoming DATA package and immediately flush
                incomingDataStream.Write(fragment, 0, length);
                incomingDataStream.Flush();
                return true;
            };

            // When gRPC call is canceled by the application we abort the request
            var cancellationTokenRegistration = cancellationToken.Register(() =>
            {
                bestRequest.Abort();
            });

            bestRequest.Callback += (request, response) =>
            {
                // We might have to handle an error when his callback is called after the request completed
                if (request.State != HTTPRequestStates.Finished)
                {
                    var ex = request.Exception ?? new Exception($"Unknown error while processing grpc req/resp (state={request.State}).");

                    // If the call was aborted instead we set the exception accordingly
                    if (request.State == HTTPRequestStates.Aborted)
                    {
                        ex = new Exception("gRPC call aborted by client.");
                    }

                    // If response IS NOT set we never got a HEADER response (arrives before any DATA)
                    if (!grpcResponseTask.Task.IsCompleted)
                    {
                        grpcResponseTask.SetException(ex);
                    }
                    // If response IS set we instead throw an exception in the blocking read() and write() thread
                    else
                    {
                        incomingDataStream.CloseWithException(ex);
                    }
                }
                // but in any case we want to close the stream to indicate to grpc that we are done
                else
                {
                    incomingDataStream.Close();
                }

                // Unregister cancellation token once we are done
                cancellationTokenRegistration.Dispose();
            };

            // Finally send request to initiate transfer
            bestRequest.Send();

            return grpcResponseTask.Task;
        }
    }
}