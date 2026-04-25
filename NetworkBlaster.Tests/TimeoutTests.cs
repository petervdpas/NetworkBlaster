using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests;

public class TimeoutTests
{
    [Fact]
    public async Task PerRequestTimeout_FiresCancellation_WhenServerStallsLonger()
    {
        var handler = new SlowHandler(TimeSpan.FromSeconds(2));
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler));

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            client.GetAsync("ping", Options.Timeout(TimeSpan.FromMilliseconds(50))));
    }

    [Fact]
    public async Task PerRequestTimeout_DoesNotFire_WhenServerRespondsInTime()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler));

        var response = await client.GetAsync("ping", Options.Timeout(TimeSpan.FromSeconds(5)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CallerCancellationToken_TakesPriorityOverTimeout()
    {
        var handler = new SlowHandler(TimeSpan.FromSeconds(2));
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler));

        using var cts = new CancellationTokenSource();
        var task = client.GetAsync("ping", Options.Timeout(TimeSpan.FromSeconds(10)), cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        public SlowHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
