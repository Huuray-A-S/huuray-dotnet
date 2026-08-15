using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Huuray;

/// <summary>
/// The API returned a non-2xx response.
/// </summary>
/// <remarks>
/// The API reports failures with <c>Status</c> and <c>StatusMessage</c> in the body
/// alongside the HTTP status. <c>Message</c> carries the same text but is marked
/// deprecated in the specification, so this client reads <c>StatusMessage</c> first
/// and falls back to <c>Message</c>.
/// </remarks>
public class HuurayApiException : HuurayException
{
    /// <summary>Creates an API exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="status">The <c>Status</c> field from the response body, when present.</param>
    /// <param name="statusMessage">The <c>StatusMessage</c> field, or the deprecated <c>Message</c> as a fallback.</param>
    /// <param name="body">The parsed response body, already redacted.</param>
    /// <param name="method">The HTTP method of the failed request.</param>
    /// <param name="path">The path of the failed request.</param>
    public HuurayApiException(
        string message,
        int httpStatus,
        int? status,
        string? statusMessage,
        JsonNode? body,
        string method,
        string path)
        : base(message)
    {
        HttpStatus = httpStatus;
        Status = status;
        StatusMessage = statusMessage;
        Body = body;
        Method = method;
        Path = path;
    }

    /// <summary>HTTP status of the response.</summary>
    public int HttpStatus { get; }

    /// <summary>The <c>Status</c> field from the response body, when the API sent one.</summary>
    public int? Status { get; }

    /// <summary>
    /// The <c>StatusMessage</c> field from the response body, falling back to the
    /// deprecated <c>Message</c> field.
    /// </summary>
    public string? StatusMessage { get; }

    /// <summary>
    /// The parsed response body — <strong>redacted</strong>.
    /// </summary>
    /// <remarks>
    /// Any field that could carry a voucher code or a contact detail is masked before
    /// it is stored here, so logging an exception can never leak a bearer instrument.
    /// The unredacted body is discarded and cannot be recovered.
    /// </remarks>
    public JsonNode? Body { get; }

    /// <summary>The HTTP method of the failed request.</summary>
    public string Method { get; }

    /// <summary>The path of the failed request, for example <c>/v4/Order</c>.</summary>
    public string Path { get; }

    /// <summary>
    /// Maps an HTTP status and response body onto the right exception type.
    /// </summary>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="body">The parsed response body, or <see langword="null"/> if there was none.</param>
    /// <param name="method">The HTTP method of the failed request.</param>
    /// <param name="path">The path of the failed request.</param>
    /// <returns>
    /// <see cref="HuurayAuthException"/> for 401 and 403, <see cref="HuurayNotFoundException"/>
    /// for 404, <see cref="HuurayValidationException"/> for 422,
    /// <see cref="HuurayServerException"/> for 5xx, and <see cref="HuurayApiException"/>
    /// for anything else.
    /// </returns>
    public static HuurayApiException Create(int httpStatus, JsonNode? body, string method, string path)
    {
        int? status = ReadInt32(body, "Status");
        string? statusMessage = ReadString(body, "StatusMessage") ?? ReadString(body, "Message");

        string detail = statusMessage is null ? string.Empty : " — " + statusMessage;
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} failed with HTTP {2}{3}",
            method,
            path,
            httpStatus,
            detail);

        // Only the redacted copy is retained, so an undocumented error payload
        // carrying voucher or recipient fields cannot ride into a consumer's logs.
        JsonNode? redacted = Redaction.Redact(body);

        return httpStatus switch
        {
            401 or 403 => new HuurayAuthException(message, httpStatus, status, statusMessage, redacted, method, path),
            404 => new HuurayNotFoundException(message, httpStatus, status, statusMessage, redacted, method, path),
            422 => new HuurayValidationException(message, httpStatus, status, statusMessage, redacted, method, path),
            >= 500 => new HuurayServerException(message, httpStatus, status, statusMessage, redacted, method, path),
            _ => new HuurayApiException(message, httpStatus, status, statusMessage, redacted, method, path),
        };
    }

    private static string? ReadString(JsonNode? body, string property)
    {
        if (body is JsonObject obj
            && obj.TryGetPropertyValue(property, out JsonNode? node)
            && node is JsonValue value
            && value.TryGetValue(out string? text))
        {
            return text;
        }

        return null;
    }

    private static int? ReadInt32(JsonNode? body, string property)
    {
        if (body is JsonObject obj
            && obj.TryGetPropertyValue(property, out JsonNode? node)
            && node is JsonValue value
            && value.TryGetValue(out int number))
        {
            return number;
        }

        return null;
    }
}

