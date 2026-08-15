using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Huuray;

/// <summary>
/// Client for the Huuray API v4.
/// </summary>
/// <remarks>
/// <para>
/// One instance is cheap to keep for the lifetime of your application, and is safe to
/// use from multiple threads at once.
/// </para>
/// <para>
/// Unless you pass your own <see cref="HttpClient"/>, every instance shares one
/// internally-managed <see cref="HttpClient"/> with connection pooling configured, so
/// creating a client per request does not exhaust sockets. In an application with
/// dependency injection, register an <c>IHttpClientFactory</c> client and pass it in.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// HuurayClient huuray = new(new HuurayClientOptions
/// {
///     ApiToken = Environment.GetEnvironmentVariable("HUURAY_API_TOKEN")!,
///     ApiSecret = Environment.GetEnvironmentVariable("HUURAY_API_SECRET")!,
/// });
///
/// ListBalancesResult balances = await huuray.Balances.ListAsync();
/// </code>
/// </example>
public sealed class HuurayClient
{
    /// <summary>
    /// The production API.
    /// </summary>
    /// <remarks>
    /// The specification declares no <c>servers</c> block, so the host is set here.
    /// Live-confirmed on 2026-08-15.
    /// </remarks>
    public const string DefaultBaseUrl = "https://api.huuray.com";

    private static readonly Lazy<HttpClient> SharedHttpClient =
        new(CreateDefaultHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly string _apiSecret;
    private readonly string _baseUrl;
    private readonly HashEncoding _hashEncoding;
    private readonly TimeSpan _timeout;
    private readonly RetryPolicy _retry;
    private readonly string _userAgent;
    private readonly Func<string> _nonceFactory;

    /// <summary>
    /// Creates a client.
    /// </summary>
    /// <param name="options">Credentials and behaviour.</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> to send with. Leave <see langword="null"/> to use a
    /// shared, pooled instance owned by this library. When you pass one, this client
    /// does not dispose it and does not change its configuration.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="HuurayConfigurationException">
    /// Credentials are missing, or <see cref="HuurayClientOptions.BaseUrl"/> is not an absolute URL.
    /// </exception>
    public HuurayClient(HuurayClientOptions options, HttpClient? httpClient = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrEmpty(options.ApiToken))
        {
            throw new HuurayConfigurationException(
                "ApiToken is required. Pass it explicitly, for example from the HUURAY_API_TOKEN environment variable.");
        }

        if (string.IsNullOrEmpty(options.ApiSecret))
        {
            throw new HuurayConfigurationException(
                "ApiSecret is required. Pass it explicitly, for example from the HUURAY_API_SECRET environment variable.");
        }

        // An unset environment variable arrives as an empty string more often than as
        // null, and silently sending to "" would be a confusing failure.
        string baseUrl = (string.IsNullOrWhiteSpace(options.BaseUrl) ? DefaultBaseUrl : options.BaseUrl)
            .TrimEnd('/');
        // The scheme check is not decoration. UriKind.Absolute alone is
        // platform-dependent: on Windows "/v4" is not absolute, but on Linux and
        // macOS it parses happily as the file URI "file:///v4". Validating only
        // absoluteness therefore accepts a misconfigured BaseUrl on exactly the
        // platforms this SDK is most likely to run on in production, turning a
        // clear construction-time error into a confusing failure at the first
        // request. Requiring http(s) is also simply correct: this client speaks
        // HTTP, so file://, ftp:// and friends are never valid here.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsedBaseUrl)
            || (parsedBaseUrl.Scheme != Uri.UriSchemeHttp && parsedBaseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new HuurayConfigurationException(
                $"BaseUrl \"{options.BaseUrl}\" is not an absolute http(s) URL. Expected something like \"{DefaultBaseUrl}\".");
        }

        _apiToken = options.ApiToken;
        _apiSecret = options.ApiSecret;
        _baseUrl = baseUrl;
        _hashEncoding = options.HashEncoding;
        _timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(30);
        _retry = RetryPolicy.Resolve(options.Retry);
        _nonceFactory = options.NonceFactory ?? RequestSigner.GenerateNonce;
        _userAgent = string.IsNullOrEmpty(options.UserAgent)
            ? SdkUserAgent
            : SdkUserAgent + " " + options.UserAgent;
        _httpClient = httpClient ?? SharedHttpClient.Value;

        Balances = new BalancesResource(this);
        Catalogue = new CatalogueResource(this);
        Templates = new TemplatesResource(this);
        Stock = new StockResource(this);
        ExchangeRates = new ExchangeRatesResource(this);
        Orders = new OrdersResource(this);
    }

