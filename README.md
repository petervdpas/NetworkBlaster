# NetworkBlaster 🌐

[![NuGet](https://img.shields.io/nuget/v/NetworkBlaster.svg)](https://www.nuget.org/packages/NetworkBlaster)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetworkBlaster.svg)](https://www.nuget.org/packages/NetworkBlaster)
[![License](https://img.shields.io/github/license/petervdpas/NetworkBlaster.svg)](https://opensource.org/licenses/MIT)

![NetworkBlaster](https://raw.githubusercontent.com/petervdpas/NetworkBlaster/master/assets/icon.png)

**NetworkBlaster** is a programmable HTTP/REST client for .NET — a sibling to
[AzureBlast](https://www.nuget.org/packages/AzureBlast) in the Blast family.
Think "Postman as a library": named connections, lazy auth, script-friendly defaults.

> ⚠️ **Status:** 0.1 scaffold. API surface is exploratory and will move.

---

## ✨ Why NetworkBlaster

* 🔹 **Script-first** — works from `.csx`, LINQPad and PowerShell with one line of setup.
* 🔹 **Vault-agnostic** — secrets are pulled through a `Func<category, key, ct, Task<string>>` delegate, *not* a hard reference to SecretBlast or anything else. Wire any resolver you like.
* 🔹 **No Blast-on-Blast deps** — the only NuGet reference is `Microsoft.Extensions.DependencyInjection.Abstractions`.
* 🔹 **Lazy hydration** — base URL and auth are resolved on first request, then cached.
* 🔹 **JSON helpers in the box** — `GetStringAsync` / `GetJsonAsync<T>` / `PostJsonAsync<T>` for the 90% case.

---

## 📦 Installation

```bash
dotnet add package NetworkBlaster
```

---

## 🚀 Script Quick-Start

### `.csx` (one-off, no vault)

```csharp
#r "nuget: NetworkBlaster, 0.1.0"
using NetworkBlaster;

var gh = NetClient.WithToken("https://api.github.com/", "ghp_xxx");
var body = await gh.GetStringAsync("repos/octocat/hello-world");
Console.WriteLine(body);
```

### `.csx` inside TaskBlaster (resolver from `Secrets`)

```csharp
#r "nuget: NetworkBlaster, 0.1.0"
using NetworkBlaster;

// Secrets.Resolver is the standard Func<category, key, ct, Task<string>> delegate
// exposed by TaskBlaster scripts. NetworkBlaster never sees SecretBlast directly.
var gh = new NetClient(Secrets.Resolver, "github");
var repo = await gh.GetJsonAsync<Repo>("repos/octocat/hello-world");
Console.WriteLine(repo?.FullName);

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

## 🧩 Anatomy of a Connection

A connection is a logical name (`"github"`, `"prod-api"`, …). When the client hits the wire for the first time it asks the resolver for two values:

| Resolver call                     | Required | Used as                        |
| --------------------------------- | -------- | ------------------------------ |
| `(connectionName, "baseUrl")`     | ✅       | `HttpClient.BaseAddress`       |
| `(connectionName, "token")`       | optional | `Authorization: Bearer <value>`|

Both keys are configurable on the constructor / DI options.

---

## 🧰 DI Wiring

```csharp
services.AddNetworkBlaster(o =>
{
    o.Resolver       = Secrets.Resolver;
    o.ConnectionName = "github";
    // o.BaseUrlKey  = "baseUrl";   // override if your vault uses a different field
    // o.TokenKey    = "token";
});
```

`INetClient` lands in the container as a singleton.

---

## 📜 License

[MIT](https://opensource.org/licenses/MIT)
