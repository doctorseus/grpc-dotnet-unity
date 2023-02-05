package net.grpc.example;

import io.grpc.*;
import io.grpc.netty.GrpcSslContexts;
import io.grpc.netty.NettyServerBuilder;
import io.netty.handler.codec.http2.Http2SecurityUtil;
import io.netty.handler.ssl.*;

import java.io.File;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.SocketAddress;
import java.util.ArrayList;

public class HelloWorldServer {
    private static final int PORT = 50051;
    private Server server;

    public void start() throws IOException {

        SslContextBuilder scb = SslContextBuilder.forServer(new File("cert.pem"), new File("key.pem"));
        GrpcSslContexts.configure(scb, SslProvider.JDK);

        ArrayList<String> allCiphers = new ArrayList<>(Http2SecurityUtil.CIPHERS);
        allCiphers.add(Ciphers.TLS_RSA_WITH_AES_128_GCM_SHA256);
        allCiphers.add(Ciphers.TLS_RSA_WITH_AES_128_CBC_SHA256);
        allCiphers.add(Ciphers.TLS_RSA_WITH_AES_128_CBC_SHA);
        scb.ciphers(allCiphers, SupportedCipherSuiteFilter.INSTANCE);

        SslContext sslContext  = scb.build();
        SocketAddress socketAddress = new InetSocketAddress("127.0.0.1", PORT);
        server = NettyServerBuilder.forAddress(socketAddress)
                .sslContext(sslContext)
                .addService(new HelloWorldServiceImpl())
                .build();
        server.start();
    }

    public void blockUntilShutdown() throws InterruptedException {
        if (server == null) return;
        server.awaitTermination();
    }

    public static void main(String[] args) throws InterruptedException, IOException {
        HelloWorldServer server = new HelloWorldServer();
        server.start();
        server.blockUntilShutdown();
    }
}