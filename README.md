# grpc-dotnet via HTTP2 for Unity

## TODO
 - Verify timeout behavior.
 - Verify call cancellation for server/client streaming.
 - Verify behavior for server disconnection.

## Introduction

Important: This package requires to have the latest version of BestHTTP/2 by Tivadar György Nagy!

Up to this point the only way to use gRPC with Unity was to use gRPC.Core. This implementation requires native platform-specific unmanaged libraries to support the HTTP2 communication and as of today is planned to be deprecated in May 2023.

The way forward and future to support gRPC within .NET/C# lays with grpc-dotnet. Unfortunately this new implementation (exclusively using managed code and without any platform-specific requirements) is not compatible with Unity as of now. This is because Unity currently does not support HTTP2 natively until the move to .NET 6/7 is completed.

This package uses the custom BestHTTP/2 package as HTTP2 client to replace the transport layer of grpc-dotnet and enable support of unary, client-streaming, server-streaming and bidirectional-streams across supported platforms.

Important: Any gRPC server implementation is not supported! Only gRPC client mode.

Apart of the glue code to enable this, this package also includes a test scene and code used to verify gRPC functionality and as an example of usage of the gRPC async API.

Latest versions of grpc-dotnet and additional required dependencies are also included.

The documentation for gRPC and it's API can be found on https://grpc.io/docs/languages/csharp/.

## Setup

After importing this package also import the latest version of **BestHTTP/2** via the package manager.
When creating a connection with gRPC the only thing required is to provide an instance of `GRPCBestHttpHandler` to `GrpcChannelOptions.HttpHandler` when initiating a new channel.
This will make sure that BestHTTP/2 is used when creating a new HTTP2 channel to the server. 
This class will make sure that all outgoing and ingoing communication from grpc-dotnet is handled by BestHTTP/2.

```
GRPCBestHttpHandler httpHandler = new GRPCBestHttpHandler();
m_Channel = GrpcChannel.ForAddress(ServerAddressInput, new GrpcChannelOptions
{
    HttpHandler = httpHandler
});

m_Client = new HelloWorldService.HelloWorldServiceClient(m_Channel);
```

## Demo Scene

The Demo Scene provides the possibility to send unary, client-streaming, server-streaming and bidirectional-streaming calls to one (or two via a secondary channel) gRPC server. The proto file used for this service can be found in `devlabs.pro/GRPC.NET/Example/Proto/hello.proto`.


### Demo Server (Java)

This package also includes a gradle based Java gGRPC server project (`grpc-example-server/`) for testing purposes with this client. 

If you are not familiar with Java, it is recommended to use https://www.jetbrains.com/idea/ to open and run the example server class `net.grpc.example.HelloWorldServer`.

# Release Notes

## Version 1.0.0
- Initial Release
- Dependencies:
    - grpc.core.api.2.50.0 (netstandard2.1)
    - grpc.net.client.2.50.0 (netstandard2.1)
    - grpc.net.common.2.50.0 (netstandard2.1)
    - google.protobuf.3.21.9 (netstandard2.0)
    - system.runtime.compilerservices.unsafe.4.5.2 (netstandard2.0)
    - microsoft.extensions.logging.abstractions.3.0.3 (netstandard2.0)
    - system.diagnostics.diagnosticsource.4.5.1 (netstandard1.3)
