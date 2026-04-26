using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkBlast.Tests.Support;

/// <summary>
/// In-memory <see cref="HttpMessageHandler"/> that records every request seen and
/// dispenses queued responses (or queued exceptions) in FIFO order.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> RequestBodies { get; } = new();
    public HttpRequestMessage LastRequest => Requests[^1];

    public RecordingHandler RespondWith(HttpStatusCode status, string? body = null, string mediaType = "application/json")
    {
        _responders.Enqueue(_ =>
        {
            var resp = new HttpResponseMessage(status);
            if (body is not null) resp.Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType);
            return resp;
        });
        return this;
    }

    public RecordingHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responders.Enqueue(factory);
        return this;
    }

    public RecordingHandler ThrowsOnce(Exception ex)
    {
        _responders.Enqueue(_ => throw ex);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var captured = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers) captured.Headers.TryAddWithoutValidation(header.Key, header.Value);

        string? body = null;
        if (request.Content is not null)
        {
            var bytes = request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
            body = System.Text.Encoding.UTF8.GetString(bytes);
            var clonedContent = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers) clonedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            captured.Content = clonedContent;
        }
        Requests.Add(captured);
        RequestBodies.Add(body);

        if (_responders.Count == 0)
            return Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("RecordingHandler: no queued response for this request."));

        try
        {
            var responder = _responders.Dequeue();
            return Task.FromResult(responder(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }
}
