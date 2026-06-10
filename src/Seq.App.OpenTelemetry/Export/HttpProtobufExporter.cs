using System.Net;
using Google.Protobuf;
using Serilog;

namespace Seq.App.OpenTelemetry.Export;

class HttpProtobufExporter : OtlpExporter
{
    static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    readonly HttpClient _httpClient;
    readonly Uri _logsEndpoint;
    readonly Uri _tracesEndpoint;
    readonly ILogger _log;

    public HttpProtobufExporter(string endpoint, IReadOnlyList<KeyValuePair<string, string>>? headers, ILogger log)
    {
        _log = log;

        // The configured endpoint is a base URL with `/v1/<signal>` appended per request;
        // tolerate a pasted signal-specific URL rather than producing `/v1/logs/v1/logs`.
        var baseUri = endpoint.TrimEnd('/');
        foreach (var signalPath in new[] { "/v1/logs", "/v1/traces" })
        {
            if (baseUri.EndsWith(signalPath, StringComparison.OrdinalIgnoreCase))
            {
                baseUri = baseUri[..^signalPath.Length];
                break;
            }
        }

        _logsEndpoint = new Uri($"{baseUri}/v1/logs");
        _tracesEndpoint = new Uri($"{baseUri}/v1/traces");

        _httpClient = new HttpClient { Timeout = RequestTimeout };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        if (headers != null)
        {
            foreach (var header in headers)
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    public override async Task ExportLogsAsync(ExportLogsServiceRequest request, CancellationToken ct)
    {
        var responseBody = await SendAsync(request, _logsEndpoint, ct);

        var partialSuccess = TryParseResponse(responseBody, ExportLogsServiceResponse.Parser)?.PartialSuccess;
        if (partialSuccess != null && (partialSuccess.RejectedLogRecords > 0 || partialSuccess.ErrorMessage.Length > 0))
        {
            _log.Warning("OTLP logs export partially succeeded; {RejectedCount} log record(s) rejected: {ErrorMessage}",
                partialSuccess.RejectedLogRecords, partialSuccess.ErrorMessage);
        }
    }

    public override async Task ExportTracesAsync(ExportTraceServiceRequest request, CancellationToken ct)
    {
        var responseBody = await SendAsync(request, _tracesEndpoint, ct);

        var partialSuccess = TryParseResponse(responseBody, ExportTraceServiceResponse.Parser)?.PartialSuccess;
        if (partialSuccess != null && (partialSuccess.RejectedSpans > 0 || partialSuccess.ErrorMessage.Length > 0))
        {
            _log.Warning("OTLP traces export partially succeeded; {RejectedCount} span(s) rejected: {ErrorMessage}",
                partialSuccess.RejectedSpans, partialSuccess.ErrorMessage);
        }
    }

    async Task<byte[]> SendAsync(IMessage request, Uri endpoint, CancellationToken ct)
    {
        var data = request.ToByteArray();

        var response = await PostProtobufAsync(data, endpoint, ct);
        try
        {
            if (IsRetryableStatusCode(response.StatusCode))
            {
                var retryDelay = GetRetryDelay(response);
                response.Dispose();
                await Task.Delay(retryDelay, ct);

                response = await PostProtobufAsync(data, endpoint, ct);
            }

            await EnsureSuccessAsync(response, ct);

            return response.Content.Headers.ContentType?.MediaType == "application/x-protobuf"
                ? await response.Content.ReadAsByteArrayAsync(ct)
                : [];
        }
        finally
        {
            response.Dispose();
        }
    }

    async Task<HttpResponseMessage> PostProtobufAsync(byte[] data, Uri endpoint, CancellationToken ct)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
        return await _httpClient.PostAsync(endpoint, content, ct);
    }

    static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        // The response body usually carries the actual rejection reason (a `google.rpc.Status`,
        // often JSON or text); include an excerpt so export failures are diagnosable.
        var detail = "";
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            detail = body.Length > 512 ? body[..512] : body;
        }
        catch
        {
            // The status line alone will have to do.
        }

        throw new HttpRequestException(
            $"The OTLP endpoint responded with {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
    }

    static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
            ?? retryAfter?.Date - DateTimeOffset.UtcNow
            ?? TimeSpan.FromSeconds(1);

        return delay < TimeSpan.Zero ? TimeSpan.Zero
            : delay > MaxRetryDelay ? MaxRetryDelay
            : delay;
    }

    static T? TryParseResponse<T>(byte[] body, MessageParser<T> parser)
        where T : class, IMessage<T>
    {
        if (body.Length == 0)
            return null;

        try
        {
            return parser.ParseFrom(body);
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    static bool IsRetryableStatusCode(HttpStatusCode code)
    {
        return code is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    public override ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return default;
    }
}
