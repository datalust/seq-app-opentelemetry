# Building Seq Apps

This skill covers building high-quality [Seq](https://datalust.co/seq) apps (plugins) using the `Seq.Apps` runtime API. Seq apps are .NET libraries that extend Seq with custom event processing (output apps) or event generation (input apps).

## Project structure

A Seq app follows this layout:

```
src/
  Seq.App.{Name}/
    Seq.App.{Name}.csproj          # Main library
    {Name}App.cs                   # SeqApp subclass (entry point)
    ...                            # Supporting types
    Resources/                     # Embedded resources (default templates, etc.)
  Seq.App.{Name}.SmokeTest/        # Optional console app for manual testing
    Program.cs
test/
  Seq.App.{Name}.Tests/
    {Name}AppTests.cs              # App-level integration tests
    ...Tests.cs                    # Unit tests for components
    Support/
      Test{Gateway}.cs             # Test doubles
      Some.cs                      # LogEvent factory helpers
```

Input apps use `Seq.Input.{Name}` naming.

### `.csproj` conventions

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Seq.Apps" Version="..." />
    <!-- Add Seq.Syntax (project or package reference) if using the Seq template language -->
  </ItemGroup>
</Project>
```

Embedded resources for default templates use `LogicalName` for clean resource stream access:

```xml
<ItemGroup>
  <EmbeddedResource Include="./Resources/DefaultHtmlTemplate.txt" LogicalName="DefaultHtmlTemplate" />
</ItemGroup>
```

Test projects target the latest stable .NET release, and use xUnit.

### Namespace disambiguation

Projects named `Seq.App.{Name}` will have a namespace that collides with `Seq.Apps.App` and `Seq.Apps.Host`. Use `global::` to disambiguate:

```csharp
// Without disambiguation, `App` resolves to the namespace `Seq.App.Telegram`
// rather than the type `Seq.Apps.App`.
public static LogEventPropertyValue? MyAppHost(global::Seq.Apps.Host host) { ... }
public static LogEventPropertyValue? MyAppInstance(global::Seq.Apps.App app) { ... }
```

This is needed anywhere `Seq.Apps.App` or `Seq.Apps.Host` is referenced as a type within a `Seq.App.*` namespace — typically in built-in functions, name resolvers, and message factories.

Use `InternalsVisibleTo` in the main project to expose `internal` constructors and types to the test project.

## App class structure

### Output apps (event subscribers)

Output apps derive from `SeqApp` and implement `ISubscribeToAsync<LogEvent>`:

```csharp
using Seq.Apps;
using Serilog.Events;

[SeqApp("App Name",
    Description = "A short sentence describing what the app does.")]
public class MyApp : SeqApp, ISubscribeToAsync<LogEvent>, IDisposable
{
    readonly IMyGateway _gateway;
    MyMessageFactory? _messageFactory;

    // Public parameterless constructor for production use
    public MyApp() : this(new MyHttpGateway()) { }

    // Internal constructor for testing (dependency injection)
    internal MyApp(IMyGateway gateway) { _gateway = gateway; }

    // Settings (see "App settings" section below)

    protected override void OnAttached()
    {
        // Validate required settings, resolve defaults, create factories
    }

    public async Task OnAsync(Event<LogEvent> evt)
    {
        // Process the event: render message, send via gateway
    }

    public void Dispose()
    {
        (_gateway as IDisposable)?.Dispose();
    }
}
```

The `SeqApp` attribute's `Name` parameter is the display name in Seq's UI. Keep it short (1-3 words). `Description` is a single sentence shown below the name.

### Input apps (event publishers)

Input apps implement `IPublishJson`:

```csharp
[SeqApp("My Input",
    Description = "Periodically does X and publishes metrics to Seq.")]
public class MyInput : SeqApp, IPublishJson, IDisposable
{
    public void Start(TextWriter inputWriter)
    {
        // Begin producing events; write CLEF JSON lines to inputWriter
        // Return immediately; use background tasks for ongoing work
    }

    public void Stop()
    {
        // Block until publishing has stopped
    }
}
```

### Lifecycle

1. Seq instantiates the app using the public parameterless constructor
2. `Attach()` is called, making `App`, `Host`, and `Log` available
3. `OnAttached()` runs - validate settings and initialize here
4. For subscribers: `OnAsync()` is called for each matching event (single-threaded)
5. For publishers: `Start()` is called, then `Stop()` on shutdown
6. `Dispose()` if implemented

Apps are hosted single-threaded: initialization, event handling, and disposal happen on the same thread. Apps may maintain their own background threads for ingestion.

## App settings

Settings are public properties on the app class decorated with `[SeqAppSetting]`. They appear in Seq's UI for the user to configure.

### `SeqAppSetting` attribute properties

| Property | Type | Purpose |
|---|---|---|
| `DisplayName` | `string` | User-facing label in the UI. If omitted, the property name is used. |
| `HelpText` | `string` | Descriptive text shown to the user. Should explain the setting's purpose and give examples. |
| `IsOptional` | `bool` | If `true`, the user can leave the field blank. Required settings should validate in `OnAttached()`. |
| `InputType` | `SettingInputType` | Controls the UI input widget: `Text`, `LongText`, `Checkbox`, `Integer`, `Decimal`, `Password`. If `Unspecified`, the runtime chooses based on the property type. |
| `Syntax` | `string` | Enables syntax highlighting: `"template"` for the Seq template language, `"code"` for fixed-width/generic code. |
| `IsInvocationParameter` | `bool` | Marks settings that can be overridden per-invocation (e.g., destination address, channel). Provides UI hints. |

### Quality guidelines for settings

**Display names** should be concise, title-case-ish, and match how users think about the concept:

| Good | Avoid |
|---|---|
| Bot token | BotToken |
| Chat ID | Telegram Chat Identifier |
| Silent notification | DisableNotification |
| Time zone | Time zone name |
| Message body | Message template |
| Parse mode | TelegramParseMode |
| Date/time format | DateTimeFormat |
| Body is plain text | BodyIsPlainText |
| Connection security | ProtocolSecurity |
| Extended error diagnostics | ExtendedErrorDiagnostics |

**Help text** should:
- Start with a clear description of the setting's purpose
- Include concrete examples where helpful (e.g., format strings, URL patterns, expected values)
- Mention defaults explicitly (e.g., "The default is `Etc/UTC`.", "The default is `POST`.")
- Use backticks for code/values in help text
- Note when template syntax is supported: "Template syntax is supported."
- For password/token fields, explain how to obtain the value

```csharp
// Good: concise display name, helpful description with examples and default
[SeqAppSetting(
    DisplayName = "Time zone",
    IsOptional = true,
    HelpText = "The IANA time zone name used when formatting dates and times " +
               "(e.g. Australia/Brisbane). The default is Etc/UTC.")]
public string? TimeZoneName { get; set; }

// Good: password input type, explains how to get the value
[SeqAppSetting(
    DisplayName = "Bot token",
    InputType = SettingInputType.Password,
    HelpText = "The bot's API token, in the format 123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11. " +
               "You can create a bot and obtain a token via @BotFather on Telegram.")]
public string? BotToken { get; set; }

// Good: template syntax, invocation parameter for per-event routing
[SeqAppSetting(
    DisplayName = "To address",
    Syntax = "template",
    IsInvocationParameter = true,
    HelpText = "The address to send email to. Multiple addresses are separated by " +
               "commas or semicolons. Template syntax is supported.")]
public string? To { get; set; }
```

### Setting validation

Validate required settings in `OnAttached()` and throw `ArgumentException` with a message that names the setting using its display name in backticks:

```csharp
protected override void OnAttached()
{
    var botToken = NormalizeOption(BotToken)
        ?? throw new ArgumentException("A `Bot token` must be supplied.");
    var chatId = NormalizeOption(ChatId)
        ?? throw new ArgumentException("A `Chat ID` must be supplied.");
    // ...
}

// Treat empty strings as null for optional settings
static string? NormalizeOption(string? s) => s == "" ? null : s;
```

### InputType usage patterns

| Scenario | InputType | Example |
|---|---|---|
| Short text (names, IDs, hosts) | `Text` (default) | Host, Chat ID, Tenant ID |
| Secrets and tokens | `Password` | Bot token, API key, Password |
| Multi-line text (templates, URLs, headers) | `LongText` | Message body, Target URLs, Other Headers |
| Boolean flags | `Checkbox` | Silent notification, Follow Redirects |
| Numeric values | `Integer` / `Decimal` | Port, Interval (seconds) |

### Syntax highlighting

- `Syntax = "template"` — for fields that accept the Seq template language (expressions in `{braces}`)
- `Syntax = "code"` — for fields with structured values (JSON, header format, HTTP method)
- Omit `Syntax` for plain values (timezone names, format strings, IDs)

## Gateway pattern (testability)

External I/O (HTTP APIs, SMTP, cloud services) is abstracted behind an interface so tests can run without network access.

```csharp
// Interface
interface IMyGateway
{
    Task<MyResponse> SendAsync(string param1, string param2, CancellationToken cancel);
}

// Production implementation
class MyHttpGateway : IMyGateway, IDisposable
{
    readonly HttpClient _httpClient = new();

    public async Task<MyResponse> SendAsync(...)
    {
        // HTTP calls, JSON serialization, response parsing
    }

    public void Dispose() => _httpClient.Dispose();
}

// Test double
class TestMyGateway : IMyGateway
{
    public List<(string Param1, string Param2)> Sent { get; } = [];
    public MyResponse NextResponse { get; set; } = new() { Ok = true };

    public Task<MyResponse> SendAsync(string param1, string param2, CancellationToken cancel)
    {
        Sent.Add((param1, param2));
        return Task.FromResult(NextResponse);
    }
}
```

The app class has dual constructors:

```csharp
public MyApp() : this(new MyHttpGateway()) { }       // Production
internal MyApp(IMyGateway gateway) { _gateway = gateway; }  // Testing
```

For simpler apps that don't need response inspection, an abstract base class pattern also works (see `HttpAppClient` in `seq-app-httprequest`).

## Template support (Seq.Syntax)

Apps that support user-customizable output should use the Seq template language via `Seq.Syntax`. This is the same expression language used in Seq's UI.

### Template compilation

Templates are compiled into `ExpressionTemplate` instances in `OnAttached()` or a factory constructor:

```csharp
using Seq.Syntax.Templates;
using Seq.Syntax.Templates.Encoding;

var template = new ExpressionTemplate(
    templateText,
    nameResolver: new OrderedNameResolver(new NameResolver[]
    {
        new StaticMemberNameResolver(typeof(MyBuiltInFunctions)),
        myAppNameResolver
    }),
    encoder: isPlainText ? null : new MyHtmlEncoder());
```

### Template rendering

Render a template against a `LogEvent`:

```csharp
static string Format(ExpressionTemplate template, LogEvent evt)
{
    var writer = new StringWriter();
    template.Format(evt, writer);
    return writer.ToString();
}
```

### Custom encoding

When output is HTML (or another format requiring escaping), implement `TemplateOutputEncoder`. The encoder is applied **only to interpolated values**, not to template literal text:

```csharp
class TemplateOutputTelegramHtmlEncoder : TemplateOutputEncoder
{
    public override string Encode(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
```

Other encoders in the codebase:
- `TemplateOutputHtmlEncoder` (seq-app-mail) — full HTML encoding via `System.Text.Encodings.Web.HtmlEncoder`
- `TemplateOutputUriEncoder` (seq-app-httprequest) — URI encoding via `Uri.EscapeDataString()`

### Built-in functions and name resolution

Apps can expose custom functions and properties within templates through a `NameResolver`:

```csharp
// Static methods become template functions
public static class MyAppBuiltInFunctions
{
    // Available as UriEncode(value) in templates
    public static LogEventPropertyValue? UriEncode(LogEventPropertyValue? value) { ... }

    // Available as InTimeZone(dateTime, timeZoneName) in templates
    public static LogEventPropertyValue? InTimeZone(
        LogEventPropertyValue? dateTime, LogEventPropertyValue? timeZoneName) { ... }

    // Bound to @Host in templates via the name resolver
    public static LogEventPropertyValue? MyAppHost(Host host) { ... }

    // Bound to @App in templates via the name resolver
    public static LogEventPropertyValue? MyAppInstance(App app) { ... }
}
```

The name resolver maps `@Host` and `@App` to the built-in functions and binds the actual `Host` and `App` instances as function parameters. See `TelegramAppNameResolver` or `MailAppNameResolver` for the implementation pattern.

### Default templates

Provide sensible default templates as embedded resources. Templates should:
- Handle both regular events and alert notifications (`@EventType = 0xA1E77001`)
- Include level indicators appropriate to the output format
- Show timestamp, message, properties, and exceptions
- Link back to the event in Seq using `@Host.BaseUri`
- Include a footer identifying the sender (e.g., "Sent by Seq at {@Host.BaseUri}")

For HTML templates, use only tags supported by the target (e.g., Telegram supports only `<b>`, `<i>`, `<code>`, `<pre>`, `<a>`).

## Message factory pattern

A factory class encapsulates template compilation and per-event rendering:

```csharp
class MyMessageFactory
{
    readonly ExpressionTemplate _body;
    readonly ExpressionTemplate _destination;

    public MyMessageFactory(
        string destinationTemplate,
        string bodyTemplate,
        bool bodyIsPlainText,
        string timeZoneName,
        string dateFormat,
        App app,
        Host host)
    {
        var nameResolver = new MyAppNameResolver(timeZoneName, dateFormat, app, host);
        _destination = CompileTemplate(destinationTemplate, nameResolver);
        _body = CompileTemplate(bodyTemplate, nameResolver,
            encoder: bodyIsPlainText ? null : new MyHtmlEncoder());
    }

    public MyMessage FromEvent(LogEvent evt)
    {
        var destination = Format(_destination, evt).Trim();
        var body = Format(_body, evt);

        // Enforce size limits if needed
        if (body.Length > MaxLength)
            body = body[..(MaxLength - 1)] + "\u2026"; // ellipsis

        return new MyMessage(destination, body);
    }
}
```

## Error handling

| Scenario | Pattern |
|---|---|
| Missing required settings | `ArgumentException` from `OnAttached()` — Seq reports as a configuration error |
| Invalid template syntax | `ArgumentException` from `ExpressionTemplate` constructor — fails at attachment time |
| Transient API errors (rate limits) | Log a warning via `Log.Warning()`, do **not** throw — avoids stalling the event pipeline |
| Permanent API errors (auth, bad request) | Throw `SeqAppException` with the error details — Seq records the failure against the event |
| Network failures | Let `HttpRequestException` propagate naturally — Seq treats as transient |

```csharp
public async Task OnAsync(Event<LogEvent> evt)
{
    var response = await _gateway.SendAsync(...);
    if (!response.Ok)
    {
        if (response.ErrorCode == 429) // Rate limited
        {
            Log.Warning("Rate limit exceeded; retry after {RetryAfter}s", response.RetryAfter);
            return; // Don't throw — prevents stalling
        }

        throw new SeqAppException($"API error {response.ErrorCode}: {response.Description}");
    }
}
```

## HTTP gateway implementation

When calling external HTTP APIs:

```csharp
class MyHttpGateway : IMyGateway, IDisposable
{
    readonly HttpClient _httpClient = new();

    public async Task<MyResponse> SendAsync(...)
    {
        // Build JSON with System.Text.Json (Utf8JsonWriter for control,
        // or JsonSerializer for simple cases)
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancel);
        var body = await response.Content.ReadAsStringAsync(cancel);
        return ParseResponse(body);
    }
}
```

- Use a single long-lived `HttpClient` (created in constructor, disposed with the gateway)
- Use `System.Text.Json` — no third-party HTTP or serialization libraries
- Omit null/default fields from JSON payloads (`JsonIgnoreCondition.WhenWritingNull` or manual construction)
- Parse responses defensively (external APIs may return unexpected shapes)

## Testing

### Test infrastructure

**`TestAppHost`** (from `Seq.Apps.Testing`): Provides a minimal `IAppHost` with test `App`, `Host`, and `Logger` instances. Used to attach the app in tests.

**`Some.cs`**: Factory helper for creating `LogEvent` instances:

```csharp
static class Some
{
    public static LogEvent InformationEvent(string message = "Test message",
        params object[] propertyValues)
    {
        var logger = new LoggerConfiguration().CreateLogger();
        logger.BindMessageTemplate(message, propertyValues,
            out var template, out var properties);
        return new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information,
            null, template!, [.. properties!]);
    }

    // Similar methods for WarningEvent, ErrorEvent, LogEventWithException, etc.
}
```

### Test categories

**App-level tests** — end-to-end through the `SeqApp` class using test gateways:

```csharp
[Fact]
public async Task EventsAreSentWithCorrectParameters()
{
    var gateway = new TestMyGateway();
    var app = new MyApp(gateway);
    var host = new TestAppHost();
    host.App.Settings["BotToken"] = "test-token";
    host.App.Settings["ChatId"] = "12345";
    app.Attach(host);

    await app.OnAsync(Some.InformationEvent());

    Assert.Single(gateway.Sent);
    Assert.Equal("test-token", gateway.Sent[0].BotToken);
    Assert.Equal("12345", gateway.Sent[0].ChatId);
}
```

**Factory/rendering tests** — template compilation and rendering in isolation:

- Plain text renders without encoding
- HTML mode encodes `<`, `>`, `&` in interpolated values
- Template literal HTML tags pass through unencoded
- Message truncation at size limits
- Alert events render the alert-specific template branch
- Built-in functions (`@Host`, `@App`, `InTimeZone()`, `UriEncode()`) work correctly

**Encoder tests** — parameterized tests for encoding correctness:

```csharp
[Theory]
[InlineData("", "")]
[InlineData("test", "test")]
[InlineData("&", "&amp;")]
[InlineData("<", "&lt;")]
[InlineData(">", "&gt;")]
[InlineData("a < b & c > d", "a &lt; b &amp; c &gt; d")]
public void SpecialCharactersAreEncoded(string input, string expected)
{
    var encoder = new MyHtmlEncoder();
    Assert.Equal(expected, encoder.Encode(input));
}
```

**Error handling tests**:
- Missing required settings throw during `OnAttached()`
- API errors throw `SeqAppException`
- Rate limits log a warning but don't throw

### Smoke test project

An optional console app for manual end-to-end verification against real services:

```csharp
// Reads settings from SEQ_APP_SETTING_* environment variables
// Creates the app with real gateway (parameterless constructor)
// Attaches via TestAppHost
// Sends a sample event and prints success/failure
```

Environment variable convention: `SEQ_APP_SETTING_{PROPERTYNAME}` (uppercase, no underscores in the property name part).

## README structure

READMEs follow this structure:

```markdown
# Seq.App.{Name}

{One sentence describing the app's purpose, mentioning Seq and the target service.}

## Getting started

**1.** {Any prerequisite setup on the target service.}

**2.** Install the app in Seq under _Settings > Apps_, using the `Seq.App.{Name}` package id.

Visit the Seq documentation for detailed information about
[installing and configuring Seq Apps](https://docs.datalust.co/docs/installing-seq-apps).

## Settings

{Table with columns: Property, Description, Template?, Default}

Settings marked **Template?** support the
[Seq template language](https://docs.datalust.co/docs/template-syntax), so event
properties and expressions can be included dynamically.

## Templates

{Description of the default template behavior and custom template support.}
{List of supported template features: built-in properties, functions, conditionals, iteration.}

## Development

{Instructions for the smoke test project and running tests.}
```

Key qualities:
- **Concise** — no unnecessary prose; users scan for information
- **Professional** — consistent formatting, no casual language
- **Actionable** — numbered steps, concrete examples
- **Setting names in the table match `DisplayName`**, not the C# property name
- **Template?** column indicates which settings support the Seq template language
- **Default** column shows the default value or links to the resource

## Runtime API reference

### Key types from `Seq.Apps` (in `seq-apps-runtime`)

| Type | Purpose |
|---|---|
| `SeqApp` | Abstract base class. Provides `App`, `Host`, `Log` after attachment. |
| `SeqAppAttribute` | Marks the entry point class. `Name` (short UI label), `Description` (sentence), `AllowReprocessing` (default `false`). |
| `SeqAppSettingAttribute` | Marks configurable properties. See "App settings" section. |
| `ISubscribeToAsync<LogEvent>` | For output apps processing Serilog `LogEvent` objects. |
| `ISubscribeToJsonAsync` | For output apps processing raw CLEF JSON strings (preferred for new code). |
| `IPublishJson` | For input apps that generate events. `Start(TextWriter)` / `Stop()`. |
| `Event<TData>` | Wrapper with `Id`, `EventType`, `Timestamp`, `Data`. Alert events have `EventType = 0xA1E77001`. |
| `App` | `Id`, `Title`, `Settings` (dictionary), `StoragePath`. |
| `Host` | `BaseUri`, `InstanceName`. |
| `SeqAppException` | Throw for app-specific errors; Seq records against the event. |
| `SettingInputType` | Enum: `Unspecified`, `Text`, `LongText`, `Checkbox`, `Integer`, `Decimal`, `Password`. |

### Important constants

- Alert event type: `0xA1E77001`
- Default time zone: `Etc/UTC`
- Default date/time format: `o` (ISO-8601 round-trip)

## Checklist for a new Seq app

1. **Project structure**: `src/` for the app, `test/` for tests, optional smoke test
2. **Target `net6.0`**, enable nullable reference types, treat warnings as errors
3. **`SeqApp` attribute** with a short name and one-sentence description
4. **Settings** with clear `DisplayName`, helpful `HelpText`, appropriate `InputType` and `Syntax`
5. **Gateway interface** abstracting external I/O for testability
6. **Validate settings** in `OnAttached()` with descriptive error messages
7. **Template support** via `Seq.Syntax` if the app produces formatted output
8. **Default templates** handling both regular events and alerts
9. **Encoding** appropriate to the output format (HTML, URI, etc.)
10. **Error handling**: `SeqAppException` for permanent errors, `Log.Warning()` for transient/rate-limit
11. **Tests**: app-level with test gateway, factory/rendering, encoder, error paths
12. **README**: getting started, settings table, template docs, development instructions
13. **`dotnet build` succeeds**, `dotnet test` passes, no warnings
