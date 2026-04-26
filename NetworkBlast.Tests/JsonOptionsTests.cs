using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NetworkBlast;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests;

public class JsonOptionsTests
{
    [Fact]
    public async Task DefaultJsonOptions_AreCaseInsensitiveOnDeserialize()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"FullName\":\"octocat/Hello-World\"}");
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler));

        var repo = await client.GetJsonAsync<Repo>("any");

        Assert.NotNull(repo);
        Assert.Equal("octocat/Hello-World", repo!.FullName);
    }

    [Fact]
    public async Task DefaultJsonOptions_SerializeAsCamelCase()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{}");
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler));

        await client.PostJsonAsync<object>("submit", new Repo("octocat/Hello-World"));

        Assert.Equal("{\"fullName\":\"octocat/Hello-World\"}", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task CustomJsonOptions_AreHonored_ForSerializeAndDeserialize()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"FullName\":\"x\"}");
        var custom = new JsonSerializerOptions { PropertyNamingPolicy = null }; // PascalCase

        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler), custom);

        var repo = await client.PostJsonAsync<Repo>("submit", new Repo("x"));

        Assert.Equal("{\"FullName\":\"x\"}", handler.RequestBodies[^1]);
        Assert.NotNull(repo);
        Assert.Equal("x", repo!.FullName);
    }

    private sealed record Repo(string FullName);
}
