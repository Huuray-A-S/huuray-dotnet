using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;

namespace Huuray;

/// <summary>
/// The gift card PDFs of previous orders.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A PDF is a bearer instrument.</strong> It carries the redeemable code and, depending
/// on the template, the CVV and QR codes: whoever holds the file can spend the gift card. Never
/// log <see cref="PdfDocument.Content"/>, and keep it no longer than you need it.
/// </para>
/// <para>
/// <c>POST /v4/Pdf</c> is read-only, so it is retried like the other reads. The API token needs
/// the Search permission, and the API serves only orders with at most three receivers,
/// answering 422 for larger ones. This client checks neither.
/// </para>
/// </remarks>
public sealed class PdfsResource
{
    /// <summary>How long <see cref="GetWhenReadyAsync"/> keeps asking when no maxWait is given: 10 minutes.</summary>
    public static TimeSpan DefaultMaxWait { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The wait after a <c>202</c> that carried no usable <c>Retry-After</c> header.</summary>
    internal static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The shortest wait between two requests, so a <c>Retry-After: 0</c> never sets off
    /// back-to-back signed requests.
    /// </summary>
    internal static readonly TimeSpan MinRetryAfter = TimeSpan.FromSeconds(1);

    private const string PdfPath = "/v4/Pdf";

    /// <summary>The longest wait <see cref="Task.Delay(TimeSpan)"/> accepts.</summary>
    private static readonly TimeSpan MaxWaitLimit = TimeSpan.FromMilliseconds(4_294_967_294);

    private readonly HuurayClient _client;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    internal PdfsResource(HuurayClient client)
        : this(client, TimeProvider.System, Task.Delay)
    {
    }

    /// <summary>
    /// Creates the resource with its own clock and wait, which tests replace so that no test sleeps.
    /// </summary>
    internal PdfsResource(HuurayClient client, TimeProvider time, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _client = client;
        _time = time;
        _delay = delay;
    }

    /// <summary>
    /// Gets the gift card PDF of an order, or of one voucher on it — once.
    /// </summary>
    /// <param name="request">The order, and optionally one voucher, a PDF template and whether to combine.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The documents, or <see cref="PdfResult.Ready"/> <see langword="false"/> and no documents
    /// when the API answered <c>202</c> because the order is not ready yet.
    /// </returns>
    /// <remarks>
    /// <c>POST /v4/Pdf</c>. A <c>202</c> is not an error and not success: ask again after
    /// <see cref="PdfResult.RetryAfter"/>, or let <see cref="GetWhenReadyAsync"/> do the asking.
    /// <para>
    /// Retried on connection failures and on 408, 425, 429, 500, 502, 503 and 504, with a fresh
    /// nonce each time. A PDF can run to several megabytes and take longer than other calls:
    /// Huuray suggests a <see cref="HuurayClientOptions.Timeout"/> of 100 seconds for it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was already cancelled, so nothing was sent.
    /// </exception>
    /// <exception cref="HuurayNotFoundException">The order or voucher was not found, or is cancelled.</exception>
    /// <exception cref="HuurayValidationException">The API rejected the request, for example an order with more than three receivers.</exception>
    /// <exception cref="HuurayApiException">The API returned another non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">
    /// The request never completed, or the response was unusable — including a document whose
    /// content is not valid base64.
    /// </exception>
    public async Task<PdfResult> GetAsync(
        GetPdfRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        (PdfResult result, _) = await FetchAsync(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Gets the gift card PDF of an order, or of one voucher on it, asking again while the API
    /// answers <c>202</c>.
    /// </summary>
    /// <param name="request">The order, and optionally one voucher, a PDF template and whether to combine.</param>
    /// <param name="maxWait">
    /// How long to keep asking, from 0 to 4294967294 milliseconds. Defaults to
    /// <see cref="DefaultMaxWait"/>, 10 minutes.
    /// </param>
    /// <param name="cancellationToken">Cancels the call, including a wait between two requests.</param>
    /// <returns>The documents; <see cref="PdfResult.Ready"/> is always <see langword="true"/>.</returns>
    /// <remarks>
    /// Calls <see cref="GetAsync"/> and returns as soon as the API answers <c>200</c>. After a
    /// <c>202</c> it waits <see cref="PdfResult.RetryAfter"/>, or 30 seconds when the API sent none,
    /// but never less than 1 second, and asks again with a new signed request. It gives up before
    /// a wait would pass <paramref name="maxWait"/>. Any other 2xx is treated like 202; any other
    /// answer ends the wait at once, as an exception.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWait"/> is negative or above 4294967294 milliseconds.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="HuurayTimeoutException">
    /// The PDF was still not ready when the next wait would have passed <paramref name="maxWait"/>. The
    /// message ends with the API's last <c>StatusMessage</c>.
    /// </exception>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response; see <see cref="GetAsync"/>.</exception>
    /// <exception cref="HuurayConnectionException">A request never completed, or its response was unusable.</exception>
    public async Task<PdfResult> GetWhenReadyAsync(
        GetPdfRequest request,
        TimeSpan? maxWait = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Task.Delay throws above 4294967294 ms, and every wait that goes ahead is shorter
        // than maxWait, so this limit keeps each one within range.
        TimeSpan budget = maxWait ?? DefaultMaxWait;
        if (budget < TimeSpan.Zero || budget > MaxWaitLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxWait),
                "maxWait must be between 0 and 4294967294 milliseconds (about 49.7 days).");
        }

        long started = _time.GetTimestamp();
        while (true)
        {
            (PdfResult result, string? statusMessage) = await FetchAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Ready)
            {
                return result;
            }

            TimeSpan wait = result.RetryAfter ?? DefaultRetryAfter;
            if (wait < MinRetryAfter)
            {
                wait = MinRetryAfter;
            }

            if (wait > budget - _time.GetElapsedTime(started))
            {
                throw new HuurayTimeoutException(
                    GaveUpMessage(budget, wait, statusMessage),
                    HttpMethod.Post.Method,
                    PdfPath,
                    budget);
            }

            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /* ---------------------------------------------------------------- private */

    /// <summary>
    /// One signed request, mapped onto the result; the <c>StatusMessage</c> comes back beside it
    /// for a wait that gives up, rather than as a member of the result.
    /// </summary>
    private async Task<(PdfResult Result, string? StatusMessage)> FetchAsync(
        GetPdfRequest request,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(
            new PdfRequestWire
            {
                OrderUID = request.OrderUid,
                VoucherID = request.VoucherId,
                PDFTemplateUid = request.PdfTemplateUid,
                Combine = request.Combine,
            },
            HuurayJsonContext.Default.PdfRequestWire);

        HuurayResponse<PdfResponseWire> response = await _client.SendAsync(
                HttpMethod.Post,
                PdfPath,
                body,
                query: null,
                retryable: true,
                HuurayJsonContext.Default.PdfResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        List<PdfDocument> documents = new();
        foreach (PdfDocumentWire? item in response.Data?.Documents ?? new List<PdfDocumentWire>())
        {
            // The specification declares no null entries; one would carry nothing to return.
            if (item is null)
            {
                continue;
            }

            documents.Add(new PdfDocument(
                item.VoucherIDs?.ToArray() ?? Array.Empty<int>(),
                item.PDFTemplateUid,
                item.FileName,
                item.ContentType,
                item.Content));
        }

        // Retry-After in whole seconds only. An HTTP-date reads as null like any other value
        // that is not whole seconds; a negative value or a fraction never parses at all.
        PdfResult result = new(
            response.HttpStatus == 200,
            response.Data?.OrderUID,
            documents,
            response.RetryAfter?.Delta);

        return (result, response.Data?.StatusMessage);
    }

    /// <summary>
    /// Says that the wait gave up within <paramref name="maxWait"/>, never that
    /// <paramref name="maxWait"/> passed: it gives up before a wait would pass it, often much sooner.
    /// </summary>
    private static string GaveUpMessage(TimeSpan maxWait, TimeSpan wait, string? statusMessage) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} gave up waiting for the gift card PDF within maxWait ({2:0} ms). The gift card PDF " +
            "was still not ready, and waiting another {3:0} {4} would pass maxWait.{5}",
            HttpMethod.Post.Method,
            PdfPath,
            maxWait.TotalMilliseconds,
            wait.TotalSeconds,
            wait == TimeSpan.FromSeconds(1) ? "second" : "seconds",
            statusMessage is null ? string.Empty : " Last status: " + statusMessage);
}
