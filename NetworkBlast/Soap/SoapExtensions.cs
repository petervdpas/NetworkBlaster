using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using NetworkBlast.Interfaces;

namespace NetworkBlast.Soap;

/// <summary>
/// SOAP 1.1 / 1.2 helpers on top of <see cref="INetClient"/>: compose envelopes,
/// POST them with the right transport headers, surface SOAP <c>Fault</c>s as exceptions.
/// </summary>
public static class SoapExtensions
{
    /// <summary>
    /// POSTs a SOAP envelope built from <paramref name="bodyXml"/> and returns the
    /// inner XML of the response <c>Body</c>. Throws <see cref="SoapFault"/> when
    /// the server responds with a <c>Fault</c> element.
    /// </summary>
    /// <param name="client">The <see cref="INetClient"/> to send through.</param>
    /// <param name="path">Relative or absolute path to the SOAP endpoint.</param>
    /// <param name="action">SOAP action URI; sent in the <c>SOAPAction</c> header (1.1) or as the <c>action=</c> parameter of <c>Content-Type</c> (1.2).</param>
    /// <param name="bodyXml">Body fragment placed inside <c>&lt;Body&gt;</c>. No XML declaration.</param>
    /// <param name="version">SOAP version controlling envelope namespace and transport.</param>
    /// <param name="headerEntries">Optional XML fragments placed inside <c>&lt;Header&gt;</c>.</param>
    /// <param name="options">Per-request options (additional headers, timeout, retry).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<string> SendSoapAsync(
        this INetClient client,
        string path,
        string action,
        string bodyXml,
        SoapVersion version = SoapVersion.V11,
        IEnumerable<string>? headerEntries = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(bodyXml);

        var envelope = SoapEnvelope.Wrap(version, bodyXml, headerEntries);
        var content = BuildContent(envelope, version, action);
        var requestOptions = AddSoapActionHeader(options, version, action);

        using var response = await client.PostAsync(path, content, requestOptions, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var fault = SoapEnvelope.TryGetFault(raw);
        if (fault is not null) throw fault;

        // SOAP servers may return non-2xx with a Fault payload; throw above takes priority.
        // For non-2xx without a Fault, surface as HttpRequestException.
        response.EnsureSuccessStatusCode();
        return SoapEnvelope.Unwrap(raw);
    }

    /// <summary>
    /// Typed SOAP call: serialises <paramref name="payload"/> via <see cref="XmlSerializer"/>,
    /// wraps it in an envelope, POSTs, and deserialises the response body to
    /// <typeparamref name="TResponse"/>. Throws <see cref="SoapFault"/> on fault responses.
    /// </summary>
    public static async Task<TResponse?> SendSoapAsync<TRequest, TResponse>(
        this INetClient client,
        string path,
        string action,
        TRequest payload,
        SoapVersion version = SoapVersion.V11,
        XmlSerializerNamespaces? namespaces = null,
        IEnumerable<string>? headerEntries = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(payload);

        var bodyXml = SerializeFragment(payload, namespaces);
        var inner = await client
            .SendSoapAsync(path, action, bodyXml, version, headerEntries, options, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(inner)) return default;

        var serializer = new XmlSerializer(typeof(TResponse));
        using var reader = new StringReader(inner);
        using var xmlReader = XmlReader.Create(reader);
        return (TResponse?)serializer.Deserialize(xmlReader);
    }

    private static StringContent BuildContent(string envelope, SoapVersion version, string action)
    {
        // SOAP 1.2 carries the action inside the content type's `action=` parameter.
        // SOAP 1.1 carries it in a separate SOAPAction header (handled in AddSoapActionHeader).
        var mediaType = version == SoapVersion.V11 ? SoapEnvelope.Soap11ContentType : SoapEnvelope.Soap12ContentType;
        var content = new StringContent(envelope, Encoding.UTF8, mediaType);
        if (version == SoapVersion.V12)
        {
            content.Headers.ContentType!.Parameters.Add(
                new System.Net.Http.Headers.NameValueHeaderValue("action", "\"" + action + "\""));
        }
        return content;
    }

    private static RequestOptions? AddSoapActionHeader(RequestOptions? options, SoapVersion version, string action)
    {
        if (version != SoapVersion.V11) return options;

        var quoted = "\"" + action + "\"";
        return options is null
            ? new RequestOptions { Headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["SOAPAction"] = quoted } }
            : options.AddHeader("SOAPAction", quoted);
    }

    private static string SerializeFragment<T>(T value, XmlSerializerNamespaces? namespaces)
    {
        var serializer = new XmlSerializer(typeof(T));
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            Encoding = new UTF8Encoding(false),
        };
        using var sw = new StringWriter();
        using (var xw = XmlWriter.Create(sw, settings))
        {
            serializer.Serialize(xw, value, namespaces ?? EmptyNamespaces);
        }
        return sw.ToString();
    }

    private static readonly XmlSerializerNamespaces EmptyNamespaces = BuildEmptyNamespaces();

    private static XmlSerializerNamespaces BuildEmptyNamespaces()
    {
        // Suppresses the default xsi/xsd attributes that XmlSerializer would otherwise emit.
        var ns = new XmlSerializerNamespaces();
        ns.Add("", "");
        return ns;
    }
}
