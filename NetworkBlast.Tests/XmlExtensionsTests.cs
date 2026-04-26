using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Serialization;
using NetworkBlast;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests;

public class XmlExtensionsTests
{
    [XmlRoot("Thing")]
    public class Thing
    {
        [XmlElement("Id")]   public int     Id   { get; set; }
        [XmlElement("Name")] public string? Name { get; set; }
    }

    [XmlRoot("Receipt")]
    public class Receipt
    {
        [XmlElement("Status")] public string? Status { get; set; }
    }

    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    [Fact]
    public async Task GetXmlAsync_DeserializesViaXmlSerializer()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "<?xml version=\"1.0\"?><Thing><Id>42</Id><Name>answer</Name></Thing>",
            mediaType: "application/xml");
        var client = BuildClient(handler);

        var thing = await client.GetXmlAsync<Thing>("things/42");

        Assert.NotNull(thing);
        Assert.Equal(42, thing!.Id);
        Assert.Equal("answer", thing.Name);
    }

    [Fact]
    public async Task GetXmlAsync_EmptyBody_ReturnsDefault()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "", mediaType: "application/xml");
        var client = BuildClient(handler);

        var thing = await client.GetXmlAsync<Thing>("things/42");

        Assert.Null(thing);
    }

    [Fact]
    public async Task GetXmlAsync_NonSuccess_Throws()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.InternalServerError, "", mediaType: "application/xml");
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetXmlAsync<Thing>("things/42"));
    }

    [Fact]
    public async Task PostXmlAsync_SendsApplicationXml_AndDeserializesResponse()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "<?xml version=\"1.0\"?><Receipt><Status>ok</Status></Receipt>",
            mediaType: "application/xml");
        var client = BuildClient(handler);

        var receipt = await client.PostXmlAsync<Thing, Receipt>("things",
            new Thing { Id = 1, Name = "first" });

        Assert.NotNull(receipt);
        Assert.Equal("ok", receipt!.Status);
        Assert.Equal("application/xml", handler.LastRequest.Content!.Headers.ContentType?.MediaType);
        Assert.Contains("<Id>1</Id>", handler.RequestBodies[^1]);
        Assert.Contains("<Name>first</Name>", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task PutXmlAsync_SendsPutVerb_WithApplicationXml()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "<Receipt><Status>updated</Status></Receipt>",
            mediaType: "application/xml");
        var client = BuildClient(handler);

        var receipt = await client.PutXmlAsync<Thing, Receipt>("things/1", new Thing { Id = 1, Name = "x" });

        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
        Assert.Equal("updated", receipt!.Status);
    }

    [Fact]
    public async Task PostXmlAsync_PreservesRequestOptions_HeadersAndQuery()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "<Receipt><Status>ok</Status></Receipt>",
            mediaType: "application/xml");
        var client = BuildClient(handler);

        await client.PostXmlAsync<Thing, Receipt>("things",
            new Thing { Id = 1, Name = "x" },
            new RequestOptions().AddHeader("X-Trace-Id", "abc").AddQuery("region", "eu"));

        Assert.Equal("abc", string.Join(",", handler.LastRequest.Headers.GetValues("X-Trace-Id")));
        Assert.Contains("region=eu", handler.LastRequest.RequestUri!.Query);
    }
}
