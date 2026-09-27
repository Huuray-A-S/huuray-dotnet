using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Huuray;

/// <summary>
/// Parameters for <c>POST /v4/Pdf</c>: the order, and optionally one voucher, to get the gift
/// card PDF of.
/// </summary>
public sealed record GetPdfRequest
{
    /// <summary>
    /// The order's unique identifier: <c>OrderUid</c> from <c>Orders.CreateAsync</c>,
    /// <c>Orders.CreateSyncAsync</c> or <c>Orders.SearchAsync</c>.
    /// </summary>
    public required string OrderUid { get; init; }

    /// <summary>
    /// One voucher on the order. Leave unset for every voucher on the order; cancelled vouchers
    /// are left out.
    /// </summary>
    public int? VoucherId { get; init; }

    /// <summary>
    /// A PDF template uid from <c>Templates.ListAsync()</c>. Leave unset for the PDF template the
    /// order's delivery email was sent with (<see cref="CreateOrderRequest.PdfTemplateUid"/>).
    /// </summary>
    /// <remarks>
    /// Without one, an order placed without a PDF template is rejected with a 422, thrown as
    /// <see cref="HuurayValidationException"/>, as is a template that is not available for the
    /// ordered product's brand and country. This client checks neither.
    /// </remarks>
    public string? PdfTemplateUid { get; init; }

    /// <summary>
    /// <see langword="true"/> for one PDF holding every selected voucher. Leave unset (or
    /// <see langword="false"/>) for one PDF per voucher.
    /// </summary>
    public bool? Combine { get; init; }
}

/// <summary>
/// The result of <c>POST /v4/Pdf</c>.
/// </summary>
/// <param name="Ready">
/// <see langword="true"/> when the API answered <c>200</c> with the documents.
/// <see langword="false"/> when it answered <c>202 Accepted</c>: the order is still being
/// processed, or a supplier has not delivered a code yet, and <paramref name="Documents"/> is
/// empty. Ask again after <paramref name="RetryAfter"/>, or use <c>Pdfs.GetWhenReadyAsync</c>.
/// </param>
/// <param name="OrderUid">The order's unique identifier.</param>
/// <param name="Documents">The PDFs: one per voucher, or a single one when <c>Combine</c> was set.</param>
/// <param name="RetryAfter">
/// The response's <c>Retry-After</c> header in whole seconds, or <see langword="null"/> when it
/// was absent or not whole seconds: an HTTP-date, a negative value and a fraction all read as
/// <see langword="null"/>. The API sends it with <c>202</c>.
/// </param>
public sealed record PdfResult(
    bool Ready,
    string? OrderUid,
    IReadOnlyList<PdfDocument> Documents,
    TimeSpan? RetryAfter);

/// <summary>
/// One gift card PDF.
/// </summary>
/// <param name="VoucherIds">The vouchers in the document: one, or every selected voucher in a combined document.</param>
/// <param name="PdfTemplateUid">
/// The PDF template the document was built from; <see langword="null"/> for a combined document
/// built from several templates.
/// </param>
/// <param name="FileName">A suggested file name, for example <c>giftcard-5123401.pdf</c>.</param>
/// <param name="ContentType">The document's media type, <c>application/pdf</c>.</param>
/// <param name="Content">The PDF itself, decoded from the API's base64.</param>
/// <remarks>
/// <see cref="Content"/> is a <strong>bearer instrument</strong>: the document carries the
/// redeemable code and, depending on the template, the CVV and QR codes, so whoever holds it can
/// spend the gift card. Never log it, and keep it no longer than you need it.
/// <see cref="ToString"/> prints its length, never its bytes.
/// </remarks>
public sealed record PdfDocument(
    IReadOnlyList<int> VoucherIds,
    string? PdfTemplateUid,
    string? FileName,
    string? ContentType,
    byte[]? Content)
{
    /// <summary>
    /// Writes the members for the record's <c>ToString</c>: <see cref="Content"/> as its length,
    /// never its bytes.
    /// </summary>
    /// <remarks>
    /// Every other member is printed as the compiler would print it. <see cref="FileName"/> is
    /// not masked: the API builds it from the voucher or order identifier, and Huuray suggests
    /// logging the voucher ids, the file name and the size in place of the document.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("VoucherIds = ");
        builder.Append(VoucherIds);
        builder.Append(", PdfTemplateUid = ");
        builder.Append(PdfTemplateUid);
        builder.Append(", FileName = ");
        builder.Append(FileName);
        builder.Append(", ContentType = ");
        builder.Append(ContentType);
        builder.Append(", Content = ");
        if (Content is not null)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "[{0} bytes]", Content.Length));
        }

        return true;
    }
}
