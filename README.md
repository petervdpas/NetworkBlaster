# NetworkBlast 🌐

[![NuGet](https://img.shields.io/nuget/v/NetworkBlast.svg)](https://www.nuget.org/packages/NetworkBlast)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetworkBlast.svg)](https://www.nuget.org/packages/NetworkBlast)
[![License](https://img.shields.io/github/license/petervdpas/NetworkBlast.svg)](https://opensource.org/licenses/MIT)

![NetworkBlast](https://raw.githubusercontent.com/petervdpas/NetworkBlast/master/assets/icon.png)

**NetworkBlast** is a programmable HTTP/REST client for .NET — a sibling to
[AzureBlast](https://www.nuget.org/packages/AzureBlast) in the Blast family.
Think "Postman as a library": named connections, lazy auth, script-friendly defaults.

---

> ✅ **Status:** 1.0 — full feature set: REST verbs + JSON / XML / SOAP, seven
> auth factories (bearer, basic, API-key header *or* query, OAuth2 client-credentials
> with token caching, NTLM/Windows, cookie jar), typed OData v4 with auto-paging,
> per-request retry / timeout / headers / query. 246 tests green, .NET 10.

---

## ✨ Why NetworkBlast

* 🔹 **Script-first** — works from `.csx`, LINQPad and PowerShell with one line of setup.
* 🔹 **Vault-agnostic** — secrets are pulled through a `Func<category, key, ct, Task<string>>` delegate, *not* a hard reference to SecretBlast or anything else. Wire any resolver you like.
* 🔹 **No Blast-on-Blast deps** — the only NuGet reference is `Microsoft.Extensions.DependencyInjection.Abstractions`.
* 🔹 **Lazy hydration** — base URL and auth are resolved on first request, then cached.
* 🔹 **Built-in resilience** — opt-in retry on 5xx / 408 / 429 / network errors, with `Retry-After` honored, plus per-request timeout.
* 🔹 **JSON helpers in the box** — `GetJsonAsync<T>` / `PostJsonAsync<T>` / `PutJsonAsync<T>` / `PatchJsonAsync<T>`, with configurable `JsonSerializerOptions`.
* 🔹 **Typed OData v4** — LINQ-flavored chain (`.Where(c => c.Status == "Active")`), auto-paging `IAsyncEnumerable<T>`, and a vault-agnostic filter DSL with operator overloads.
* 🔹 **Real-world auth** — bearer, basic, API key (header *or* query), OAuth2 client-credentials with token caching + refresh, NTLM / Windows auth, and a cookie jar.
* 🔹 **SOAP + XML in the box** — SOAP 1.1 / 1.2 envelope helper, typed `SendSoapAsync<TReq, TResp>` round-trip, `Fault` → exception, plus general `GetXmlAsync<T>` / `PostXmlAsync` JSON-mirrors.

---

## 📦 Installation

```bash
dotnet add package NetworkBlast
```

---

## 🚀 Script Quick-Start

### `.csx` — bearer token, no vault

```csharp
#r "nuget: NetworkBlast, 1.0.0"
using NetworkBlast;

var gh = NetClient.WithToken("https://api.github.com/", "ghp_xxx");
var body = await gh.GetStringAsync("repos/octocat/hello-world");
Console.WriteLine(body);
```

### `.csx` — API-key header

```csharp
#r "nuget: NetworkBlast, 1.0.0"
using NetworkBlast;

var api = NetClient.WithApiKey("https://api.example.com/", "X-API-Key", "secret");
var data = await api.GetJsonAsync<Thing>("things/42");

record Thing(int Id, string Name);
```

### `.csx` — Basic auth

```csharp
var jira = NetClient.WithBasicAuth("https://acme.atlassian.net/", "alice@acme.com", apiToken);
var issue = await jira.GetJsonAsync<JiraIssue>("rest/api/3/issue/PROJ-123");
```

### `.csx` — API key in the query string

```csharp
var weather = NetClient.WithApiKeyQuery("https://api.openweathermap.org/", "appid", "abc123");
var paris = await weather.GetStringAsync("data/2.5/weather?q=Paris");
```

### `.csx` — OAuth2 client credentials (token cached + auto-refresh)

```csharp
#r "nuget: NetworkBlast, 1.0.0"
using NetworkBlast;

var graph = NetClient.WithOAuth2ClientCredentials(
    baseUrl:       "https://graph.microsoft.com/v1.0/",
    tokenEndpoint: "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
    clientId:      "...",
    clientSecret:  "...",
    scope:         "https://graph.microsoft.com/.default");

var users = await graph.GetJsonAsync<GraphUsers>("users");
```

The token is fetched on first request, cached for its `expires_in` lifetime, refreshed automatically before expiry, and re-fetched once on a `401 Unauthorized` response in case it went stale mid-flight.

### `.csx` — Windows Integrated Auth (NTLM / Negotiate / Kerberos)

```csharp
var intranet = NetClient.WithWindowsAuth("https://intranet.acme.corp/api/");
// or with explicit creds:
var creds = new NetworkCredential("svc-account", "p@ssw0rd", "CORP");
var intranet = NetClient.WithWindowsAuth("https://intranet.acme.corp/api/", creds);
```

### `.csx` — Cookie jar (login + session)

```csharp
var jar = new CookieContainer();
var site = NetClient.WithCookieJar("https://legacy-portal.example.test/", jar);

await site.PostJsonAsync<object>("login", new { user = "alice", pass = "..." });
// session cookie set by the server is auto-replayed on follow-up calls:
var report = await site.GetJsonAsync<Report>("reports/today");
```

### `.csx` inside TaskBlaster — resolver from `Secrets`

```csharp
#r "nuget: NetworkBlast, 1.0.0"
using NetworkBlast;

var gh = new NetClient(Secrets.Resolver, "github");
var repo = await gh.GetJsonAsync<Repo>("repos/octocat/hello-world");

record Repo(string FullName);
```

The vault must hold `(category: "github", key: "baseUrl")` and optionally `(category: "github", key: "token")`.

### LINQPad

```csharp
// NuGet → NetworkBlast
var api = NetworkBlast.NetClient.Anonymous("https://api.publicapis.org/");
var json = await api.GetStringAsync("entries");
json.Dump();
```

### PowerShell

```powershell
Add-Type -Path (Resolve-Path .\NetworkBlast.dll)

$gh = [NetworkBlast.NetClient]::WithToken("https://api.github.com/", $env:GH_TOKEN)
$body = $gh.GetStringAsync("repos/octocat/hello-world").GetAwaiter().GetResult()
$body
```

---

## 🧰 Per-Request Options

Every verb method takes an optional `RequestOptions` for query string, headers, timeout, and per-call retry budget. Use the `Options` static helpers for one-off shapes, or chain `.AddQuery(...)` / `.AddHeader(...)` to compose:

```csharp
using NetworkBlast;

// One-off query
await client.GetAsync("search", Options.Query(("q", "hello world"), ("page", "2")));

// One-off headers
await client.GetAsync("ping", Options.Headers(("X-Trace-Id", "abc-123")));

// Per-call timeout
await client.GetAsync("slow", Options.Timeout(TimeSpan.FromSeconds(5)));

// Per-call retry override (overrides the client default)
await client.GetAsync("flaky", Options.Retries(3, TimeSpan.FromMilliseconds(200)));

// Compose multiple via record `with` or chained extensions
var opts = new RequestOptions()
    .AddQuery("page", "1")
    .AddQuery("size", "50")
    .AddHeader("X-Tenant", "acme");

await client.GetAsync("widgets", opts);
```

---

## 🔁 Retry Policy

Off by default. When enabled (via `defaultRetryCount` on the constructor, `DefaultRetryCount` in `NetworkBlastOptions`, or per-call via `Options.Retries`), NetworkBlast will retry:

* 5xx server errors
* `408 Request Timeout`
* `429 Too Many Requests`
* `HttpRequestException` (network errors)

Backoff is exponential (`baseDelay * 2^attempt`) plus a small random jitter. If the response carries a `Retry-After` header (delta-seconds *or* HTTP date), that value wins over the computed backoff.

Non-transient 4xx responses (`400`, `401`, `403`, `404`, `409`, …) are returned immediately — no retry, no surprises.

---

## ⏱ Per-Request Timeout

`Options.Timeout(TimeSpan)` builds a linked `CancellationTokenSource` with `CancelAfter`, so it composes cleanly with whatever `CancellationToken` the caller passes:

```csharp
using var cts = new CancellationTokenSource();
await client.GetAsync("slow", Options.Timeout(TimeSpan.FromSeconds(5)), cts.Token);
```

If the caller cancels before the timeout fires, the caller's cancellation wins.

---

## 🧼 SOAP (1.1 / 1.2) and general XML

For legacy services that speak SOAP, NetworkBlast ships envelope helpers and a typed `XmlSerializer` round-trip. SOAP `<Fault>` responses are surfaced as a typed `SoapFault` exception so you don't have to re-parse XML to detect errors.

### Raw SOAP — paste-an-envelope style

```csharp
using NetworkBlast;
using NetworkBlast.Soap;

var soap = NetClient.WithToken("https://legacy.example.com/svc", token);

string responseInner = await soap.SendSoapAsync(
    path:    "weather.asmx",
    action:  "http://tempuri.org/GetWeather",
    bodyXml: """<GetWeather xmlns="http://tempuri.org/"><City>Paris</City></GetWeather>""",
    version: SoapVersion.V11);
```

### Typed SOAP — `XmlSerializer` round-trip

```csharp
[XmlRoot("GetWeather", Namespace = "http://tempuri.org/")]
public class GetWeatherRequest  { [XmlElement("City")] public string? City { get; set; } }

[XmlRoot("GetWeatherResponse", Namespace = "http://tempuri.org/")]
public class GetWeatherResponse { [XmlElement("Temp")] public int Temp { get; set; } }

var resp = await soap.SendSoapAsync<GetWeatherRequest, GetWeatherResponse>(
    path:    "weather.asmx",
    action:  "http://tempuri.org/GetWeather",
    payload: new GetWeatherRequest { City = "Paris" },
    version: SoapVersion.V11);

Console.WriteLine(resp!.Temp);
```

### Faults

```csharp
try
{
    var resp = await soap.SendSoapAsync<GetWeatherRequest, GetWeatherResponse>(...);
}
catch (SoapFault f)
{
    Console.Error.WriteLine($"{f.Code}: {f.Reason}");
}
```

### Envelope helper (compose by hand)

```csharp
var envelope = SoapEnvelope.Wrap(SoapVersion.V12, bodyXml,
    headerEntries: new[] { "<auth>secret</auth>" });

string innerXml = SoapEnvelope.Unwrap(envelope);
SoapFault? fault = SoapEnvelope.TryGetFault(envelope);
```

### General-purpose XML helpers

For plain REST services that speak XML (no SOAP envelope), the JSON helpers have direct mirrors:

```csharp
var thing = await client.GetXmlAsync<Thing>("things/42");
var receipt = await client.PostXmlAsync<Thing, Receipt>("things", thing);
var updated = await client.PutXmlAsync <Thing, Receipt>("things/42", thing);
```

Both use `XmlSerializer`; the request body is sent as `application/xml`.

> **Skipping by design:** WSDL → C# generation (use `dotnet-svcutil` once at build-time, then hand the generated types to `SendSoapAsync<TReq, TResp>`); WS-Security; MTOM. Add a `headerEntries` argument to the `SendSoapAsync` calls for header injection if you need WS-Addressing or a custom security header.

---

## 🧮 OData v4 (typed, with auto-paging)

NetworkBlast ships first-class support for OData v4 query options and pagination. The headline path is the typed LINQ-flavored chain on `INetClient.OData<T>(path)`:

```csharp
using NetworkBlast;
using NetworkBlast.OData;

var customers = client
    .OData<Customer>("Customers")
    .Where(c => c.Status == "Active" && c.Age > 18)
    .OrderBy(c => c.Name)
    .ThenByDescending(c => c.Id)
    .Select(c => c.Id, c => c.Name)
    .Top(50);

await foreach (var c in customers)            // IAsyncEnumerable<T>, auto-pages via @odata.nextLink
    Console.WriteLine(c.Name);

var first = await customers.FirstOrDefaultAsync();
var all   = await customers.ToListAsync();
var page  = await customers.FirstPageAsync(); // ODataPage<T> { Value, Count, NextLink, Context }
var n     = await customers.WithCount().CountAsync();

record Customer(int Id, string Status, string Name, int Age);
```

### Filter DSL — three styles, all interchangeable

```csharp
// 1. Typed LINQ predicate (translated to OData $filter)
client.OData<Customer>("Customers").Where(c => c.Status == "Active" && c.CreatedOn > date);

// 2. Static factories with operator overloads
var f = ODataFilter.Eq("Status", "Active") & ODataFilter.Gt("CreatedOn", date);
client.OData<Customer>("Customers").Where(f);

// 3. Raw string fallback
client.OData<Customer>("Customers").Where("Status eq 'Active'");
```

The LINQ translator supports `==`, `!=`, `>`, `<`, `>=`, `<=`, `&&`, `||`, `!`, nested member access (`c.Address.City` → `Address/City`), `string.Contains` / `StartsWith` / `EndsWith`, and closure-captured constants. Strings, dates, GUIDs, bools, and numbers are auto-rendered as proper OData literals (apostrophes inside strings are doubled, dates are ISO-8601, GUIDs are bare).

### Builder API (typed *or* string-based)

For ad-hoc / dynamic / hand-rolled paths, `ODataQuery` is a plain immutable record builder:

```csharp
var q = ODataQuery
    .Filter<Customer>(c => c.Status == "Active")   // typed
    .Select<Customer>(c => c.Id, c => c.Name)
    .OrderBy<Customer>(c => c.Name)
    .OrderByDescending<Customer>(c => c.Id)        // multiple OrderBy = ThenBy semantics
    .Top(50)
    .Count();

var page = await client.GetODataAsync<Customer>("Customers", q);
await foreach (var c in client.QueryODataAsync<Customer>("Customers", q)) { /* ... */ }
```

Every method has both a typed (`Expression<Func<T, object?>>`) and a string overload, so you can drop into raw OData when you need `Address/City` on a service with no entity model.

### Single-entity fetch

```csharp
var one = await client.GetODataItemAsync<Customer>("Customers(42)");
```

### Literal helpers (raw filter authoring)

```csharp
ODataLiteral.String("O'Brien")          // → 'O''Brien'
ODataLiteral.DateTimeOffset(DateTime.Now)
ODataLiteral.Guid(Guid.NewGuid())
ODataLiteral.From(any)                  // dispatches by type
```

---

## 🧩 Anatomy of a Connection

A connection is a logical name (`"github"`, `"prod-api"`, …). When the client hits the wire for the first time it asks the resolver for two values:

| Resolver call                     | Required | Used as                        |
| --------------------------------- | -------- | ------------------------------ |
| `(connectionName, "baseUrl")`     | ✅       | `HttpClient.BaseAddress`       |
| `(connectionName, "token")`       | optional | `Authorization: Bearer <value>`|

Both keys are configurable on the constructor / DI options.

For the script-friendly factories (`WithToken`, `WithApiKey`, `WithBasicAuth`, `Anonymous`) the resolver is built inline, so you never see it.

---

## 🧱 DI Wiring

```csharp
services.AddNetworkBlast(o =>
{
    o.Resolver       = Secrets.Resolver;
    o.ConnectionName = "github";

    // optional overrides:
    // o.BaseUrlKey            = "baseUrl";
    // o.TokenKey              = "token";
    // o.JsonOptions           = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    // o.DefaultRetryCount     = 3;
    // o.DefaultRetryBaseDelay = TimeSpan.FromMilliseconds(200);
});
```

`INetClient` lands in the container as a singleton.

---

## 📖 API Surface

```csharp
namespace NetworkBlast;

// resolver shape — compatible with TaskBlaster's Secrets.Resolver
public delegate Task<string> SecretResolver(string category, string key, CancellationToken ct);

public interface INetClient
{
    string ConnectionName { get; }

    Task<HttpResponseMessage> GetAsync   (string path, RequestOptions? o = null, CancellationToken ct = default);
    Task<HttpResponseMessage> PostAsync  (string path, HttpContent? body, RequestOptions? o = null, CancellationToken ct = default);
    Task<HttpResponseMessage> PutAsync   (string path, HttpContent? body, RequestOptions? o = null, CancellationToken ct = default);
    Task<HttpResponseMessage> PatchAsync (string path, HttpContent? body, RequestOptions? o = null, CancellationToken ct = default);
    Task<HttpResponseMessage> DeleteAsync(string path, RequestOptions? o = null, CancellationToken ct = default);
    Task<HttpResponseMessage> SendAsync  (HttpRequestMessage request, RequestOptions? o = null, CancellationToken ct = default);

    Task<string>      GetStringAsync (string path, RequestOptions? o = null, CancellationToken ct = default);
    Task<T?>          GetJsonAsync<T>(string path, RequestOptions? o = null, CancellationToken ct = default);
    Task<TResponse?>  PostJsonAsync<TResponse> (string path, object? payload, RequestOptions? o = null, CancellationToken ct = default);
    Task<TResponse?>  PutJsonAsync<TResponse>  (string path, object? payload, RequestOptions? o = null, CancellationToken ct = default);
    Task<TResponse?>  PatchJsonAsync<TResponse>(string path, object? payload, RequestOptions? o = null, CancellationToken ct = default);
}

public sealed class NetClient : INetClient
{
    public NetClient(
        SecretResolver resolver,
        string connectionName,
        HttpClient? httpClient = null,
        string baseUrlKey = "baseUrl",
        string tokenKey   = "token",
        JsonSerializerOptions? jsonOptions = null,
        int defaultRetryCount = 0,
        TimeSpan? defaultRetryBaseDelay = null);

    // script-friendly factories (no resolver needed)
    public static NetClient Anonymous   (string baseUrl, HttpClient? http = null, JsonSerializerOptions? json = null);
    public static NetClient WithToken   (string baseUrl, string token,    HttpClient? http = null, JsonSerializerOptions? json = null);
    public static NetClient WithApiKey  (string baseUrl, string headerName, string apiKey, HttpClient? http = null, JsonSerializerOptions? json = null);
    public static NetClient WithBasicAuth(string baseUrl, string username, string password, HttpClient? http = null, JsonSerializerOptions? json = null);

    // chainable defaults
    public NetClient WithDefaultHeader(string name, string value);
    public NetClient WithDefaultQuery (string name, string value);

    // additional auth factories
    public static NetClient WithApiKeyQuery       (string baseUrl, string paramName, string apiKey, HttpClient? http = null, JsonSerializerOptions? json = null);
    public static NetClient WithWindowsAuth       (string baseUrl, JsonSerializerOptions? json = null);
    public static NetClient WithWindowsAuth       (string baseUrl, NetworkCredential credentials, JsonSerializerOptions? json = null);
    public static NetClient WithCookieJar         (string baseUrl, CookieContainer? cookies = null, JsonSerializerOptions? json = null);
    public static NetClient WithOAuth2ClientCredentials(
        string baseUrl, string tokenEndpoint, string clientId, string clientSecret,
        string? scope = null, HttpClient? tokenClient = null, JsonSerializerOptions? json = null);
    public static NetClient WithOAuth2(string baseUrl, OAuth2TokenProvider provider, JsonSerializerOptions? json = null);
}

// ---------- OAuth2 building blocks ----------

namespace NetworkBlast.Auth;

public sealed class OAuth2TokenProvider
{
    public OAuth2TokenProvider(
        Uri tokenEndpoint, string clientId, string clientSecret,
        string? scope = null, HttpClient? httpClient = null);

    public Task<string> GetAccessTokenAsync(CancellationToken ct = default);
    public Task         InvalidateAsync();
}

// ---------- SOAP ----------

namespace NetworkBlast.Soap;

public enum SoapVersion { V11, V12 }

public sealed class SoapFault : Exception
{
    public SoapVersion Version { get; }
    public string Code   { get; }
    public string Reason { get; }
    public string? Detail { get; }
}

public static class SoapEnvelope
{
    public const string Soap11Namespace   = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string Soap12Namespace   = "http://www.w3.org/2003/05/soap-envelope";
    public const string Soap11ContentType = "text/xml";
    public const string Soap12ContentType = "application/soap+xml";

    public static string      Wrap        (SoapVersion v, string body, IEnumerable<string>? headerEntries = null);
    public static string      Unwrap      (string envelopeXml);
    public static SoapFault?  TryGetFault (string envelopeXml);
    public static string      NamespaceFor(SoapVersion v);
}

public static class SoapExtensions
{
    public static Task<string> SendSoapAsync(
        this INetClient client, string path, string action, string bodyXml,
        SoapVersion version = SoapVersion.V11,
        IEnumerable<string>? headerEntries = null,
        RequestOptions? options = null,
        CancellationToken ct = default);

    public static Task<TResponse?> SendSoapAsync<TRequest, TResponse>(
        this INetClient client, string path, string action, TRequest payload,
        SoapVersion version = SoapVersion.V11,
        XmlSerializerNamespaces? namespaces = null,
        IEnumerable<string>? headerEntries = null,
        RequestOptions? options = null,
        CancellationToken ct = default);
}

// ---------- general XML helpers ----------

namespace NetworkBlast;

public static class XmlExtensions
{
    public static Task<T?>          GetXmlAsync<T>        (this INetClient client, string path, RequestOptions? o = null, CancellationToken ct = default);
    public static Task<TResponse?>  PostXmlAsync<TRequest, TResponse>(this INetClient client, string path, TRequest payload, RequestOptions? o = null, CancellationToken ct = default);
    public static Task<TResponse?>  PutXmlAsync<TRequest, TResponse> (this INetClient client, string path, TRequest payload, RequestOptions? o = null, CancellationToken ct = default);
}

public sealed record RequestOptions
{
    public IReadOnlyDictionary<string, string?>? Query    { get; init; }
    public IReadOnlyDictionary<string, string?>? Headers  { get; init; }
    public TimeSpan? Timeout         { get; init; }
    public int?      RetryCount      { get; init; }
    public TimeSpan? RetryBaseDelay  { get; init; }
}

// shorthand factories
public static class Options
{
    public static RequestOptions Query   (params (string key, string? value)[] pairs);
    public static RequestOptions Headers (params (string name, string? value)[] pairs);
    public static RequestOptions Timeout (TimeSpan timeout);
    public static RequestOptions Retries (int count, TimeSpan? baseDelay = null);
}

// chainable composition
public static class RequestOptionsExtensions
{
    public static RequestOptions AddQuery (this RequestOptions o, string name, string? value);
    public static RequestOptions AddHeader(this RequestOptions o, string name, string? value);
}

// ---------- OData v4 ----------

namespace NetworkBlast.OData;

public sealed record ODataPage<T>
{
    public IReadOnlyList<T> Value     { get; init; }
    public long?            Count     { get; init; }
    public string?          NextLink  { get; init; }
    public string?          Context   { get; init; }
}

public abstract record ODataFilter
{
    public abstract string Render();

    // comparison
    public static ODataFilter Eq(string field, object? value);
    public static ODataFilter Ne(string field, object? value);
    public static ODataFilter Gt(string field, object? value);
    public static ODataFilter Lt(string field, object? value);
    public static ODataFilter Ge(string field, object? value);
    public static ODataFilter Le(string field, object? value);

    // string functions
    public static ODataFilter Contains  (string field, string value);
    public static ODataFilter StartsWith(string field, string value);
    public static ODataFilter EndsWith  (string field, string value);

    // collection / logical
    public static ODataFilter In (string field, params object?[] values);
    public static ODataFilter And(ODataFilter left, ODataFilter right);
    public static ODataFilter Or (ODataFilter left, ODataFilter right);
    public static ODataFilter Not(ODataFilter inner);
    public static ODataFilter Raw(string expression);

    // LINQ entry-point
    public static ODataFilter For<T>(Expression<Func<T, bool>> predicate);

    // operator overloads: &  |  !
}

public sealed record ODataQuery
{
    public static ODataQuery Empty { get; }

    public static ODataQuery Filter (string expression);
    public static ODataQuery Filter (ODataFilter filter);
    public static ODataQuery Filter<T>(Expression<Func<T, bool>> predicate);

    public ODataQuery WithFilter(string expression);
    public ODataQuery WithFilter(ODataFilter filter);
    public ODataQuery WithFilter<T>(Expression<Func<T, bool>> predicate);

    public ODataQuery Select   (params string[] fields);
    public ODataQuery Select<T>(params Expression<Func<T, object?>>[] fields);
    public ODataQuery Expand   (params string[] navigations);
    public ODataQuery Expand<T>(params Expression<Func<T, object?>>[] navigations);

    public ODataQuery OrderBy           (string field, bool descending = false);
    public ODataQuery OrderBy<T>        (Expression<Func<T, object?>> selector, bool descending = false);
    public ODataQuery OrderByDescending (string field);
    public ODataQuery OrderByDescending<T>(Expression<Func<T, object?>> selector);

    public ODataQuery Top    (int count);
    public ODataQuery Skip   (int count);
    public ODataQuery Search (string text);
    public ODataQuery Count  (bool include = true);

    public IReadOnlyDictionary<string, string?> Build();
    public RequestOptions ToRequestOptions();
}

public sealed class ODataRequest<T> : IAsyncEnumerable<T>
{
    public ODataRequest<T> Where(Expression<Func<T, bool>> predicate);
    public ODataRequest<T> Where(ODataFilter filter);
    public ODataRequest<T> Where(string rawExpression);

    public ODataRequest<T> OrderBy           (Expression<Func<T, object?>> selector);
    public ODataRequest<T> OrderByDescending (Expression<Func<T, object?>> selector);
    public ODataRequest<T> ThenBy            (Expression<Func<T, object?>> selector);
    public ODataRequest<T> ThenByDescending  (Expression<Func<T, object?>> selector);

    public ODataRequest<T> Select (params Expression<Func<T, object?>>[] fields);
    public ODataRequest<T> Expand (params Expression<Func<T, object?>>[] navigations);

    public ODataRequest<T> Top       (int count);
    public ODataRequest<T> Skip      (int count);
    public ODataRequest<T> Search    (string text);
    public ODataRequest<T> WithCount (bool include = true);

    // terminals
    public Task<ODataPage<T>>    FirstPageAsync       (CancellationToken ct = default);
    public Task<IReadOnlyList<T>> ToListAsync          (CancellationToken ct = default);
    public Task<T?>              FirstOrDefaultAsync  (CancellationToken ct = default);
    public Task<T?>              SingleOrDefaultAsync (CancellationToken ct = default);
    public Task<long>            CountAsync           (CancellationToken ct = default);
}

public static class ODataExtensions
{
    public static ODataRequest<T>     OData<T>            (this INetClient client, string path);
    public static Task<ODataPage<T>>  GetODataAsync<T>    (this INetClient client, string path, ODataQuery? query = null, RequestOptions? options = null, CancellationToken ct = default);
    public static Task<T?>            GetODataItemAsync<T>(this INetClient client, string path, ODataQuery? query = null, RequestOptions? options = null, CancellationToken ct = default);
    public static IAsyncEnumerable<T> QueryODataAsync<T>  (this INetClient client, string path, ODataQuery? query = null, RequestOptions? options = null, CancellationToken ct = default);
}

public static class ODataLiteral
{
    public static string String         (string value);
    public static string DateTimeOffset (DateTimeOffset value);
    public static string DateTime       (DateTime value);
    public static string Date           (DateOnly value);
    public static string Time           (TimeOnly value);
    public static string Guid           (Guid value);
    public static string Bool           (bool value);
    public static string Number         (IFormattable value);
    public static string From           (object? value);
}
```

---

## 🤖 AI assistants

This assembly carries the **Blast.PrimaryFacade** convention: an
`[AssemblyMetadata("Blast.PrimaryFacade", "...")]` attribute names the
canonical front-door type(s) of the package, so AI helpers (e.g.
TaskBlaster's script assistant) can identify the entry points without
scanning every public type.

For NetworkBlast the front door is:

| Type | Purpose |
|------|---------|
| `NetworkBlast.NetClient` | REST / HTTP / SOAP / OData client; resolver-aware via `Func<category, key, ct, Task<string>>`. |

Read it back from a loaded assembly via reflection:

```csharp
var facade = typeof(NetworkBlast.NetClient).Assembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .FirstOrDefault(a => a.Key == "Blast.PrimaryFacade")?.Value;
```

The value is a hint for tooling; consumers don't need to read it.

---

## 📜 License

[MIT](https://opensource.org/licenses/MIT)