    /// <summary>Available balances on your B2B account. <c>GET /v4/Balance</c>.</summary>
    public BalancesResource Balances { get; }

    /// <summary>The products you can order. <c>POST /v4/Catalogue</c>.</summary>
    public CatalogueResource Catalogue { get; }

    /// <summary>The delivery templates on your account. <c>POST /v4/Template</c>.</summary>
    public TemplatesResource Templates { get; }

    /// <summary>Stock for a product. <c>POST /v4/Stock</c>.</summary>
    public StockResource Stock { get; }

    /// <summary>Currency conversion. <c>GET /v4/ExchangeRates</c>.</summary>
    public ExchangeRatesResource ExchangeRates { get; }

    /// <summary>Ordering, searching, resending and cancelling.</summary>
    public OrdersResource Orders { get; }

    /// <summary>The <c>User-Agent</c> this client sends, before any suffix you add.</summary>
    internal static string SdkUserAgent { get; } = "huuray-dotnet/" + SdkVersion();

    /// <summary>
    /// Sends one gift card to one recipient — the common case, in a single call.
    /// </summary>
    /// <param name="request">Product, amount, recipient, template and your reference.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The order's identifier and the reference you sent.</returns>
    /// <remarks>
    /// Performs exactly one <c>POST /v4/Order</c> with <c>Sync: false</c> and
    /// <c>Quantity: 1</c>. Delivery is handled by Huuray using the template you name, so
    /// no voucher codes come back; use <see cref="OrdersResource.SearchAsync"/> to look
    /// the order up later.
    /// <para>
    /// <see cref="SendRewardRequest.RefId"/> is required by this SDK even though the API
    /// treats it as optional: without it there is no way to find out whether an order
    /// landed after a timeout. See <see cref="HuurayIndeterminateOrderException"/>.
    /// </para>
    /// </remarks>
    public Task<CreateOrderResult> SendRewardAsync(
        SendRewardRequest request,
        CancellationToken cancellationToken = default) =>
        Orders.SendRewardAsync(request, cancellationToken);

    /// <summary>
    /// Calls any v4 endpoint with signing handled — the escape hatch for anything the
    /// typed resources do not cover.
    /// </summary>
    /// <param name="method">The HTTP method, for example <see cref="HttpMethod.Post"/>.</param>
    /// <param name="path">The path, for example <c>/v4/Search</c>.</param>
    /// <param name="body">The JSON request body, or <see langword="null"/> to send none.</param>
    /// <param name="retryable">
    /// Whether repeating this call is safe. <strong>Leave this false for anything that
    /// moves value</strong> — Order, Resend and Cancel have no idempotency key.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The parsed response body.</returns>
    /// <remarks>
    /// Request and response shapes are exactly as documented in the Huuray API
    /// reference; this method does no renaming.
    /// <code language="csharp">
    /// JsonNode? found = await huuray.RequestAsync(
    ///     HttpMethod.Post,
    ///     "/v4/Search",
    ///     new JsonObject { ["RefID"] = "payroll-2026-08-jane" });
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    public async Task<JsonNode?> RequestAsync(
        HttpMethod method,
        string path,
        JsonNode? body = null,
        bool retryable = false,
        CancellationToken cancellationToken = default)
    {
        if (method is null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        HuurayResponse<JsonNode> response = await SendAsync<JsonNode>(
                method,
                path,
                body?.ToJsonString(),
                query: null,
                retryable,
                static (string text) => JsonNode.Parse(text),
                cancellationToken)
            .ConfigureAwait(false);

        return response.Data;
    }

    /// <summary>
    /// Signs and sends one request, deserialising the body with a source-generated contract.
    /// </summary>
    internal Task<HuurayResponse<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        string? jsonBody,
        string? query,
        bool retryable,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken) =>
        SendAsync(method, path, jsonBody, query, retryable, text => JsonSerializer.Deserialize(text, typeInfo), cancellationToken);

