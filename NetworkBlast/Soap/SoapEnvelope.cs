using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NetworkBlast.Soap;

/// <summary>
/// Static helpers for composing and parsing SOAP envelopes. Supports both
/// SOAP 1.1 and 1.2 in their canonical shapes.
/// </summary>
public static class SoapEnvelope
{
    /// <summary>SOAP 1.1 envelope namespace.</summary>
    public const string Soap11Namespace = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>SOAP 1.2 envelope namespace.</summary>
    public const string Soap12Namespace = "http://www.w3.org/2003/05/soap-envelope";

    /// <summary>SOAP 1.1 content type.</summary>
    public const string Soap11ContentType = "text/xml";

    /// <summary>SOAP 1.2 content type.</summary>
    public const string Soap12ContentType = "application/soap+xml";

    /// <summary>Returns the envelope namespace URI for the given version.</summary>
    public static string NamespaceFor(SoapVersion version) =>
        version == SoapVersion.V11 ? Soap11Namespace : Soap12Namespace;

    /// <summary>
    /// Wraps an XML body fragment in a SOAP envelope of the given version.
    /// </summary>
    /// <param name="version">SOAP version controlling namespace and shape.</param>
    /// <param name="body">XML fragment placed inside <c>Body</c>. Must not include an XML declaration.</param>
    /// <param name="headerEntries">Optional entries placed inside <c>Header</c>; each is an XML fragment.</param>
    public static string Wrap(SoapVersion version, string body, IEnumerable<string>? headerEntries = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var ns = NamespaceFor(version);
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<soap:Envelope xmlns:soap=\"").Append(ns).Append("\">");

        if (headerEntries is not null)
        {
            sb.Append("<soap:Header>");
            foreach (var entry in headerEntries) sb.Append(entry);
            sb.Append("</soap:Header>");
        }

        sb.Append("<soap:Body>").Append(body).Append("</soap:Body>");
        sb.Append("</soap:Envelope>");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the inner XML of the envelope's <c>Body</c> element. The version
    /// is auto-detected from the envelope namespace.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the input is not a SOAP envelope.</exception>
    public static string Unwrap(string envelopeXml)
    {
        ArgumentNullException.ThrowIfNull(envelopeXml);
        var (doc, version) = ParseAndDetectVersion(envelopeXml);
        var ns = NamespaceFor(version);
        var body = doc.Root!.Element(XName.Get("Body", ns))
            ?? throw new InvalidOperationException("SOAP envelope is missing a Body element.");

        // Return the concatenated inner XML of every direct child of Body.
        var sb = new StringBuilder();
        foreach (var node in body.Nodes()) sb.Append(node.ToString(SaveOptions.DisableFormatting));
        return sb.ToString();
    }

    /// <summary>
    /// Returns a <see cref="SoapFault"/> if the envelope carries a fault, otherwise <c>null</c>.
    /// </summary>
    public static SoapFault? TryGetFault(string envelopeXml)
    {
        ArgumentNullException.ThrowIfNull(envelopeXml);
        var (doc, version) = ParseAndDetectVersion(envelopeXml);
        var ns = NamespaceFor(version);
        var body = doc.Root!.Element(XName.Get("Body", ns));
        var fault = body?.Element(XName.Get("Fault", ns));
        if (fault is null) return null;

        return version == SoapVersion.V11 ? ParseFault11(fault) : ParseFault12(fault, ns);
    }

    private static (XDocument doc, SoapVersion version) ParseAndDetectVersion(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (XmlException ex)
        {
            throw new InvalidOperationException("SOAP envelope is not well-formed XML.", ex);
        }

        if (doc.Root is null || doc.Root.Name.LocalName != "Envelope")
            throw new InvalidOperationException("Root element is not <Envelope>.");

        var ns = doc.Root.Name.NamespaceName;
        return ns switch
        {
            Soap11Namespace => (doc, SoapVersion.V11),
            Soap12Namespace => (doc, SoapVersion.V12),
            _ => throw new InvalidOperationException($"Unrecognised SOAP envelope namespace: '{ns}'."),
        };
    }

    private static SoapFault ParseFault11(XElement fault)
    {
        var code = fault.Element("faultcode")?.Value ?? string.Empty;
        var reason = fault.Element("faultstring")?.Value ?? string.Empty;
        var detail = fault.Element("detail")?.Value;
        return new SoapFault(SoapVersion.V11, code, reason, detail);
    }

    private static SoapFault ParseFault12(XElement fault, string ns)
    {
        var code = fault
            .Element(XName.Get("Code", ns))?
            .Element(XName.Get("Value", ns))?.Value ?? string.Empty;
        var reason = fault
            .Element(XName.Get("Reason", ns))?
            .Element(XName.Get("Text", ns))?.Value ?? string.Empty;
        var detail = fault.Element(XName.Get("Detail", ns))?.ToString(SaveOptions.DisableFormatting);
        return new SoapFault(SoapVersion.V12, code, reason, detail);
    }
}
