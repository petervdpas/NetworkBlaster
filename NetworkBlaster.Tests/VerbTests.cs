using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests;

public class VerbTests
{
    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    [Fact]
    public async Task PutAsync_SendsPutVerb_WithBody()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.NoContent);
        var client = BuildClient(handler);

        await client.PutAsync("widgets/1", new StringContent("{\"name\":\"x\"}"));

        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
        Assert.Equal("{\"name\":\"x\"}", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task PatchAsync_SendsPatchVerb_WithBody()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        await client.PatchAsync("widgets/1", new StringContent("{\"name\":\"y\"}"));

        Assert.Equal(HttpMethod.Patch, handler.LastRequest.Method);
        Assert.Equal("{\"name\":\"y\"}", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteVerb_WithoutBody()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.NoContent);
        var client = BuildClient(handler);

        await client.DeleteAsync("widgets/1");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
        Assert.Null(handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task PutJsonAsync_SerializesPayload_AndDeserializesResponse()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"id\":7,\"name\":\"updated\"}");
        var client = BuildClient(handler);

        var result = await client.PutJsonAsync<Widget>("widgets/7", new Widget(7, "updated"));

        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
        Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType?.MediaType);
        Assert.NotNull(result);
        Assert.Equal(7, result!.Id);
        Assert.Equal("updated", result.Name);
    }

    [Fact]
    public async Task PatchJsonAsync_RoundTripsThroughWebDefaults()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"id\":3,\"name\":\"patched\"}");
        var client = BuildClient(handler);

        var result = await client.PatchJsonAsync<Widget>("widgets/3", new { name = "patched" });

        Assert.NotNull(result);
        Assert.Equal(3, result!.Id);
        Assert.Equal("patched", result.Name);
    }

    [Fact]
    public async Task DeleteAsync_RespectsRequestOptions_HeadersAndQuery()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.NoContent);
        var client = BuildClient(handler);

        await client.DeleteAsync("widgets/1",
            new RequestOptions()
                .AddQuery("force", "true")
                .AddHeader("X-Reason", "cleanup"));

        Assert.Contains("force=true", handler.LastRequest.RequestUri!.Query);
        Assert.Equal("cleanup", string.Join(",", handler.LastRequest.Headers.GetValues("X-Reason")));
    }

    private sealed record Widget(int Id, string Name);
}
