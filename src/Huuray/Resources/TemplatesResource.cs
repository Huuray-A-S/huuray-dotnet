using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;

namespace Huuray;

/// <summary>
/// Delivery templates — the emails and texts your recipients receive.
/// </summary>
public sealed class TemplatesResource
{
    private readonly HuurayClient _client;

    internal TemplatesResource(HuurayClient client) => _client = client;

    /// <summary>
    /// Lists the delivery templates and PDF templates available to your account.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The delivery templates and the PDF templates on the account.</returns>
    /// <remarks>
    /// <c>POST /v4/Template</c>. The endpoint declares no request body in the API
    /// specification, so this client sends none — confirmed accepted by the live API.
    /// <para>
    /// When the account has <strong>no active templates</strong>, the API answers
    /// <c>404 "There were no active templates"</c> rather than an empty list, so this
    /// method throws <see cref="HuurayNotFoundException"/> in that case. Catch it and
    /// read it as "no templates exist".
    /// </para>
    /// </remarks>
    /// <exception cref="HuurayNotFoundException">The account has no active templates.</exception>
    /// <exception cref="HuurayApiException">The API returned another non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<ListTemplatesResult> ListAsync(CancellationToken cancellationToken = default)
    {
        HuurayResponse<TemplateResponseWire> response = await _client.SendAsync(
                HttpMethod.Post,
                "/v4/Template",
                jsonBody: null,
                query: null,
                retryable: true,
                HuurayJsonContext.Default.TemplateResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        List<TemplateItemWire> items = response.Data?.Templates ?? new List<TemplateItemWire>();
        Template[] templates = new Template[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            TemplateItemWire t = items[i];
            templates[i] = new Template(
                t.Id,
                t.Name,
                t.Type,
                t.Language,
                t.Sender,
                t.Subject,
                t.FormattedText,
                t.PlainText);
        }

        List<PDFTemplateItemWire> pdfItems = response.Data?.PDFTemplates ?? new List<PDFTemplateItemWire>();
        PdfTemplate[] pdfTemplates = new PdfTemplate[pdfItems.Count];
        for (int i = 0; i < pdfItems.Count; i++)
        {
            PDFTemplateItemWire p = pdfItems[i];
            pdfTemplates[i] = new PdfTemplate(
                p.Uid,
                p.Name,
                p.Type,
                p.Language,
                p.Country,
                p.BrandName);
        }

        return new ListTemplatesResult(templates, pdfTemplates);
    }
}
