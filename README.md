# NetworkBlaster 🌐

[![NuGet](https://img.shields.io/nuget/v/NetworkBlaster.svg)](https://www.nuget.org/packages/NetworkBlaster)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetworkBlaster.svg)](https://www.nuget.org/packages/NetworkBlaster)
[![License](https://img.shields.io/github/license/petervdpas/NetworkBlaster.svg)](https://opensource.org/licenses/MIT)

![NetworkBlaster](https://raw.githubusercontent.com/petervdpas/NetworkBlaster/master/assets/icon.png)

**NetworkBlaster** is a programmable HTTP/REST client for .NET — a sibling to
[AzureBlast](https://www.nuget.org/packages/AzureBlast) in the Blast family.
Think "Postman as a library": named connections, lazy auth, script-friendly defaults.

---

> ✅ **Status:** 0.2 — verbs (GET/POST/PUT/PATCH/DELETE + JSON twins), per-request
> headers / query / timeout, retry policy, configurable JSON, four script-friendly
> auth factories. 44 tests green.

---

## ✨ Why NetworkBlaster

* 🔹 **Script-first** — works from `.csx`, LINQPad and PowerShell with one line of setup.
* 🔹 **Vault-agnostic** — secrets are pulled through a `Func<category, key, ct, Task<string>>` delegate, *not* a hard reference to SecretBlast or anything else. Wire any resolver you like.
* 🔹 **No Blast-on-Blast deps** — the only NuGet reference is `Microsoft.Extensions.DependencyInjection.Abstractions`.
* 🔹 **Lazy hydration** — base URL and auth are resolved on first request, then cached.
* 🔹 **Built-in resilience** — opt-in retry on 5xx / 408 / 429 / network errors, with `Retry-After` honored, plus per-request timeout.
* 🔹 **JSON helpers in the box** — `GetJsonAsync<T>` / `PostJsonAsync<T>` / `PutJsonAsync<T>` / `PatchJsonAsync<T>`, with configurable `JsonSerializerOptions`.

---

## 📦 Installation

```bash
dotnet add package NetworkBlaster
```

---

## 🚀 Script Quick-Start

### `.csx` — bearer token, no vault

```csharp
#r "nuget: NetworkBlaster, 0.2.0"
using NetworkBlaster;

var gh = NetClient.WithToken("https://api.github.com/", "ghp_xxx");
var body = await gh.GetStringAsync("repos/octocat/hello-world");
Console.WriteLine(body);
```

### `.csx` — API-key header

```csharp
#r "nuget: NetworkBlaster, 0.2.0"
using NetworkBlaster;

var api = NetClient.WithApiKey("https://api.example.com/", "X-API-Key", "secret");
var data = await api.GetJsonAsync<Thing>("things/42");

record Thing(int Id, string Name);
```

### `.csx` — Basic auth

```csharp
var jira = NetClient.WithBasicAuth("https://acme.atlassian.net/", "alice@acme.com", apiToken);
var issue = await jira.GetJsonAsync<JiraIssue>("rest/api/3/issue/PROJ-123");
```

### `.csx` inside TaskBlaster — resolver from `Secrets`

```csharp
#r "nuget: NetworkBlaster, 0.2.0"
using NetworkBlaster;

var gh = new NetClient(Secrets.Resolver, "github");
var repo = await gh.GetJsonAsync<Repo>("repos/octocat/hello-world");

record Repo(string FullName);
```

The vault must hold `(category: "github", key: "baseUrl")` and optionally `(category: "github", key: "token")`.

### LINQPad

```csharp
// NuGet → NetworkBlaster
var api = NetworkBlaster.NetClient.Anonymous("https://api.publicapis.org/");
var json = await api.GetStringAsync("entries");
json.Dump();
```

### PowerShell

```powershell
Add-Type -Path (Resolve-Path .\NetworkBlaster.dll)

$gh = [NetworkBlaster.NetClient]::WithToken("https://api.github.com/", $env:GH_TOKEN)
$body = $gh.GetStringAsync("repos/octocat/hello-world").GetAwaiter().GetResult()
$body
```

---

## 🧰 Per-Request Options

Every verb method takes an optional `RequestOptions` for query string, headers, timeout, and per-call retry budget. Use the `Options` static helpers for one-off shapes, or chain `.AddQuery(...)` / `.AddHeader(...)` to compose:

```csharp
using NetworkBlaster;

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

Off by default. When enabled (via `defaultRetryCount` on the constructor, `DefaultRetryCount` in `NetworkBlasterOptions`, or per-call via `Options.Retries`), NetworkBlaster will retry:

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
services.AddNetworkBlaster(o =>
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
namespace NetworkBlaster;

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
```

---

## 📜 License

[MIT](https://opensource.org/licenses/MIT)
