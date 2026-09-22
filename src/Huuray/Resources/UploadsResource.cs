using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;

namespace Huuray;

/// <summary>
/// Purchase order files, uploaded before the order they belong to.
/// </summary>
/// <remarks>
/// <strong>An upload is never retried.</strong> Every <c>POST /v4/Upload</c> stores a new
/// file that holds one of the account's pending upload slots until an order uses its
/// token, and no endpoint lists uploads — so a repeated upload can leave one behind that
/// you have no token for.
/// </remarks>
public sealed class UploadsResource
{
    /// <summary>What a failed upload's message adds: the file may be stored without a token to show for it.</summary>
    internal const string MayBeStoredNote =
        "The upload may still have been stored, and may hold a pending upload slot until it is used or cleaned up. " +
        "Uploads are never retried automatically.";

    /// <summary>The one part the specification declares.</summary>
    private const string FilePartName = "File";

    private const string DefaultContentType = "application/octet-stream";

    private readonly HuurayClient _client;

    internal UploadsResource(HuurayClient client) => _client = client;

    /// <summary>
    /// Uploads a purchase order file, for an order to attach to its invoice.
    /// </summary>
    /// <param name="request">The file's bytes, its name and, optionally, its media type.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The token to order with, and the name, type and size the file was stored with.</returns>
    /// <remarks>
    /// <c>POST /v4/Upload</c>, as <c>multipart/form-data</c> with one part, <c>File</c>. Pass
    /// <see cref="UploadResult.Token"/> as <see cref="CreateOrderRequest.PurchaseOrderFileToken"/>
    /// or <see cref="SendRewardRequest.PurchaseOrderFileToken"/>.
    /// <para>
    /// <strong>Not retried on failure.</strong> A timeout or a dropped connection throws the
    /// ordinary <see cref="HuurayTimeoutException"/> or <see cref="HuurayConnectionException"/>,
    /// whose message says the upload may still have been stored.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <see cref="CreateUploadRequest.FileName"/> is empty, only whitespace, or holds a double quote or
    /// a control character, or <see cref="CreateUploadRequest.ContentType"/> is not a media type.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was already cancelled, so nothing was sent.
    /// </exception>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<UploadResult> CreateAsync(
        CreateUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Checked here and never quoted. HttpClient refuses a line break or a double quote
        // only while building the request, with a message that repeats the file name, and
        // writes a NUL or any other control character into the part header as it is.
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException(
                "FileName is required, for example \"purchase-order-4711.pdf\".",
                nameof(request));
        }

        if (request.FileName.AsSpan().ContainsAnyInRange('\0', '\u001f')
            || request.FileName.AsSpan().ContainsAny('"', '\u007f'))
        {
            throw new ArgumentException(
                "FileName contains a double quote or a control character (a line break, tab, NUL or similar) " +
                "and cannot be sent in the File part's header.",
                nameof(request));
        }

        string contentType = string.IsNullOrEmpty(request.ContentType) ? DefaultContentType : request.ContentType;
        if (!MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            throw new ArgumentException(
                "ContentType is not a media type. Expected something like \"application/pdf\", or leave it unset.",
                nameof(request));
        }

        ReadOnlyMemory<byte> file = request.File;
        string fileName = request.FileName;

        HuurayResponse<UploadResponseWire> response = await _client.SendAsync(
                HttpMethod.Post,
                "/v4/Upload",
                () => BuildContent(file, fileName, contentType),
                retryable: false,
                MayBeStoredNote,
                HuurayJsonContext.Default.UploadResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        return new UploadResult(
            response.Data?.Token,
            response.Data?.FileName,
            response.Data?.ContentType,
            response.Data?.Size);
    }

    /// <summary>
    /// The body: one <c>File</c> part, with its file name and media type. MultipartFormDataContent
    /// writes the boundary into the request's Content-Type itself.
    /// </summary>
    private static MultipartFormDataContent BuildContent(ReadOnlyMemory<byte> file, string fileName, string contentType)
    {
        ReadOnlyMemoryContent part = new(file);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        MultipartFormDataContent content = new();
        content.Add(part, FilePartName, fileName);
        return content;
    }
}
