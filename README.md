# Seq.App.OpenTelemetry

Forwards Seq events to an OTLP-compatible endpoint as logs and traces.

> This codebase is in the early preview phase, as an exploratory exercise and proof-of-concept. It contains code ported generatively from other Seq apps hand-written by Datalust and external contributors. We welcome feedback and bug reports (which we'll always respond to personally!).

## Getting started

**1.** Install the app in Seq under _Settings > Apps_, using the `Seq.App.OpenTelemetry` package id.

Visit the Seq documentation for detailed information about
[installing and configuring Seq Apps](https://datalust.co/docs/installing-seq-apps).

**2.** Add an instance of the app under _Settings > App Instances_, and configure the OTLP endpoint and protocol.

**3.** Assign a stream so that matching events are forwarded to the OTLP endpoint.

## Settings

| Setting | Description | Default |
|---------|-------------|---------|
| Endpoint | The OTLP endpoint base URL. | `http://localhost:4318` (HTTP) or `http://localhost:4317` (gRPC) |
| Protocol | The OTLP export protocol: `HttpProtobuf` or `Grpc`. | `HttpProtobuf` |
| Headers | Additional headers sent with each export request, in `key=value,key=value` format. | |
| Resource attributes | Additional OTLP resource attributes in `key=value,key=value` format. | |
| Max batch size | The maximum number of events per export request. | `1000` |
| Max queue size | The maximum number of pending events before backpressure is applied. | `10000` |
| Flush interval (ms) | How often the background thread flushes pending events, in milliseconds. | `2000` |

## Development

Build the app:

```
dotnet build
```

Run the tests:

```
dotnet test
```

Run the smoke test against a local OTLP collector:

```
SEQ_APP_SETTING_ENDPOINT=http://localhost:4318 dotnet run --project test/Seq.App.OpenTelemetry.SmokeTest
```

## Acknowledgements

This codebase draws on the approach, structure, conventions, and examples in https://github.com/serilog/serilog-sinks-opentelemetry.
