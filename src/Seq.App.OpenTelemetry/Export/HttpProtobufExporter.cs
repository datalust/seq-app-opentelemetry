using System.Net;
using Google.Protobuf;

namespace Seq.App.OpenTelemetry.Export;

class HttpProtobufExporter : IOtlpExporter
{
    readonly HttpClient _httpClient;
    readonly Uri _logsEndpoint;
    readonly Uri _tracesEndpoint;

    public HttpProtobufExporter(string endpoint, IReadOnlyList<KeyValuePair<string, string>>? headers)
    {
        var baseUri = endpoint.TrimEnd('/');
        _logsEndpoint = new Uri($"{baseUri}/v1/logs");
        _tracesEndpoint = new Uri($"{baseUri}/v1/traces");

        _httpClient = new HttpClient();
        if (headers != null)
        {
            foreach (var header in headers)
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    public async Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct)
    {
        await SendAsync(request, _logsEndpoint, ct);
    }

    public async Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct)
    {
        await SendAsync(request, _tracesEndpoint, ct);
    }

    async Task SendAsync(IMessage request, Uri endpoint, CancellationToken ct)
    {
        var data = request.ToByteArray();

        using var response = await PostProtobufAsync(data, endpoint, ct);

        if (IsRetryableStatusCode(response.StatusCode))
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            await Task.Delay(retryAfter, ct);

            using var retryResponse = await PostProtobufAsync(data, endpoint, ct);
            retryResponse.EnsureSuccessStatusCode();
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    async Task<HttpResponseMessage> PostProtobufAsync(byte[] data, Uri endpoint, CancellationToken ct)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
        return await _httpClient.PostAsync(endpoint, content, ct);
    }

    static bool IsRetryableStatusCode(HttpStatusCode code)
    {
        return code is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return default;
    }
}
