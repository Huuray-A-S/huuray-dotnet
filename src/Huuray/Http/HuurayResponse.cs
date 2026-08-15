namespace Huuray;

/// <summary>
/// A parsed response plus the HTTP status, which some endpoints use semantically.
/// </summary>
/// <remarks>
/// Resource methods need the status because <c>206 Partial Content</c> on Cancel and
/// Resend is a real outcome rather than a flavour of success.
/// </remarks>
/// <typeparam name="T">The deserialised body type.</typeparam>
/// <param name="Data">The deserialised body, or <see langword="null"/> if the body was the JSON literal <c>null</c>.</param>
/// <param name="HttpStatus">The HTTP status of the response.</param>
internal sealed record HuurayResponse<T>(T? Data, int HttpStatus);
