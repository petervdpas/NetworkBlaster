using System;
using NetworkBlaster.Soap;
using Xunit;

namespace NetworkBlaster.Tests.Soap;

public class SoapEnvelopeTests
{
    private const string Body11 = "<GetWeather xmlns=\"http://tempuri.org/\"><City>Paris</City></GetWeather>";

    [Fact]
    public void Wrap_V11_UsesXmlsoapNamespace_AndWrapsBody()
    {
        var envelope = SoapEnvelope.Wrap(SoapVersion.V11, Body11);
        Assert.Contains("xmlns:soap=\"" + SoapEnvelope.Soap11Namespace + "\"", envelope);
        Assert.Contains("<soap:Body>" + Body11 + "</soap:Body>", envelope);
        Assert.DoesNotContain("<soap:Header>", envelope);
    }

    [Fact]
    public void Wrap_V12_UsesW3cNamespace()
    {
        var envelope = SoapEnvelope.Wrap(SoapVersion.V12, Body11);
        Assert.Contains("xmlns:soap=\"" + SoapEnvelope.Soap12Namespace + "\"", envelope);
    }

    [Fact]
    public void Wrap_HeaderEntries_IncludedInsideHeaderElement()
    {
        var envelope = SoapEnvelope.Wrap(SoapVersion.V11, Body11,
            new[] { "<auth>secret</auth>", "<trace>abc-123</trace>" });
        Assert.Contains("<soap:Header><auth>secret</auth><trace>abc-123</trace></soap:Header>", envelope);
    }

    [Fact]
    public void Wrap_NoHeaderEntries_OmitsHeaderElement()
    {
        var envelope = SoapEnvelope.Wrap(SoapVersion.V11, Body11);
        Assert.DoesNotContain("Header", envelope);
    }

    [Fact]
    public void Wrap_NullBody_Throws() => Assert.Throws<ArgumentNullException>(() => SoapEnvelope.Wrap(SoapVersion.V11, null!));

    [Fact]
    public void Unwrap_V11_ReturnsInnerOfBody()
    {
        var envelope = SoapEnvelope.Wrap(SoapVersion.V11, Body11);
        Assert.Contains("Paris", SoapEnvelope.Unwrap(envelope));
    }

    [Fact]
    public void Unwrap_V12_ReturnsInnerOfBody()
    {
        var envelope = SoapEnvelope.Wrap(SoapVersion.V12, Body11);
        Assert.Contains("Paris", SoapEnvelope.Unwrap(envelope));
    }

    [Fact]
    public void Unwrap_MultipleBodyChildren_ConcatenatesAll()
    {
        var envelope =
            $"<soap:Envelope xmlns:soap=\"{SoapEnvelope.Soap11Namespace}\"><soap:Body><A/><B>x</B></soap:Body></soap:Envelope>";
        var inner = SoapEnvelope.Unwrap(envelope);
        Assert.Contains("<A", inner);
        Assert.Contains("<B>x</B>", inner);
    }

    [Fact]
    public void Unwrap_PreservesNamespaceAttributesOnBodyChildren()
    {
        var envelope = SoapEnvelope.Wrap(SoapVersion.V11, Body11);
        var inner = SoapEnvelope.Unwrap(envelope);
        Assert.Contains("xmlns=\"http://tempuri.org/\"", inner);
    }

    [Fact]
    public void Unwrap_NotAnEnvelope_Throws()
        => Assert.Throws<InvalidOperationException>(() => SoapEnvelope.Unwrap("<NotAnEnvelope/>"));

    [Fact]
    public void Unwrap_MalformedXml_Throws()
        => Assert.Throws<InvalidOperationException>(() => SoapEnvelope.Unwrap("<not-closed>"));

    [Fact]
    public void Unwrap_UnknownEnvelopeNamespace_Throws()
    {
        var envelope = "<soap:Envelope xmlns:soap=\"http://example.com/wrong\"><soap:Body/></soap:Envelope>";
        Assert.Throws<InvalidOperationException>(() => SoapEnvelope.Unwrap(envelope));
    }

    [Fact]
    public void Unwrap_MissingBody_Throws()
    {
        var envelope = $"<soap:Envelope xmlns:soap=\"{SoapEnvelope.Soap11Namespace}\"></soap:Envelope>";
        Assert.Throws<InvalidOperationException>(() => SoapEnvelope.Unwrap(envelope));
    }

    [Fact]
    public void TryGetFault_V11_ReturnsCodeReasonDetail()
    {
        var faultEnvelope = $"""
            <soap:Envelope xmlns:soap="{SoapEnvelope.Soap11Namespace}">
              <soap:Body>
                <soap:Fault>
                  <faultcode>soap:Server</faultcode>
                  <faultstring>Boom</faultstring>
                  <detail>extra info</detail>
                </soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """;
        var fault = SoapEnvelope.TryGetFault(faultEnvelope);
        Assert.NotNull(fault);
        Assert.Equal(SoapVersion.V11, fault!.Version);
        Assert.Equal("soap:Server", fault.Code);
        Assert.Equal("Boom", fault.Reason);
        Assert.Equal("extra info", fault.Detail);
    }

    [Fact]
    public void TryGetFault_V11_NoFault_ReturnsNull()
    {
        var ok = SoapEnvelope.Wrap(SoapVersion.V11, "<Ok/>");
        Assert.Null(SoapEnvelope.TryGetFault(ok));
    }

    [Fact]
    public void TryGetFault_V12_ReturnsCodeAndReason()
    {
        var faultEnvelope = $"""
            <env:Envelope xmlns:env="{SoapEnvelope.Soap12Namespace}">
              <env:Body>
                <env:Fault>
                  <env:Code><env:Value>env:Sender</env:Value></env:Code>
                  <env:Reason><env:Text xml:lang="en">Bad input</env:Text></env:Reason>
                </env:Fault>
              </env:Body>
            </env:Envelope>
            """;
        var fault = SoapEnvelope.TryGetFault(faultEnvelope);
        Assert.NotNull(fault);
        Assert.Equal(SoapVersion.V12, fault!.Version);
        Assert.Equal("env:Sender", fault.Code);
        Assert.Equal("Bad input", fault.Reason);
    }

    [Fact]
    public void TryGetFault_V12_NoFault_ReturnsNull()
    {
        var ok = SoapEnvelope.Wrap(SoapVersion.V12, "<Ok/>");
        Assert.Null(SoapEnvelope.TryGetFault(ok));
    }

    [Fact]
    public void NamespaceFor_V11_ReturnsXmlsoapUrl() => Assert.Equal(SoapEnvelope.Soap11Namespace, SoapEnvelope.NamespaceFor(SoapVersion.V11));

    [Fact]
    public void NamespaceFor_V12_ReturnsW3cUrl() => Assert.Equal(SoapEnvelope.Soap12Namespace, SoapEnvelope.NamespaceFor(SoapVersion.V12));
}