    /// <summary>
    /// The one place a request is built, signed, sent, retried and mapped onto errors.
    /// </summary>
    /// <remarks>
    /// The <c>parse</c> delegate turns the response text into the result type. It must
    /// throw <see cref="JsonException"/> for text that is not usable JSON — that is how a
    /// truncated or proxied body is detected, and it is why the delegate exists rather
    /// than the deserialiser being called inline.
    /// </remarks>
    private async Task<HuurayResponse<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        string? jsonBody,
        string? query,
        bool retryable,
        Func<string, T?> parse,
        CancellationToken cancellationToken)
    {
        Uri uri = new(_baseUrl + path + (string.IsNullOrEmpty(query) ? string.Empty : "?" + query));
        string verb = method.Method;

        int attempts = retryable ? _retry.MaxRetries : 0;
        Exception? lastError = null;

        for (int attempt = 0; attempt <= attempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(_retry.BackoffDelay(attempt - 1), cancellationToken).ConfigureAwait(false);
            }

            using CancellationTokenSource timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);

            using HttpRequestMessage request = new(method, uri);

            // A fresh nonce every attempt: the API rejects a repeat for 60 days.
            foreach (KeyValuePair<string, string> header in RequestSigner.BuildAuthHeaders(
                         _apiToken, _apiSecret, _nonceFactory(), _hashEncoding))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

            if (jsonBody is not null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            int httpStatus;
            string text;
            HttpResponseMessage? response = null;
            try
            {
                // HttpClient resolves as soon as the headers arrive; the body streams
                // afterwards under the same cancellation. BOTH awaits sit inside this
                // try, because a mid-body failure escaping raw would bypass every
                // downstream check — including the one that wraps order failures in
                // HuurayIndeterminateOrderException.
                response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                    .ConfigureAwait(false);

                httpStatus = (int)response.StatusCode;
                text = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Cancellation the caller asked for is theirs, not a Huuray failure.
                cancellationToken.ThrowIfCancellationRequested();

                HuurayConnectionException error = exception is OperationCanceledException
                    ? new HuurayTimeoutException(verb, path, _timeout, exception)
                    : new HuurayConnectionException(
                        $"{verb} {path} failed to reach the Huuray API: {exception.Message}",
                        verb,
                        path,
                        exception);

                lastError = error;
                if (attempt < attempts)
                {
                    continue;
                }

                throw error;
            }
            finally
            {
                response?.Dispose();
            }

            if (httpStatus is >= 200 and <= 299)
            {
                T? data;
                try
                {
                    data = parse(text);
                }
                catch (JsonException exception)
                {
                    // Every documented 2xx carries a JSON body. An empty or unparseable
                    // body on a success status is a transport-level fault (proxy
                    // interference, truncation) — NOT an empty result. Coercing it to an
                    // empty result would make Orders.SearchAsync report "order absent"
                    // after a garbled response, and the documented reconciliation flow
                    // would then order a second time. The body itself is never included
                    // in the message: it could hold voucher codes.
                    HuurayConnectionException error = new(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} {1} returned HTTP {2} but the body was {3} ({4} bytes). " +
                            "Treat the outcome as unknown rather than empty.",
                            verb,
                            path,
                            httpStatus,
                            text.Length == 0 ? "empty" : "not usable JSON",
                            text.Length),
                        verb,
                        path,
                        exception);

                    lastError = error;
                    if (attempt < attempts)
                    {
                        continue;
                    }

                    throw error;
                }

                return new HuurayResponse<T>(data, httpStatus);
            }

            HuurayApiException apiError = HuurayApiException.Create(httpStatus, TryParseJson(text), verb, path);
            lastError = apiError;
            if (attempt < attempts && RetryPolicy.IsRetryableStatus(httpStatus))
            {
                continue;
            }

            throw apiError;
        }

        // Unreachable: the loop always returns or throws.
        throw lastError ?? new HuurayConnectionException($"{verb} {path} produced no response.", verb, path);
    }

    /// <summary>Builds a query string, dropping every parameter that was not supplied.</summary>
    internal static string? BuildQuery(params KeyValuePair<string, string?>[] parameters)
    {
        StringBuilder builder = new();
        foreach (KeyValuePair<string, string?> parameter in parameters)
        {
            if (parameter.Value is null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(parameter.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(parameter.Value));
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static JsonNode? TryParseJson(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        if (SocketsHttpHandler.IsSupported)
        {
            SocketsHttpHandler handler = new()
            {
                // Recycle pooled connections so DNS changes are picked up, without
                // paying a new TLS handshake per request.
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            };

            return new HttpClient(handler, disposeHandler: true)
            {
                // Timeouts are governed per request by this client, so the transport's
                // own timeout must not fire first and produce a less specific error.
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
        }

        return new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    private static string SdkVersion()
    {
        string? informational = typeof(HuurayClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return "0.0.0";
        }

        int plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
