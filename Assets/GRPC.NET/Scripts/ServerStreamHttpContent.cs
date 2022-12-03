using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace GRPC.NET
{
    public class ServerStreamHttpContent : HttpContent
    {
        private readonly Stream m_Stream;

        public ServerStreamHttpContent(Stream memoryStream) => m_Stream = memoryStream;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => throw new NotSupportedException();

        protected override bool TryComputeLength(out long length) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(m_Stream);

        protected override void Dispose(bool disposing) { }
    }
}