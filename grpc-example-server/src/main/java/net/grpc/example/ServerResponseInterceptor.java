package net.grpc.example;


import io.grpc.*;

import java.util.Date;

public class ServerResponseInterceptor implements ServerInterceptor {

    class ServerMetadataCall<ReqT, RespT> extends ForwardingServerCall.SimpleForwardingServerCall<ReqT, RespT> {

        protected ServerMetadataCall(ServerCall<ReqT, RespT> delegate) {
            super(delegate);
        }

        @Override
        public void sendHeaders(Metadata headers) {
            headers.put(Metadata.Key.of("server-version", Metadata.ASCII_STRING_MARSHALLER), "1.0.0");
            System.out.println(" > wire headers [" + headers.keys().size() + "]");
            super.sendHeaders(headers);
        }

        @Override
        public void sendMessage(RespT message) {
            System.out.println(" > wire message");
            super.sendMessage(message);
        }

        @Override
        public void close(Status status, Metadata trailers) {
            trailers.put(Metadata.Key.of("time", Metadata.ASCII_STRING_MARSHALLER), new Date().toString());
            System.out.println(" > wire close [" + trailers.keys().size() + "]");
            super.close(status, trailers);
        }
    }

    @Override
    public <ReqT, RespT> ServerCall.Listener<ReqT> interceptCall(ServerCall<ReqT, RespT> call, Metadata headers, ServerCallHandler<ReqT, RespT> next) {
        ServerMetadataCall<ReqT, RespT> customServerCall = new ServerMetadataCall<>(call);
        return next.startCall(customServerCall, headers);
    }
}
