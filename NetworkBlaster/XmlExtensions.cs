using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using NetworkBlaster.Interfaces;

namespace NetworkBlaster;

/// <summary>
/// XML mirror of the JSON helpers on <see cref="INetClient"/>. Uses
/// <see cref="XmlSerializer"/> for both directions and sends/expects
/// <c>application/xml</c> bodies.
/// </summary>
public static class XmlExtensions
{
    /// <summary>GET <paramref name="path"/> and deserialise the response body via <see cref="XmlSerializer"/>.</summary>
    public static async Task<T?> GetXmlAsync<T>(
        this INetClient client,
        string path,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync(path, options, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (stream.CanSeek && stream.Length == 0) return default;

        var serializer = new XmlSerializer(typeof(T));
        using var xmlReader = XmlReader.Create(stream);
        return (T?)serializer.Deserialize(xmlReader);
    }

    /// <summary>POST <paramref name="payload"/> as <c>application/xml</c> and deserialise the response.</summary>
    public static async Task<TResponse?> PostXmlAsync<TRequest, TResponse>(
        this INetClient client,
        string path,
        TRequest payload,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(payload);

        var body = Serialize(payload);
        using var response = await client.PostAsync(path, body, options, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (stream.CanSeek && stream.Length == 0) return default;

        var serializer = new XmlSerializer(typeof(TResponse));
        using var xmlReader = XmlReader.Create(stream);
        return (TResponse?)serializer.Deserialize(xmlReader);
    }

    /// <summary>PUT <paramref name="payload"/> as <c>application/xml</c> and deserialise the response.</summary>
    public static async Task<TResponse?> PutXmlAsync<TRequest, TResponse>(
        this INetClient client,
        string path,
        TRequest payload,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(payload);

        var body = Serialize(payload);
        using var response = await client.PutAsync(path, body, options, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (stream.CanSeek && stream.Length == 0) return default;

        var serializer = new XmlSerializer(typeof(TResponse));
        using var xmlReader = XmlReader.Create(stream);
        return (TResponse?)serializer.Deserialize(xmlReader);
    }

    private static StringContent Serialize<T>(T value)
    {
        var serializer = new XmlSerializer(typeof(T));
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Indent = false,
            Encoding = new UTF8Encoding(false),
        };
        using var sw = new StringWriter();
        using (var xw = XmlWriter.Create(sw, settings))
        {
            serializer.Serialize(xw, value);
        }
        return new StringContent(sw.ToString(), Encoding.UTF8, "application/xml");
    }
}
