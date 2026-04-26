using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlast;
using NetworkBlast.Soap;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests.Soap;

public class SoapExtensionsTests
{
    private const string TempuriBody = "<GetWeather xmlns=\"http://tempuri.org/\"><City>Paris</City></GetWeather>";

    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    private static string SuccessEnvelope11(string innerXml) =>
        $"<soap:Envelope xmlns:soap=\"{SoapEnvelope.Soap11Namespace}\"><soap:Body>{innerXml}</soap:Body></soap:Envelope>";

    [Fact]
    public async Task SendSoapAsync_V11_PostsTextXml_WithSoapActionHeader()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            SuccessEnvelope11("<Ok/>"), mediaType: SoapEnvelope.Soap11ContentType);
        var client = BuildClient(handler);

        await client.SendSoapAsync("svc", "http://tempuri.org/GetWeather", TempuriBody, SoapVersion.V11);

        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal(SoapEnvelope.Soap11ContentType, handler.LastRequest.Content!.Headers.ContentType?.MediaType);
        var soapAction = string.Join(",", handler.LastRequest.Headers.GetValues("SOAPAction"));
        Assert.Equal("\"http://tempuri.org/GetWeather\"", soapAction);
    }

    [Fact]
    public async Task SendSoapAsync_V11_BodyIsAValidEnvelopeWrappingThePayload()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            SuccessEnvelope11("<Ok/>"), mediaType: SoapEnvelope.Soap11ContentType);
        var client = BuildClient(handler);

        await client.SendSoapAsync("svc", "act", TempuriBody, SoapVersion.V11);

        var sent = handler.RequestBodies[^1]!;
        Assert.Contains(SoapEnvelope.Soap11Namespace, sent);
        Assert.Contains("<soap:Body>", sent);
        Assert.Contains("<City>Paris</City>", sent);
    }

    [Fact]
    public async Task SendSoapAsync_V12_PostsApplicationSoapXml_WithActionParameter_NoSoapActionHeader()
    {
        var responseEnv = $"<soap:Envelope xmlns:soap=\"{SoapEnvelope.Soap12Namespace}\"><soap:Body><Ok/></soap:Body></soap:Envelope>";
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, responseEnv, mediaType: SoapEnvelope.Soap12ContentType);
        var client = BuildClient(handler);

        await client.SendSoapAsync("svc", "http://tempuri.org/X", "<X xmlns=\"http://tempuri.org/\"/>", SoapVersion.V12);

        var contentType = handler.LastRequest.Content!.Headers.ContentType!;
        Assert.Equal(SoapEnvelope.Soap12ContentType, contentType.MediaType);
        Assert.Contains(contentType.Parameters, p =>
            p.Name == "action" && p.Value == "\"http://tempuri.org/X\"");
        Assert.False(handler.LastRequest.Headers.Contains("SOAPAction"));
    }

    [Fact]
    public async Task SendSoapAsync_Returns_InnerBodyXml_OnSuccess()
    {
        var inner = "<GetWeatherResponse xmlns=\"http://tempuri.org/\"><Temp>9</Temp></GetWeatherResponse>";
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, SuccessEnvelope11(inner));
        var client = BuildClient(handler);

        var result = await client.SendSoapAsync("svc", "act", TempuriBody, SoapVersion.V11);

        Assert.Contains("<Temp>9</Temp>", result);
    }

    [Fact]
    public async Task SendSoapAsync_FaultResponse_ThrowsSoapFault_WithParsedFields()
    {
        var faultBody = $"""
            <soap:Envelope xmlns:soap="{SoapEnvelope.Soap11Namespace}">
              <soap:Body>
                <soap:Fault>
                  <faultcode>soap:Client</faultcode>
                  <faultstring>City not found</faultstring>
                </soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """;
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.InternalServerError, faultBody);
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<SoapFault>(() =>
            client.SendSoapAsync("svc", "act", TempuriBody, SoapVersion.V11));
        Assert.Equal("soap:Client", ex.Code);
        Assert.Equal("City not found", ex.Reason);
    }

    [Fact]
    public async Task SendSoapAsync_NonSuccessNoFault_ThrowsHttpRequestException()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.InternalServerError,
            SuccessEnvelope11("<Whoops/>"));
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SendSoapAsync("svc", "act", TempuriBody, SoapVersion.V11));
    }

    [Fact]
    public async Task SendSoapAsync_HeaderEntries_AppearInEnvelopeHeader()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, SuccessEnvelope11("<Ok/>"));
        var client = BuildClient(handler);

        await client.SendSoapAsync(
            "svc", "act", TempuriBody, SoapVersion.V11,
            headerEntries: new[] { "<auth>secret</auth>" });

        var sent = handler.RequestBodies[^1]!;
        Assert.Contains("<soap:Header><auth>secret</auth></soap:Header>", sent);
    }

    [Fact]
    public async Task SendSoapAsync_RespectsCallerRequestOptions_PreservesSoapActionHeader()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, SuccessEnvelope11("<Ok/>"));
        var client = BuildClient(handler);

        await client.SendSoapAsync(
            "svc", "act", TempuriBody, SoapVersion.V11,
            options: Options.Headers(("X-Trace-Id", "abc")));

        Assert.Equal("\"act\"", string.Join(",", handler.LastRequest.Headers.GetValues("SOAPAction")));
        Assert.Equal("abc", string.Join(",", handler.LastRequest.Headers.GetValues("X-Trace-Id")));
    }

    [Fact]
    public async Task SendSoapAsync_NullArguments_Throw()
    {
        var client = BuildClient(new RecordingHandler());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendSoapAsync("svc", action: null!, bodyXml: TempuriBody));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.SendSoapAsync("svc", "act", bodyXml: null!));
    }
}
