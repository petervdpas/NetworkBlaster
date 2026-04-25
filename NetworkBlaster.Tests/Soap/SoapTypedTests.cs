using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Serialization;
using NetworkBlaster;
using NetworkBlaster.Soap;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests.Soap;

public class SoapTypedTests
{
    [XmlRoot("GetWeather", Namespace = "http://tempuri.org/")]
    public class GetWeatherRequest
    {
        [XmlElement("City")] public string? City { get; set; }
    }

    [XmlRoot("GetWeatherResponse", Namespace = "http://tempuri.org/")]
    public class GetWeatherResponse
    {
        [XmlElement("Temp")] public int Temp { get; set; }
    }

    private static string SuccessEnvelope11(string innerXml) =>
        $"<soap:Envelope xmlns:soap=\"{SoapEnvelope.Soap11Namespace}\"><soap:Body>{innerXml}</soap:Body></soap:Envelope>";

    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    [Fact]
    public async Task TypedSendSoapAsync_RoundTripsThroughXmlSerializer()
    {
        var responseInner = "<GetWeatherResponse xmlns=\"http://tempuri.org/\"><Temp>7</Temp></GetWeatherResponse>";
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, SuccessEnvelope11(responseInner));
        var client = BuildClient(handler);

        var result = await client.SendSoapAsync<GetWeatherRequest, GetWeatherResponse>(
            "svc", "http://tempuri.org/GetWeather", new GetWeatherRequest { City = "Paris" }, SoapVersion.V11);

        Assert.NotNull(result);
        Assert.Equal(7, result!.Temp);

        var sentBody = handler.RequestBodies[^1]!;
        Assert.Contains("<GetWeather xmlns=\"http://tempuri.org/\">", sentBody);
        Assert.Contains("<City>Paris</City>", sentBody);
    }

    [Fact]
    public async Task TypedSendSoapAsync_FaultResponse_ThrowsSoapFault()
    {
        var faultBody = $"""
            <soap:Envelope xmlns:soap="{SoapEnvelope.Soap11Namespace}">
              <soap:Body>
                <soap:Fault><faultcode>soap:Client</faultcode><faultstring>nope</faultstring></soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """;
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.InternalServerError, faultBody);
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<SoapFault>(() =>
            client.SendSoapAsync<GetWeatherRequest, GetWeatherResponse>(
                "svc", "act", new GetWeatherRequest { City = "x" }, SoapVersion.V11));
    }

    [Fact]
    public async Task TypedSendSoapAsync_RequestXml_OmitsXmlDeclaration()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            SuccessEnvelope11("<GetWeatherResponse xmlns=\"http://tempuri.org/\"><Temp>0</Temp></GetWeatherResponse>"));
        var client = BuildClient(handler);

        await client.SendSoapAsync<GetWeatherRequest, GetWeatherResponse>(
            "svc", "act", new GetWeatherRequest { City = "X" }, SoapVersion.V11);

        var bodyTagStart = handler.RequestBodies[^1]!.IndexOf("<soap:Body>", System.StringComparison.Ordinal);
        var afterBody = handler.RequestBodies[^1]![(bodyTagStart + "<soap:Body>".Length)..];
        Assert.DoesNotContain("<?xml", afterBody);
    }

    [Fact]
    public async Task TypedSendSoapAsync_DoesNotEmitXsiXsdAttributes_ByDefault()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            SuccessEnvelope11("<GetWeatherResponse xmlns=\"http://tempuri.org/\"><Temp>0</Temp></GetWeatherResponse>"));
        var client = BuildClient(handler);

        await client.SendSoapAsync<GetWeatherRequest, GetWeatherResponse>(
            "svc", "act", new GetWeatherRequest { City = "X" }, SoapVersion.V11);

        var sent = handler.RequestBodies[^1]!;
        Assert.DoesNotContain("xmlns:xsi", sent);
        Assert.DoesNotContain("xmlns:xsd", sent);
    }
}
