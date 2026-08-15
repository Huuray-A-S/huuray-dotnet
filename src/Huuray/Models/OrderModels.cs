using System;
using System.Collections.Generic;
using System.Globalization;

namespace Huuray;

/// <summary>
/// Someone a gift card is delivered to.
/// </summary>
/// <remarks>
/// Mirrors the specification's <c>OrderRecipient</c> and <c>SearchRecipient</c>, which
/// are declared separately but have identical members.
/// </remarks>
public sealed record Recipient
{
    /// <summary>The recipient's name.</summary>
    public string? Name { get; init; }

    /// <summary>Email address. Required when the delivery template sends email.</summary>
    public string? Email { get; init; }

    /// <summary>Phone number. Required when the delivery template sends SMS.</summary>
    public string? Phone { get; init; }

    /// <summary>Your own identifier for this recipient.</summary>
    public string? RefId { get; init; }

    /// <summary>
    /// A description safe to log: the contact details are masked.
    /// </summary>
    /// <remarks>
    /// The compiler-generated record <c>ToString</c> would print
    /// <see cref="Email"/> and <see cref="Phone"/> in the clear. That matters
    /// beyond this type: <see cref="Voucher.ToString"/> interpolates the
    /// recipient, so logging a voucher would have leaked personal data even
    /// though the bearer fields beside it were correctly masked.
    /// </remarks>
    /// <returns>A redacted rendering of this recipient.</returns>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "Recipient {{ Name = {0}, Email = {1}, Phone = {2}, RefId = {3} }}",
        Name,
        MaskContact(Email),
        MaskContact(Phone),
        RefId);

    private static string MaskContact(string? value) =>
        string.IsNullOrEmpty(value)
            ? (value is null ? "null" : "\"\"")
            : Redaction.MaskPartial(value!);
}

/// <summary>
/// One issued gift card.
/// </summary>
/// <param name="Id">Voucher identifier, used by <c>ResendAsync</c> and <c>CancelAsync</c>.</param>
/// <param name="Code">
/// The redeemable code. <strong>Blank unless <c>ReturnCode</c> is enabled on your B2B
/// account.</strong> If you need codes returned to your own system rather than delivered
/// by Huuray, ask your Huuray contact to enable it.
/// </param>
/// <param name="Cvv">The card's CVV, where the product has one. Same account setting applies.</param>
/// <param name="RedeemLink">A link that redeems the card. Same account setting applies.</param>
/// <param name="Expires">Expiry, formatted as the API returned it.</param>
/// <param name="Recipient">Who this card was issued for, when the order named recipients.</param>
/// <remarks>
/// <see cref="Voucher.Code"/>, <see cref="Voucher.Cvv"/> and
/// <see cref="Voucher.RedeemLink"/> are <strong>bearer instruments</strong>: whoever holds
/// the value can spend it. This type overrides <see cref="ToString"/> so that printing a
/// voucher — in a log line, an exception, or a debugger watch window — never reveals them.
/// </remarks>
public sealed record Voucher(
    int? Id,
    string? Code,
    string? Cvv,
    string? RedeemLink,
    string? Expires,
    Recipient? Recipient)
{
    /// <summary>
    /// A description safe to log: the bearer fields are replaced with a marker.
    /// </summary>
    /// <returns>A redacted rendering of this voucher.</returns>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "Voucher {{ Id = {0}, Code = {1}, Cvv = {2}, RedeemLink = {3}, Expires = {4}, Recipient = {5} }}",
        Id,
        Mask(Code),
        Mask(Cvv),
        Mask(RedeemLink),
        Expires,
        Recipient);

    private static string Mask(string? value) =>
        string.IsNullOrEmpty(value) ? (value is null ? "null" : "\"\"") : Redaction.SecretMarker;
}

/// <summary>
/// The outcome of cancelling one voucher.
/// </summary>
/// <param name="Id">The voucher's identifier.</param>
/// <param name="Cancelled">Whether this particular voucher was cancelled.</param>
public sealed record CancelledVoucher(int Id, bool Cancelled);

/* ------------------------------------------------------------------- requests */

