using System.Collections.Generic;

namespace Huuray;

/// <summary>
/// A delivery template — the email or SMS your recipients receive.
/// </summary>
/// <param name="Id">Pass this as <c>TemplateId</c> when ordering.</param>
/// <param name="Name">The template's name on your account.</param>
/// <param name="Type">Template type, for example email or SMS, as named by the API.</param>
/// <param name="Language">ISO alpha-2 language code.</param>
/// <param name="Sender">The sender recipients will see.</param>
/// <param name="Subject">Subject line, for email templates.</param>
/// <param name="FormattedText">Template body including HTML.</param>
/// <param name="PlainText">Template body as plain text.</param>
public sealed record TemplateItem(
    int Id,
    string? Name,
    string? Type,
    string? Language,
    string? Sender,
    string? Subject,
    string? FormattedText,
    string? PlainText);

/// <summary>
/// A PDF template — delivers the codes as a document attached to an email.
/// </summary>
/// <param name="Uid">Unique PDF template identifier. Pass this as <c>PdfTemplateUid</c> when ordering.</param>
/// <param name="Name">The PDF template's name.</param>
/// <param name="Type">PDF template type, as named by the API.</param>
/// <param name="Language">ISO alpha-2 language code.</param>
/// <param name="Country">
/// The country the template can be used for, or <see langword="null"/> if it can be used for any country.
/// </param>
/// <param name="BrandName">
/// The brand the template can be used for, or <see langword="null"/> if it can be used for any brand.
/// </param>
public sealed record PdfTemplate(
    string? Uid,
    string? Name,
    string? Type,
    string? Language,
    string? Country,
    string? BrandName);

/// <summary>
/// The result of <c>POST /v4/Template</c>.
/// </summary>
/// <param name="Templates">The delivery templates available to your account.</param>
/// <param name="PdfTemplates">
/// The PDF templates available to your account, used to deliver the codes as a document
/// attached to an email. Empty when the API returns none.
/// </param>
public sealed record ListTemplatesResult(
    IReadOnlyList<TemplateItem> Templates,
    IReadOnlyList<PdfTemplate> PdfTemplates);
