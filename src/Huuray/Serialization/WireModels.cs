using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Huuray.Serialization;

/*
 * The wire.
 *
 * These types are a literal transcription of `openapi/huuray-v4.json`: every member
 * carries the specification's own property name, spelling and casing. Nothing here is
 * invented, and nothing is renamed — the public result records do the renaming, once,
 * where a reader can see both sides.
 *
 * Request types are serialised with DefaultIgnoreCondition = WhenWritingNull, so an
 * unset optional field is omitted from the body rather than sent as null.
 */

/// <summary>Request body of <c>POST /v4/Catalogue</c>.</summary>
internal sealed class CatalogueRequestWire
{
    [JsonPropertyName("All")]
    public bool All { get; set; }
}

/// <summary>Request body of <c>POST /v4/Stock</c>.</summary>
internal sealed class StockRequestWire
{
    [JsonPropertyName("ProductToken")]
    public string ProductToken { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public int? Value { get; set; }
}

/// <summary>The <c>Product</c> member of an order request.</summary>
internal sealed class OrderProductWire
{
    [JsonPropertyName("Token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public int Value { get; set; }

    [JsonPropertyName("Currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("Quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("Expires")]
    public string? Expires { get; set; }
}

/// <summary>One entry in the <c>Recipients</c> array of an order request.</summary>
internal sealed class OrderRecipientWire
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    [JsonPropertyName("Phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("RefID")]
    public string? RefID { get; set; }
}

/// <summary>Request body of <c>POST /v4/Order</c>.</summary>
internal sealed class OrderRequestWire
{
    [JsonPropertyName("Product")]
    public OrderProductWire Product { get; set; } = new();

    [JsonPropertyName("Sync")]
    public bool Sync { get; set; }

    [JsonPropertyName("RefID")]
    public string? RefID { get; set; }

    [JsonPropertyName("DeliveryTemplateId")]
    public int? DeliveryTemplateId { get; set; }

    [JsonPropertyName("DeliveryDatetime")]
    public string? DeliveryDatetime { get; set; }

    [JsonPropertyName("PersonalMessage")]
    public string? PersonalMessage { get; set; }

    [JsonPropertyName("Recipients")]
    public List<OrderRecipientWire>? Recipients { get; set; }
}

/// <summary>Request body of <c>POST /v4/Search</c>.</summary>
internal sealed class SearchRequestWire
{
    [JsonPropertyName("OrderUID")]
    public string? OrderUID { get; set; }

    [JsonPropertyName("VoucherID")]
    public int? VoucherID { get; set; }

    [JsonPropertyName("ProductToken")]
    public string? ProductToken { get; set; }

    [JsonPropertyName("RefID")]
    public string? RefID { get; set; }

    [JsonPropertyName("SMSTemplateID")]
    public int? SMSTemplateID { get; set; }

    [JsonPropertyName("EmailTemplateID")]
    public int? EmailTemplateID { get; set; }

    [JsonPropertyName("DeliveryDatetime")]
    public string? DeliveryDatetime { get; set; }

    [JsonPropertyName("RecipientName")]
    public string? RecipientName { get; set; }

    [JsonPropertyName("RecipientEmail")]
    public string? RecipientEmail { get; set; }

    [JsonPropertyName("RecipientPhone")]
    public string? RecipientPhone { get; set; }

    [JsonPropertyName("RecipientRefID")]
    public string? RecipientRefID { get; set; }
}

/// <summary>Request body of <c>POST /v4/Resend</c>.</summary>
internal sealed class ResendRequestWire
{
    [JsonPropertyName("OrderUID")]
    public string OrderUID { get; set; } = string.Empty;

    [JsonPropertyName("VoucherID")]
    public int? VoucherID { get; set; }
}

/// <summary>Request body of <c>DELETE /v4/Cancel</c>.</summary>
internal sealed class CancelRequestWire
{
    [JsonPropertyName("OrderUID")]
    public string OrderUID { get; set; } = string.Empty;

    [JsonPropertyName("VoucherID")]
    public int? VoucherID { get; set; }
}

/* ------------------------------------------------------------------ responses */

internal sealed class BalanceItemWire
{
    [JsonPropertyName("Currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("Balance")]
    public long Balance { get; set; }

    [JsonPropertyName("Master")]
    public bool Master { get; set; }
}

internal sealed class BalanceResponseWire
{
    [JsonPropertyName("Balances")]
    public List<BalanceItemWire>? Balances { get; set; }
}

internal sealed class CatalogueProductWire
{
    [JsonPropertyName("ProductToken")]
    public string? ProductToken { get; set; }

    [JsonPropertyName("BrandName")]
    public string? BrandName { get; set; }

    [JsonPropertyName("Country")]
    public string? Country { get; set; }

    [JsonPropertyName("CountryCode")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("Discount")]
    public decimal? Discount { get; set; }

    [JsonPropertyName("Denominations")]
    public string? Denominations { get; set; }

    [JsonPropertyName("Currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("RealTimeStock")]
    public string? RealTimeStock { get; set; }

    [JsonPropertyName("Categories")]
    public string? Categories { get; set; }

    [JsonPropertyName("LanguageCode")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("Active")]
    public bool Active { get; set; }

    [JsonPropertyName("BrandDescription")]
    public string? BrandDescription { get; set; }

    [JsonPropertyName("RedemptionInstructions")]
    public string? RedemptionInstructions { get; set; }

    [JsonPropertyName("LogoFile")]
    public string? LogoFile { get; set; }
}

internal sealed class CatalogueResponseWire
{
    [JsonPropertyName("Products")]
    public List<CatalogueProductWire>? Products { get; set; }
}

internal sealed class TemplateItemWire
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("Language")]
    public string? Language { get; set; }

    [JsonPropertyName("Sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("Subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("FormattedText")]
    public string? FormattedText { get; set; }

    [JsonPropertyName("PlainText")]
    public string? PlainText { get; set; }
}

internal sealed class TemplateResponseWire
{
    [JsonPropertyName("Templates")]
    public List<TemplateItemWire>? Templates { get; set; }
}

internal sealed class StockResponseWire
{
    [JsonPropertyName("Stock")]
    public int? Stock { get; set; }
}

internal sealed class ExchangeRatesResponseWire
{
    [JsonPropertyName("ExchangeRate")]
    public double? ExchangeRate { get; set; }

    [JsonPropertyName("Spread")]
    public int? Spread { get; set; }
}

/// <summary>
/// Mirrors <c>OrderRecipient</c> and <c>SearchRecipient</c>, which the specification
/// declares separately with identical members.
/// </summary>
internal sealed class RecipientWire
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    [JsonPropertyName("Phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("RefID")]
    public string? RefID { get; set; }
}

/// <summary>
/// Mirrors <c>OrderVoucher</c> and <c>SearchVoucher</c>, which the specification
/// declares separately with identical members.
/// </summary>
internal sealed class VoucherWire
{
    [JsonPropertyName("ID")]
    public int? ID { get; set; }

    [JsonPropertyName("Code")]
    public string? Code { get; set; }

    [JsonPropertyName("CVV")]
    public string? CVV { get; set; }

    [JsonPropertyName("RedeemLink")]
    public string? RedeemLink { get; set; }

    /// <summary>Kept as text: the API's own formatting is passed through, never re-guessed.</summary>
    [JsonPropertyName("Expires")]
    public string? Expires { get; set; }

    [JsonPropertyName("Recipient")]
    public RecipientWire? Recipient { get; set; }
}

/// <summary>
/// Mirrors <c>OrderResponse</c> and <c>SearchResponse</c>, which the specification
/// declares separately with identical members.
/// </summary>
internal sealed class OrderResponseWire
{
    [JsonPropertyName("OrderUID")]
    public string? OrderUID { get; set; }

    [JsonPropertyName("RefID")]
    public string? RefID { get; set; }

    [JsonPropertyName("Vouchers")]
    public List<VoucherWire>? Vouchers { get; set; }
}

internal sealed class ResendResponseWire
{
    [JsonPropertyName("NumberOfResends")]
    public int? NumberOfResends { get; set; }
}

internal sealed class CancelVoucherWire
{
    [JsonPropertyName("ID")]
    public int ID { get; set; }

    [JsonPropertyName("Cancelled")]
    public bool Cancelled { get; set; }
}

internal sealed class CancelResponseWire
{
    [JsonPropertyName("OrderUID")]
    public string? OrderUID { get; set; }

    [JsonPropertyName("OrderCancelled")]
    public bool OrderCancelled { get; set; }

    [JsonPropertyName("Vouchers")]
    public List<CancelVoucherWire>? Vouchers { get; set; }
}