/// <summary>
/// Parameters for <c>POST /v4/Order</c>, used by both
/// <c>Orders.CreateAsync</c> and <c>Orders.CreateSyncAsync</c>.
/// </summary>
public sealed record CreateOrderRequest
{
    /// <summary>Product identifier from <c>Catalogue.ListAsync()</c>.</summary>
    public required string ProductToken { get; init; }

    /// <summary>
    /// Denomination in <strong>minor units</strong> — 50.00 is <c>5000</c>.
    /// </summary>
    /// <remarks>
    /// A major-unit amount here orders 1/100th of what you meant. The type is
    /// <see cref="int"/> so the compiler rejects a fractional literal; if your amount
    /// arrives as a <see cref="decimal"/> or <see cref="double"/>, put it through
    /// <see cref="MinorUnits"/> first.
    /// </remarks>
    public required int Value { get; init; }

    /// <summary>ISO alpha-3 currency code.</summary>
    public required string Currency { get; init; }

    /// <summary>
    /// How many codes to order. <c>Orders.CreateSyncAsync</c> caps this at
    /// <see cref="OrdersResource.SyncQuantityLimit"/>.
    /// </summary>
    public required int Quantity { get; init; }

    /// <summary>Optional expiry for the gift cards. Cannot exceed the product default.</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>
    /// Your own identifier for this order. Strongly recommended: it is the only way to
    /// find out whether an order landed after a timeout.
    /// </summary>
    public string? RefId { get; init; }

    /// <summary>Delivery template id from <c>Templates.ListAsync()</c>. Leave unset for no delivery.</summary>
    public int? TemplateId { get; init; }

    /// <summary>Schedule delivery for a future time. Leave unset to deliver as soon as possible.</summary>
    public DateTimeOffset? DeliveryDatetime { get; init; }

    /// <summary>A message included in every email or SMS sent for this order.</summary>
    public string? PersonalMessage { get; init; }

    /// <summary>
    /// Recipients. Required when <see cref="TemplateId"/> is set. The count must be
    /// either 1 or exactly <see cref="Quantity"/>.
    /// </summary>
    public IReadOnlyList<Recipient>? Recipients { get; init; }
}

/// <summary>
/// Parameters for the one-card, one-recipient convenience call.
/// </summary>
/// <remarks>
/// Performs exactly one <c>POST /v4/Order</c> with <c>Sync: false</c> and
/// <c>Quantity: 1</c>.
/// </remarks>
public sealed record SendRewardRequest
{
    /// <summary>Product identifier from <c>Catalogue.ListAsync()</c>.</summary>
    public required string ProductToken { get; init; }

    /// <summary>Denomination in <strong>minor units</strong> — 50.00 is <c>5000</c>.</summary>
    public required int Value { get; init; }

    /// <summary>ISO alpha-3 currency code.</summary>
    public required string Currency { get; init; }

    /// <summary>The single person receiving this reward.</summary>
    public required Recipient Recipient { get; init; }

    /// <summary>Delivery template id from <c>Templates.ListAsync()</c>.</summary>
    public required int TemplateId { get; init; }

    /// <summary>
    /// Your reconciliation key. <strong>Required by this SDK</strong>, though the API
    /// treats it as optional: without it, an order that times out cannot be looked up,
    /// and you cannot safely determine whether it landed. This SDK never generates one
    /// for you — it has to be a key your own system can reproduce.
    /// </summary>
    public required string RefId { get; init; }

    /// <summary>Optional expiry for the gift card. Cannot exceed the product default.</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>Schedule delivery for a future time.</summary>
    public DateTimeOffset? DeliveryDatetime { get; init; }

    /// <summary>A message included in the email or SMS.</summary>
    public string? PersonalMessage { get; init; }
}

/// <summary>
/// Parameters for <c>POST /v4/Search</c>. Every field is a filter; all are optional.
/// </summary>
public sealed record SearchOrdersRequest
{
    /// <summary>The unique identifier of the order.</summary>
    public string? OrderUid { get; init; }

    /// <summary>The unique identifier of the voucher. Required for the response to include the code.</summary>
    public int? VoucherId { get; init; }

