using System.ComponentModel;

namespace Seq.App.OpenTelemetry.Settings;

public enum OtlpExportProtocol
{
    [Description("HTTP/Protobuf")]
    HttpProtobuf,
    [Description("gRPC")]
    Grpc
}
