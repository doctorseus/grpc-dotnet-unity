﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Best.HTTP;

namespace GRPC.NET
{
    public class PushPullStream : Best.HTTP.Request.Upload.UploadStreamBase
    {
        private const long MAX_BUFFER_LENGTH = 5 * 1024 * 1024; // 5 MB

        public bool NonBlockingRead = false;

        private readonly string m_Name;

        private readonly Queue<byte> m_Buffer = new Queue<byte>();

        private bool m_Flushed;
        private bool m_Closed;

        private Exception m_Exception;

        public Action OnStreamFlushCallback;

        public PushPullStream(string name)
        {
            m_Name = name;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
                return 0;

            var readLength = 0;
            lock (m_Buffer)
            {
                while (!ReadAvailable(count))
                    Monitor.Wait(m_Buffer);

                for (; readLength < count && m_Buffer.Count > 0; readLength++)
                {
                    buffer[readLength] = m_Buffer.Dequeue();
                }

                Monitor.Pulse(m_Buffer);
            }

            // BestHTTP expects us to return -1 when we have no data (but have not reached EOF yet).
            if (readLength == 0 && !m_Closed)
                return -1;

            return readLength;
        }

        private bool ReadAvailable(int count)
        {
            if (m_Exception != null)
                throw m_Exception;

            // Either we have data to read, or we got flushed (e.g. stream got closed)
            // or we are in non blocking read mode.
            return m_Buffer.Count >= count && m_Flushed || m_Closed || NonBlockingRead;
        }

        // avoid sending content-length=0 (EOF) by using -2 for chunked encoding
        public override long Length => -2;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (m_Buffer)
            {
                while (m_Buffer.Count >= MAX_BUFFER_LENGTH)
                    Monitor.Wait(m_Buffer);

                for (var i = offset; i < offset + count; i++) m_Buffer.Enqueue(buffer[i]);

                m_Flushed = false;
                Monitor.Pulse(m_Buffer);
            }
        }

        public override void Flush()
        {
            m_Flushed = true;
            lock (m_Buffer)
                Monitor.Pulse(m_Buffer);
            OnStreamFlushCallback?.Invoke();
        }

        public override void Close() => CloseWithException(null);

        public void CloseWithException(Exception ex)
        {
            m_Exception = ex;
            m_Closed = true;
            Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotSupportedException();
        public override void SetLength(long value) => throw new System.NotSupportedException();

        public override void BeforeSendHeaders(HTTPRequest request)
        {
            // Each time gRPC flushes the stream the Http2Handler will have to be triggered so it writes the available
            // DATA package to the wire. But to get the http2Handler object we have to have an active HTTP2 connection
            // available first so we wait for the headers to be sent to set the OnStreamFlushCallback.

            // Signal Http2Handler each time a new DATA package should be written to the wire
            OnStreamFlushCallback += () => Signaler.SignalThread();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
    }
}