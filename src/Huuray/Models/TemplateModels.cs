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
/// The result of <c>POST /v4/Template</c>.
/// </summary>
/// <param name="Templates">The delivery templates available to your account.</param>
public sealed record ListTemplatesResult(IReadOnlyList<TemplateItem> Templates);
