package net.grpc.example;

import io.grpc.*;
public class ClientRequestInterceptor implements ServerInterceptor {

    public static final Context.Key<Object> USER_IDENTITY = Context.key("identity");
    @Override
    public <ReqT, RespT> ServerCall.Listener<ReqT> interceptCall(ServerCall<ReqT, RespT> call, Metadata headers, ServerCallHandler<ReqT, RespT> next) {
        System.out.println(" > intercept request headers=" + headers);

        String identity = headers.get(Metadata.Key.of("Authorization", Metadata.ASCII_STRING_MARSHALLER));
        if (identity == null) {
            call.close(Status.UNAUTHENTICATED.withDescription("No token found in header"), new Metadata());
            return new ServerCall.Listener() {};
        }

        Context context = Context.current().withValue(USER_IDENTITY, identity);
        return Contexts.interceptCall(context, call, headers, next);
    }

}
