using System.Net.Http.Headers;

namespace Huuray;

/// <summary>
/// A parsed response plus the HTTP status, which some endpoints use semantically, and the
/// <c>Retry-After</c> header.
/// </summary>
/// <remarks>
/// Resource methods need the status because <c>206 Partial Content</c> on Cancel and
/// Resend is a real outcome rather than a flavour of success, and <c>202 Accepted</c> on
/// Pdf means "not ready yet", with <c>Retry-After</c> saying when to ask again.
/// </remarks>
/// <typeparam name="T">The deserialised body type.</typeparam>
/// <param name="Data">The deserialised body, or <see langword="null"/> if the body was the JSON literal <c>null</c>.</param>
/// <param name="HttpStatus">The HTTP status of the response.</param>
/// <param name="RetryAfter">The parsed <c>Retry-After</c> header, or <see langword="null"/> when absent or unparseable.</param>
internal sealed record HuurayResponse<T>(T? Data, int HttpStatus, RetryConditionHeaderValue? RetryAfter);
