using System;
using System.Globalization;
using System.Text;

namespace Huuray;

/// <summary>
/// Parameters for <c>POST /v4/Upload</c>: one purchase order file.
/// </summary>
/// <remarks>
/// The file is held in memory and sent as the <c>File</c> part of a <c>multipart/form-data</c>
/// body. Which files the API accepts, and how large, is the API's decision; this client
/// does not check either.
/// </remarks>
public sealed record CreateUploadRequest
{
    /// <summary>The file's contents, for example from <c>File.ReadAllBytesAsync</c>.</summary>
    public required ReadOnlyMemory<byte> File { get; init; }

    /// <summary>
    /// The file's name, for example <c>purchase-order-4711.pdf</c>. Sent as the part's file name.
    /// </summary>
    /// <remarks>
    /// Must not be empty or only whitespace, and must not contain a double quote or a control
    /// character, which cannot be sent in the part's header. Personal data, so
    /// <see cref="ToString"/> masks it.
    /// </remarks>
    public required string FileName { get; init; }

    /// <summary>
    /// The file's media type, for example <c>application/pdf</c>. Leave unset (or empty) to send
    /// <c>application/octet-stream</c>.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Writes the members for the record's <c>ToString</c>: the file as its length, never its
    /// bytes, and <see cref="FileName"/> masked.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("File = ");
        builder.Append(string.Format(CultureInfo.InvariantCulture, "[{0} bytes]", File.Length));
        builder.Append(", FileName = ");
        builder.Append(Redaction.MaskMember(FileName));
        builder.Append(", ContentType = ");
        builder.Append(ContentType);
        return true;
    }
}

/// <summary>
/// The result of <c>POST /v4/Upload</c>.
/// </summary>
/// <param name="Token">
/// The token identifying the uploaded file. Pass it as
/// <see cref="CreateOrderRequest.PurchaseOrderFileToken"/>; the order it is used with consumes it.
/// </param>
/// <param name="FileName">The file name the upload was stored with.</param>
/// <param name="ContentType">The content type the file was recognized as.</param>
/// <param name="Size">The size of the uploaded file in bytes.</param>
public sealed record UploadResult(string? Token, string? FileName, string? ContentType, long? Size)
{
    /// <summary>
    /// Writes the members for the record's <c>ToString</c>, with <see cref="FileName"/> masked.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Token = ");
        builder.Append(Token);
        builder.Append(", FileName = ");
        builder.Append(Redaction.MaskMember(FileName));
        builder.Append(", ContentType = ");
        builder.Append(ContentType);
        builder.Append(", Size = ");
        builder.Append(Size.ToString());
        return true;
    }
}
