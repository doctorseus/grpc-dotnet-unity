package net.grpc.example;

import io.grpc.Metadata;
import io.grpc.Status;
import io.grpc.stub.StreamObserver;
import net.grpc.example.protos.HelloRequest;
import net.grpc.example.protos.HelloResponse;
import net.grpc.example.protos.HelloWorldServiceGrpc;

import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

public class HelloWorldServiceImpl extends HelloWorldServiceGrpc.HelloWorldServiceImplBase {

    private ScheduledExecutorService e = Executors.newSingleThreadScheduledExecutor();

    @Override
    public void hello(HelloRequest request, StreamObserver<HelloResponse> responseObserver) {
        String message = request.getText();
        System.out.println("hello() called");
        System.out.println(" > received " + message);

        if (!message.contains("[no-response]")) {
            if (message.contains("[exception-before]")) {
                responseObserver.onError(Status.INVALID_ARGUMENT.withDescription("Before Response Exception").asException());
                return;
            } else if (message.contains("[exception-before-meta]")) {
                Metadata metadata = new Metadata();
                metadata.put(Metadata.Key.of("metadata-key", Metadata.ASCII_STRING_MARSHALLER), "metadata-value");
                metadata.put(Metadata.Key.of("metadata-key1", Metadata.ASCII_STRING_MARSHALLER), "metadata-value1");
                responseObserver.onError(Status.INVALID_ARGUMENT.withDescription("Before Response Exception with Metadata").asException(metadata));
                return;
            }

            String txt = "Hello " + message;
            System.out.println(" > send " + txt + " + done");
            responseObserver.onNext(HelloResponse.newBuilder().setText(txt).build());

            if (message.contains("[exception-after]")) {
                responseObserver.onError(Status.INTERNAL.withDescription("After Response Exception").asException());
                return;
            } else if (message.contains("[exception-after-meta]")) {
                Metadata metadata = new Metadata();
                metadata.put(Metadata.Key.of("metadata-key", Metadata.ASCII_STRING_MARSHALLER), "metadata-value");
                metadata.put(Metadata.Key.of("metadata-key1", Metadata.ASCII_STRING_MARSHALLER), "metadata-value1");
                responseObserver.onError(Status.INTERNAL.withDescription("After Response Exception with Metadata").asException(metadata));
                return;
            }
        }

        responseObserver.onCompleted();
    }


    private void sendMessageFor(StreamObserver<HelloResponse> responseObserver, String message, int number) {
        if (number > 0) {
            String txt = message + " ["+ number + "]";
            System.out.println(" > send " + txt);
            responseObserver.onNext(HelloResponse.newBuilder().setText(txt).build());
            e.schedule(() -> sendMessageFor(responseObserver, message, number -1), 3, TimeUnit.SECONDS);
        } else {
            System.out.println(" > end of stream ");
            responseObserver.onCompleted();
        }
    }

    @Override
    public void helloServer(HelloRequest request, StreamObserver<HelloResponse> responseObserver) {
        System.out.println("helloServer() called");
        System.out.println(" > received " + request.getText());
        sendMessageFor(responseObserver, "Hello " + request.getText(), 4);
    }

    @Override
    public StreamObserver<HelloRequest> helloClient(StreamObserver<HelloResponse> responseObserver) {
        System.out.println("helloClient() called");
        return new StreamObserver<HelloRequest>() {

            int count = 0;

            @Override
            public void onNext(HelloRequest request) {
                String message = request.getText();
                System.out.println(" > received " + message);

                if (message.contains("[stop]")) {
                    responseObserver.onError(Status.INTERNAL.withDescription("Abort from Server side").asException());
                }

                count++;
            }

            @Override
            public void onError(Throwable t) {
                System.out.println(" > received error: " + t.getMessage());
            }

            @Override
            public void onCompleted() {
                System.out.println(" > received total of " + count + " messages");
                responseObserver.onNext(HelloResponse.newBuilder().setText("Hello to all  " + count).build());
                responseObserver.onCompleted();
            }
        };
    }

    @Override
    public StreamObserver<HelloRequest> helloBoth(StreamObserver<HelloResponse> responseObserver) {
        System.out.println("helloBoth() called");
        return new StreamObserver<HelloRequest>() {

            int count = 0;

            @Override
            public void onNext(HelloRequest request) {
                String message = request.getText();
                System.out.println(" > received " + request.getText());
                String txt = message + " ["+ count + "]";
                System.out.println(" > send " + txt);
                responseObserver.onNext(HelloResponse.newBuilder().setText(txt).build());
                count++;
            }

            @Override
            public void onError(Throwable t) {
                System.out.println(" > received error: " + t.getMessage());
            }

            @Override
            public void onCompleted() {
                System.out.println(" > received and send total of " + count + " messages");
                responseObserver.onCompleted();
            }
        };
    }

}
