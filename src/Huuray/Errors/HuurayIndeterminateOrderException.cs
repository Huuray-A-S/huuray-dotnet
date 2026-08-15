using System;

namespace Huuray;

/// <summary>
/// An order request failed in a way that leaves its outcome unknown — a timeout, a
/// dropped connection, an unreadable response, or a 5xx after the request was sent.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Do not retry.</strong> <c>POST /v4/Order</c> has no idempotency key, so a
/// retry can order a second set of gift cards. The order may or may not have been created.
/// </para>
/// <para>
/// Resolve it by looking the order up instead. The API answers <c>404</c> when nothing
/// matches, which this client throws as <see cref="HuurayNotFoundException"/> — catch
/// it and read it as "the order did not land":
/// </para>
/// <code language="csharp">
/// try
/// {
///     await huuray.SendRewardAsync(new SendRewardRequest { RefId = "payroll-2026-08-jane", /* … */ });
/// }
/// catch (HuurayIndeterminateOrderException ex)
/// {
///     try
///     {
///         SearchOrdersResult found = await huuray.Orders.SearchAsync(new SearchOrdersRequest { RefId = ex.RefId });
///         if (found.OrderUid is not null)
///         {
///             // The order landed. Nothing more to do.
///         }
///         else
///         {
///             // No match, so it did not land. Safe to send again with the same RefId.
///         }
///     }
///     catch (HuurayNotFoundException)
///     {
///         // 404 — no order exists for this RefId. Safe to send again.
///     }
/// }
/// </code>
/// </remarks>
public sealed class HuurayIndeterminateOrderException : HuurayException
{
    /// <summary>Creates an indeterminate-order exception.</summary>
    /// <param name="refId">The <c>RefID</c> sent with the order, if any — the key to look it up with.</param>
    /// <param name="innerException">The transport or server failure that caused this one.</param>
    public HuurayIndeterminateOrderException(string? refId, Exception? innerException = null)
        : base(BuildMessage(refId), innerException)
    {
        RefId = refId;
    }

    /// <summary>
    /// The <c>RefID</c> sent with the order, if any — the key to reconcile with.
    /// </summary>
    public string? RefId { get; }

    private static string BuildMessage(string? refId) =>
        "The order request did not complete, so it is unknown whether the order was created. " +
        "Do NOT retry: /v4/Order has no idempotency key and a retry may order a second time. " +
        (string.IsNullOrEmpty(refId)
            ? "No RefID was sent, so the order cannot be looked up by reference. " +
              "Always set RefId on orders so this case is recoverable."
            : $"Call Orders.SearchAsync(new SearchOrdersRequest {{ RefId = \"{refId}\" }}) to check whether it landed.");
}
