# NetworkBlaster 🌐

[![NuGet](https://img.shields.io/nuget/v/NetworkBlaster.svg)](https://www.nuget.org/packages/NetworkBlaster)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetworkBlaster.svg)](https://www.nuget.org/packages/NetworkBlaster)
[![License](https://img.shields.io/github/license/petervdpas/NetworkBlaster.svg)](https://opensource.org/licenses/MIT)

![NetworkBlaster](https://raw.githubusercontent.com/petervdpas/NetworkBlaster/master/assets/icon.png)

**NetworkBlaster** is a programmable HTTP/REST client for .NET — a sibling to
[AzureBlast](https://www.nuget.org/packages/AzureBlast) in the Blast family.
Think "Postman as a library": named connections, lazy auth, script-friendly defaults.

---

> ✅ **Status:** 0.3 — adds **typed OData v4** support on top of v0.2: LINQ-flavored
> chainable queries, typed filter DSL, auto-paging `IAsyncEnumerable<T>`, and a
> string/expression-friendly query builder. 177 tests green.

---

## ✨ Why NetworkBlaster

* 🔹 **Script-first** — works from `.csx`, LINQPad and PowerShell with one line of setup.
* 🔹 **Vault-agnostic** — secrets are pulled through a `Func<category, key, ct, Task<string>>` delegate, *not* a hard reference to SecretBlast or anything else. Wire any resolver you like.
* 🔹 **No Blast-on-Blast deps** — the only NuGet reference is `Microsoft.Extensions.DependencyInjection.Abstractions`.
* 🔹 **Lazy hydration** — base URL and auth are resolved on first request, then cached.
* 🔹 **Built-in resilience** — opt-in retry on 5xx / 408 / 429 / network errors, with `Retry-After` honored, plus per-request timeout.
* 🔹 **JSON helpers in the box** — `GetJsonAsync<T>` / `PostJsonAsync<T>` / `PutJsonAsync<T>` / `PatchJsonAsync<T>`, with configurable `JsonSerializerOptions`.
* 🔹 **Typed OData v4** — LINQ-flavored chain (`.Where(c => c.Status == "Active")`), auto-paging `IAsyncEnumerable<T>`, and a vault-agnostic filter DSL with operator overloads.

---

## 📦 Installation

```bash
dotnet add package NetworkBlaster
```

---

## 🚀 Script Quick-Start

### `.csx` — bearer token, no vault

```csharp
#r "nuget: NetworkBlaster, 0.3.0"
using NetworkBlaster;

var gh = NetClient.WithToken("https://api.github.com/", "ghp_xxx");
var body = await gh.GetStringAsync("repos/octocat/hello-world");
Console.WriteLine(body);
```

### `.csx` — API-key header

```csharp
#r "nuget: NetworkBlaster, 0.3.0"
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
#r "nuget: NetworkBlaster, 0.3.0"
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

## 🧮 OData v4 (typed, with auto-paging)

NetworkBlaster ships first-class support for OData v4 query options and pagination. The headline path is the typed LINQ-flavored chain on `INetClient.OData<T>(path)`:

```csharp
using NetworkBlaster;
using NetworkBlaster.OData;

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

// ---------- OData v4 ----------

namespace NetworkBlaster.OData;

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

## 📜 License

[MIT](https://opensource.org/licenses/MIT)