    /// <summary>The token that identifies the product.</summary>
    public string? ProductToken { get; init; }

    /// <summary>Your own order reference.</summary>
    public string? RefId { get; init; }

    /// <summary>The unique identifier of the SMS template.</summary>
    public int? SmsTemplateId { get; init; }

    /// <summary>The unique identifier of the email template.</summary>
    public int? EmailTemplateId { get; init; }

    /// <summary>The scheduled delivery time to match.</summary>
    public DateTimeOffset? DeliveryDatetime { get; init; }

    /// <summary>The recipient's name.</summary>
    public string? RecipientName { get; init; }

    /// <summary>The recipient's email address.</summary>
    public string? RecipientEmail { get; init; }

    /// <summary>The recipient's phone number.</summary>
    public string? RecipientPhone { get; init; }

    /// <summary>Your own reference for the recipient.</summary>
    public string? RecipientRefId { get; init; }
}

/// <summary>
/// Parameters for <c>POST /v4/Resend</c>.
/// </summary>
public sealed record ResendRequest
{
    /// <summary>The order to resend from.</summary>
    public required string OrderUid { get; init; }

    /// <summary>
    /// A single voucher. Leave unset to resend the whole order to all its recipients.
    /// </summary>
    public int? VoucherId { get; init; }
}

/// <summary>
/// Parameters for <c>DELETE /v4/Cancel</c>.
/// </summary>
public sealed record CancelRequest
{
    /// <summary>The order to cancel from.</summary>
    public required string OrderUid { get; init; }

    /// <summary>A single voucher. Leave unset to attempt cancelling the whole order.</summary>
    public int? VoucherId { get; init; }
}

/* -------------------------------------------------------------------- results */

/// <summary>
/// The result of an asynchronous order. No voucher data is returned.
/// </summary>
/// <param name="OrderUid">The order's unique identifier. Keep it.</param>
/// <param name="RefId">The reference you sent, echoed back.</param>
public sealed record CreateOrderResult(string? OrderUid, string? RefId);

/// <summary>
/// The result of a synchronous order. Vouchers are returned inline.
/// </summary>
/// <param name="OrderUid">The order's unique identifier. Keep it.</param>
/// <param name="RefId">The reference you sent, echoed back.</param>
/// <param name="Vouchers">
/// The issued gift cards. Codes are blank unless <c>ReturnCode</c> is enabled on your account.
/// </param>
public sealed record CreateSyncOrderResult(
    string? OrderUid,
    string? RefId,
    IReadOnlyList<Voucher> Vouchers);

/// <summary>
/// The result of <c>POST /v4/Search</c>.
/// </summary>
/// <param name="OrderUid">The matched order's unique identifier.</param>
/// <param name="RefId">The matched order's reference.</param>
/// <param name="Vouchers">The gift cards on the matched order.</param>
public sealed record SearchOrdersResult(
    string? OrderUid,
    string? RefId,
    IReadOnlyList<Voucher> Vouchers);

/// <summary>
/// The result of <c>POST /v4/Resend</c>.
/// </summary>
/// <param name="NumberOfResends">How many deliveries the API reports it sent.</param>
/// <param name="Partial">
/// <see langword="true"/> when the API answered <c>206 Partial Content</c> — some
/// resends succeeded and some did not. Treating this as plain success is a common bug.
/// </param>
public sealed record ResendResult(int? NumberOfResends, bool Partial);

/// <summary>
/// The result of <c>DELETE /v4/Cancel</c>.
/// </summary>
/// <param name="OrderUid">The order the cancellation applied to.</param>
/// <param name="OrderCancelled">Whether the order as a whole was cancelled.</param>
/// <param name="Vouchers">The per-voucher outcome.</param>
/// <param name="Partial">
/// <see langword="true"/> when the API answered <c>206 Partial Content</c> — inspect
/// <paramref name="Vouchers"/> to see which ones were not cancelled.
/// </param>
public sealed record CancelResult(
    string? OrderUid,
    bool OrderCancelled,
    IReadOnlyList<CancelledVoucher> Vouchers,
    bool Partial);
