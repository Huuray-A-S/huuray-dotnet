using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Huuray.Tests;

/// <summary>
/// One request the SDK made, captured by <see cref="RecordingHandler"/>.
/// </summary>
public sealed record CapturedRequest(
    string Method,
    Uri Url,
    string Origin,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyDictionary<string, string> Headers,
    string? Body)
{
    /// <summary><see langword="true"/> when no body was sent at all — distinct from an empty object.</summary>
    public bool BodyOmitted => Body is null;

    /// <summary>The request body parsed as JSON, or <see langword="null"/> when none was sent.</summary>
    public JsonNode? BodyJson => Body is null ? null : JsonNode.Parse(Body);
}

/// <summary>
/// What the fake transport should do for one request.
/// </summary>
public sealed record MockResponse
{
    /// <summary>HTTP status to answer with.</summary>
    public int Status { get; init; } = 200;

    /// <summary>Response body as JSON. Ignored when <see cref="Text"/> is set.</summary>
    public JsonNode? Json { get; init; }

    /// <summary>Raw body text; takes precedence over <see cref="Json"/>. Use to simulate garbled responses.</summary>
    public string? Text { get; init; }

    /// <summary>Throw instead of responding, to simulate a network failure before headers arrive.</summary>
    public Exception? Throws { get; init; }

    /// <summary>Resolve the response, but make reading its body throw — a mid-body drop.</summary>
    public Exception? BodyThrows { get; init; }

    /// <summary>Resolve the response, but never finish the body — a mid-body stall.</summary>
    public bool BodyHangs { get; init; }

    /// <summary>Delay before answering at all, to let a client timeout fire.</summary>
    public bool Hangs { get; init; }
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> that records requests and replays canned responses.
/// </summary>
/// <remarks>
/// <para>
/// No test in this suite touches the network: ordering gift cards from a test runner
/// would spend real money.
/// </para>
/// <para>
/// Queue semantics: a list is strict — one response per request, and a request beyond the
/// end THROWS, so a test can never silently absorb an extra HTTP call. An accidental
/// order retry is exactly the bug class this suite exists to catch. A single response
/// repeats for every request.
/// </para>
/// </remarks>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly bool _strict;
    private readonly Queue<MockResponse> _queue;
    private readonly MockResponse _repeating;

    public RecordingHandler(MockResponse response)
    {
        _strict = false;
        _repeating = response;
        _queue = new Queue<MockResponse>();
    }

    public RecordingHandler(IEnumerable<MockResponse> responses)
    {
        _strict = true;
        _repeating = new MockResponse();
        _queue = new Queue<MockResponse>(responses);
    }

    public List<CapturedRequest> Calls { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri url = request.RequestUri!;

        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        if (request.Content is not null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }
        }

        Calls.Add(new CapturedRequest(
            request.Method.Method,
            url,
            url.GetLeftPart(UriPartial.Authority),
            url.AbsolutePath,
            ParseQuery(url.Query),
            headers,
            body));

        MockResponse mock;
        if (_strict)
        {
            if (_queue.Count == 0)
            {
                throw new InvalidOperationException(
                    $"RecordingHandler: request #{Calls.Count} ({request.Method.Method} {url.AbsolutePath}) " +
                    "exceeds the queued responses — the code under test made more HTTP calls than the test expected.");
            }

            mock = _queue.Dequeue();
        }
        else
        {
            mock = _repeating;
        }

        if (mock.Throws is not null)
        {
            throw mock.Throws;
        }

        if (mock.Hangs)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        string text = mock.Text ?? (mock.Json?.ToJsonString() ?? "{}");

        HttpResponseMessage response = new((HttpStatusCode)mock.Status);
        if (mock.BodyThrows is not null)
        {
            response.Content = new StreamContent(new FailingStream(mock.BodyThrows));
        }
        else if (mock.BodyHangs)
        {
            response.Content = new StreamContent(new HangingStream());
        }
        else
        {
            response.Content = new StringContent(text, Encoding.UTF8, "application/json");
        }

        return response;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
            }
            else
            {
                result[Uri.UnescapeDataString(pair[..equals])] = Uri.UnescapeDataString(pair[(equals + 1)..]);
            }
        }

        return result;
    }
}

/// <summary>A response body that fails partway through — the connection dropping mid-stream.</summary>
internal sealed class FailingStream : Stream
{
    private readonly Exception _exception;

    internal FailingStream(Exception exception) => _exception = exception;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw _exception;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        throw _exception;

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        throw _exception;

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A response body that never arrives — the stall a request timeout exists for.</summary>
internal sealed class HangingStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Thread.Sleep(System.Threading.Timeout.Infinite);
        return 0;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