/// <summary>
/// HTTP 401 or 403.
/// </summary>
/// <remarks>
/// With credentials you believe are correct, the usual causes are, in order: a wrong
/// <c>X-API-HASH</c> encoding (see <see cref="HuurayClientOptions.HashEncoding"/>), a
/// reused nonce (the API remembers them for 60 days), or a nonce over 50 characters.
/// </remarks>
public sealed class HuurayAuthException : HuurayApiException
{
    /// <summary>Creates an authentication exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="status">The <c>Status</c> field from the response body, when present.</param>
    /// <param name="statusMessage">The <c>StatusMessage</c> field, or the deprecated <c>Message</c>.</param>
    /// <param name="body">The parsed response body, already redacted.</param>
    /// <param name="method">The HTTP method of the failed request.</param>
    /// <param name="path">The path of the failed request.</param>
    public HuurayAuthException(
        string message,
        int httpStatus,
        int? status,
        string? statusMessage,
        JsonNode? body,
        string method,
        string path)
        : base(message, httpStatus, status, statusMessage, body, method, path)
    {
    }
}

/// <summary>
/// HTTP 404 — the order, voucher, or product was not found.
/// </summary>
/// <remarks>
/// The API signals an <em>empty result set</em> this way rather than with an empty
/// <c>200</c> — observed live on <c>POST /v4/Template</c>, which answers
/// <c>404 "There were no active templates"</c>. So a 404 from <c>POST /v4/Search</c>
/// means "no order matched", which during reconciliation reads as "the order did not land".
/// </remarks>
public sealed class HuurayNotFoundException : HuurayApiException
{
    /// <summary>Creates a not-found exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="status">The <c>Status</c> field from the response body, when present.</param>
    /// <param name="statusMessage">The <c>StatusMessage</c> field, or the deprecated <c>Message</c>.</param>
    /// <param name="body">The parsed response body, already redacted.</param>
    /// <param name="method">The HTTP method of the failed request.</param>
    /// <param name="path">The path of the failed request.</param>
    public HuurayNotFoundException(
        string message,
        int httpStatus,
        int? status,
        string? statusMessage,
        JsonNode? body,
        string method,
        string path)
        : base(message, httpStatus, status, statusMessage, body, method, path)
    {
    }
}

/// <summary>
/// HTTP 422 — the request was well-formed but rejected. Read <see cref="HuurayApiException.StatusMessage"/>.
/// </summary>
public sealed class HuurayValidationException : HuurayApiException
{
    /// <summary>Creates a validation exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="status">The <c>Status</c> field from the response body, when present.</param>
    /// <param name="statusMessage">The <c>StatusMessage</c> field, or the deprecated <c>Message</c>.</param>
    /// <param name="body">The parsed response body, already redacted.</param>
    /// <param name="method">The HTTP method of the failed request.</param>
    /// <param name="path">The path of the failed request.</param>
    public HuurayValidationException(
        string message,
        int httpStatus,
        int? status,
        string? statusMessage,
        JsonNode? body,
        string method,
        string path)
        : base(message, httpStatus, status, statusMessage, body, method, path)
    {
    }
}

/// <summary>
/// HTTP 5xx — a server-side failure. Safe to repeat only for reads.
/// </summary>
public sealed class HuurayServerException : HuurayApiException
{
    /// <summary>Creates a server exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="status">The <c>Status</c> field from the response body, when present.</param>
    /// <param name="statusMessage">The <c>StatusMessage</c> field, or the deprecated <c>Message</c>.</param>
    /// <param name="body">The parsed response body, already redacted.</param>
    /// <param name="method">The HTTP method of the failed request.</param>
    /// <param name="path">The path of the failed request.</param>
    public HuurayServerException(
        string message,
        int httpStatus,
        int? status,
        string? statusMessage,
        JsonNode? body,
        string method,
        string path)
        : base(message, httpStatus, status, statusMessage, body, method, path)
    {
    }
}
