using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    byte[]? RawBody)
{
    /// <summary><see langword="true"/> when no body was sent at all — distinct from an empty object.</summary>
    public bool BodyOmitted => RawBody is null;

    /// <summary>The body as UTF-8 text, or <see langword="null"/> when none was sent.</summary>
    public string? Body => RawBody is null ? null : Encoding.UTF8.GetString(RawBody);

    /// <summary>
    /// The body's media type, lower-case and without parameters, or <see langword="null"/>
    /// when the request carried no <c>Content-Type</c>.
    /// </summary>
    public string? MediaType =>
        Headers.TryGetValue("Content-Type", out string? value)
        && MediaTypeHeaderValue.TryParse(value, out MediaTypeHeaderValue? parsed)
            ? parsed.MediaType?.ToLowerInvariant()
            : null;

    /// <summary>
    /// The body parsed as JSON; <see langword="null"/> when none was sent or it was not sent as
    /// <c>application/json</c>. A multipart body is never handed to the JSON parser.
    /// </summary>
    public JsonNode? BodyJson => Body is null || MediaType != "application/json" ? null : JsonNode.Parse(Body);

    /// <summary>
    /// The parts of a <c>multipart/form-data</c> body; <see langword="null"/> for any other body,
    /// or when the body is not well-formed (see <see cref="PartsError"/>).
    /// </summary>
    public IReadOnlyList<CapturedPart>? Parts => ReadParts().Parts;

    /// <summary>Why a <c>multipart/form-data</c> body could not be read, or <see langword="null"/>.</summary>
    public string? PartsError => ReadParts().Error;

    /// <summary>Records a request, reading its body without consuming anything the SDK still needs.</summary>
    internal static async Task<CapturedRequest> CaptureAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri url = request.RequestUri!;

        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
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

        return new CapturedRequest(
            request.Method.Method,
            url,
            url.GetLeftPart(UriPartial.Authority),
            url.AbsolutePath,
            RecordingHandler.ParseQuery(url.Query),
            headers,
            body);
    }

    private (IReadOnlyList<CapturedPart>? Parts, string? Error) ReadParts()
    {
        if (RawBody is null || MediaType != "multipart/form-data")
        {
            return (null, null);
        }

        return Multipart.TryParse(RawBody, Headers["Content-Type"], out List<CapturedPart> parts, out string? error)
            ? (parts, null)
            : (null, error);
    }
}

/// <summary>
/// One part of a captured <c>multipart/form-data</c> body.
/// </summary>
/// <param name="Disposition">The Content-Disposition type, which must be <c>form-data</c>.</param>
/// <param name="Name">The part's name, unquoted.</param>
/// <param name="FileNameParameter">The <c>filename</c> parameter, unquoted and decoded, if sent.</param>
/// <param name="FileNameStarParameter">The <c>filename*</c> parameter, decoded, if sent.</param>
/// <param name="ContentType">The part's own Content-Type, if sent.</param>
/// <param name="Headers">Every header of the part.</param>
/// <param name="Content">The part's bytes, exactly as sent.</param>
public sealed record CapturedPart(
    string Disposition,
    string? Name,
    string? FileNameParameter,
    string? FileNameStarParameter,
    string? ContentType,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Content)
{
    /// <summary>The file name a server reads: <c>filename*</c> when present, else <c>filename</c>.</summary>
    public string? FileName => FileNameStarParameter ?? FileNameParameter;
}

/// <summary>
/// A strict reader for the <c>multipart/form-data</c> bodies the SDK sends, so a test can
/// check every part byte for byte. Anything it does not recognise is an error, never a guess.
/// </summary>
internal static class Multipart
{
    internal static bool TryParse(byte[] body, string contentType, out List<CapturedPart> parts, out string? error)
    {
        parts = new List<CapturedPart>();
        error = null;

        if (!MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? media))
        {
            error = "the Content-Type header cannot be parsed";
            return false;
        }

        string? boundary = null;
        foreach (NameValueHeaderValue parameter in media.Parameters)
        {
            if (string.Equals(parameter.Name, "boundary", StringComparison.OrdinalIgnoreCase))
            {
                boundary = parameter.Value?.Trim('"');
            }
        }

        if (string.IsNullOrEmpty(boundary))
        {
            error = "the Content-Type header has no boundary";
            return false;
        }

        byte[] delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        byte[] nextDelimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);
        byte[] crlf = { (byte)'\r', (byte)'\n' };
        byte[] headersEnd = Encoding.ASCII.GetBytes("\r\n\r\n");
        ReadOnlySpan<byte> span = body;

        if (!span.StartsWith(delimiter))
        {
            error = "the body does not start with the boundary";
            return false;
        }

        int position = delimiter.Length;
        while (true)
        {
            ReadOnlySpan<byte> rest = span[position..];
            if (rest.StartsWith("--"u8))
            {
                if (!rest[2..].SequenceEqual(crlf) && rest.Length != 2)
                {
                    error = "unexpected bytes after the closing boundary";
                    return false;
                }

                return true;
            }

            if (!rest.StartsWith(crlf))
            {
                error = "a boundary is not followed by a line break";
                return false;
            }

            position += crlf.Length;

            string headerText;
            if (span[position..].StartsWith(crlf))
            {
                headerText = string.Empty;
                position += crlf.Length;
            }
            else
            {
                int end = span[position..].IndexOf(headersEnd);
                if (end < 0)
                {
                    error = "a part's headers are not terminated";
                    return false;
                }

                headerText = Encoding.Latin1.GetString(span.Slice(position, end));
                position += end + headersEnd.Length;
            }

            int contentLength = span[position..].IndexOf(nextDelimiter);
            if (contentLength < 0)
            {
                error = "a part is not terminated by the boundary";
                return false;
            }

            byte[] content = span.Slice(position, contentLength).ToArray();
            position += contentLength + nextDelimiter.Length;

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0 || !headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim()))
                {
                    error = "a part header is malformed or repeated";
                    return false;
                }
            }

            if (!headers.TryGetValue("Content-Disposition", out string? dispositionText)
                || !ContentDispositionHeaderValue.TryParse(dispositionText, out ContentDispositionHeaderValue? disposition))
            {
                error = "a part has no readable Content-Disposition";
                return false;
            }

            headers.TryGetValue("Content-Type", out string? partContentType);
            parts.Add(new CapturedPart(
                disposition.DispositionType,
                disposition.Name?.Trim('"'),
                disposition.FileName?.Trim('"'),
                disposition.FileNameStar,
                partContentType,
                headers,
                content));
        }
    }
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

    /// <summary>A <c>Retry-After</c> header, added as given, so an unparseable value reaches the SDK too.</summary>
    public string? RetryAfter { get; init; }

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

    /// <summary>
    /// Every time the transport was handed a request, counted before anything can throw —
    /// unlike <see cref="Calls"/>, which a cancelled body read never reaches.
    /// </summary>
    public int Invocations { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Invocations++;
        Uri url = request.RequestUri!;

        Calls.Add(await CapturedRequest.CaptureAsync(request, cancellationToken).ConfigureAwait(false));

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
        if (mock.RetryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", mock.RetryAfter);
        }

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

    internal static Dictionary<string, string> ParseQuery(string query)
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
