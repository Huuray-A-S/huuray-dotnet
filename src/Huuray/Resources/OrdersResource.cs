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
/// Ordering gift cards, and everything you do with an order afterwards.
/// </summary>
/// <remarks>
/// <strong>None of the value-moving calls here are ever retried.</strong>
/// <c>POST /v4/Order</c>, <c>POST /v4/Resend</c> and <c>DELETE /v4/Cancel</c> have no
/// idempotency key, so repeating one can order a second time or re-deliver a live gift
/// card. <see cref="SearchAsync"/> is a read and is retried, despite being a POST.
/// </remarks>
public sealed class OrdersResource
{
    /// <summary>The maximum quantity a synchronous order may request, per the API.</summary>
    public const int SyncQuantityLimit = 25;

    private readonly HuurayClient _client;

    internal OrdersResource(HuurayClient client) => _client = client;

    /// <summary>
    /// Places an order and returns immediately.
    /// </summary>
    /// <param name="request">Product, amount, quantity, delivery and your reference.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The order's identifier and the reference you sent.</returns>
    /// <remarks>
    /// <c>POST /v4/Order</c> with <c>Sync: false</c>.
    /// <para>
    /// Huuray delivers the gift cards using the template you name; no voucher data comes
    /// back. Use <see cref="SearchAsync"/> with your reference to find the order later.
    /// </para>
    /// <para>
    /// <strong>Not retried on failure.</strong> A timeout, a dropped connection, an
    /// unreadable response or a 5xx throws <see cref="HuurayIndeterminateOrderException"/>
    /// instead — do not retry it, reconcile.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The quantity or the recipient list is invalid.</exception>
    /// <exception cref="HuurayIndeterminateOrderException">The outcome of the order is unknown.</exception>
    /// <exception cref="HuurayApiException">The API definitively rejected the order.</exception>
    public async Task<CreateOrderResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string body = BuildOrderBody(request, sync: false);
        OrderResponseWire? data = await PostOrderAsync(body, request.RefId, cancellationToken).ConfigureAwait(false);

        return new CreateOrderResult(data?.OrderUID, data?.RefID);
    }

    /// <summary>
    /// Places an order and waits for the vouchers.
    /// </summary>
    /// <param name="request">Product, amount, quantity, delivery and your reference.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The order's identifier, your reference, and the issued vouchers.</returns>
    /// <remarks>
    /// <c>POST /v4/Order</c> with <c>Sync: true</c>.
    /// <para>
    /// <see cref="CreateOrderRequest.Quantity"/> is limited to
    /// <see cref="SyncQuantityLimit"/> for synchronous orders. Voucher codes are blank
    /// unless <c>ReturnCode</c> is enabled on your account.
    /// </para>
    /// <para><strong>Not retried on failure</strong>, exactly as <see cref="CreateAsync"/>.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The quantity exceeds <see cref="SyncQuantityLimit"/>, or the recipient list is invalid.
    /// </exception>
    /// <exception cref="HuurayIndeterminateOrderException">The outcome of the order is unknown.</exception>
    /// <exception cref="HuurayApiException">The API definitively rejected the order.</exception>
    public async Task<CreateSyncOrderResult> CreateSyncAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Quantity > SyncQuantityLimit)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Synchronous orders are limited to {0} codes; received {1}. Use CreateAsync for larger orders.",
                    SyncQuantityLimit,
                    request.Quantity),
                nameof(request));
        }

        string body = BuildOrderBody(request, sync: true);
        OrderResponseWire? data = await PostOrderAsync(body, request.RefId, cancellationToken).ConfigureAwait(false);

        return new CreateSyncOrderResult(data?.OrderUID, data?.RefID, MapVouchers(data?.Vouchers));
    }

    /// <summary>
    /// Sends one gift card to one recipient — the common case in a single call.
    /// </summary>
    /// <param name="request">Product, amount, the recipient, the template, and your reference.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The order's identifier and the reference you sent.</returns>
    /// <remarks>
    /// Performs exactly one <c>POST /v4/Order</c> with <c>Sync: false</c> and
    /// <c>Quantity: 1</c>, and nothing else.
    /// <para>
    /// <see cref="SendRewardRequest.RefId"/> must be a non-empty key from your own
    /// system. This SDK will not generate one: a generated reference is lost the moment
    /// the process that made it dies, which is exactly when you need it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="SendRewardRequest.RefId"/> is empty.</exception>
    /// <exception cref="HuurayIndeterminateOrderException">The outcome of the order is unknown.</exception>
    /// <exception cref="HuurayApiException">The API definitively rejected the order.</exception>
    public Task<CreateOrderResult> SendRewardAsync(
        SendRewardRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrEmpty(request.RefId))
        {
            throw new ArgumentException(
                "RefId is required by SendRewardAsync. It is the only way to determine whether an order " +
                "landed if the request times out, because /v4/Order has no idempotency key. Use a stable " +
                "key from your own system, for example \"payroll-2026-08-jane\".",
                nameof(request));
        }

        return CreateAsync(
            new CreateOrderRequest
            {
                ProductToken = request.ProductToken,
                Value = request.Value,
                Currency = request.Currency,
                Quantity = 1,
                RefId = request.RefId,
                TemplateId = request.TemplateId,
                Recipients = new[] { request.Recipient },
                Expires = request.Expires,
                DeliveryDatetime = request.DeliveryDatetime,
                PersonalMessage = request.PersonalMessage,
            },
            cancellationToken);
    }

    /// <summary>
    /// Searches gift cards from previous orders.
    /// </summary>
    /// <param name="request">The filters to apply. Every one is optional.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The matched order and its vouchers.</returns>
    /// <remarks>
    /// <c>POST /v4/Search</c> — a read, despite being a POST, so it is retried.
    /// <para>
    /// This is also how you resolve an order whose outcome is unknown: search by the
    /// reference you sent. Note that the API answers <c>404</c> when nothing matches,
    /// which arrives here as <see cref="HuurayNotFoundException"/> — during
    /// reconciliation that means "the order did not land".
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HuurayNotFoundException">Nothing matched the filters.</exception>
    /// <exception cref="HuurayApiException">The API returned another non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<SearchOrdersResult> SearchAsync(
        SearchOrdersRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string body = JsonSerializer.Serialize(
            new SearchRequestWire
            {
                OrderUID = request.OrderUid,
                VoucherID = request.VoucherId,
                ProductToken = request.ProductToken,
                RefID = request.RefId,
                SMSTemplateID = request.SmsTemplateId,
                EmailTemplateID = request.EmailTemplateId,
                DeliveryDatetime = SpecFormats.ToSpecDateTime(request.DeliveryDatetime),
                RecipientName = request.RecipientName,
                RecipientEmail = request.RecipientEmail,
                RecipientPhone = request.RecipientPhone,
                RecipientRefID = request.RecipientRefId,
            },
            HuurayJsonContext.Default.SearchRequestWire);

        HuurayResponse<OrderResponseWire> response = await _client.SendAsync(
                HttpMethod.Post,
                "/v4/Search",
                body,
                query: null,
                retryable: true,
                HuurayJsonContext.Default.OrderResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        return new SearchOrdersResult(
            response.Data?.OrderUID,
            response.Data?.RefID,
            MapVouchers(response.Data?.Vouchers));
    }

    /// <summary>
    /// Resends an order, or one voucher from it, to its original recipients.
    /// </summary>
    /// <param name="request">The order, and optionally a single voucher.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>How many deliveries were sent, and whether the outcome was partial.</returns>
    /// <remarks>
    /// <c>POST /v4/Resend</c>.
    /// <para>
    /// <strong>Never retried.</strong> A resend delivers a live gift card, so repeating
    /// it on a timeout would re-send real value.
    /// </para>
    /// <para>
    /// Check <see cref="ResendResult.Partial"/>: the API answers <c>206</c> when only
    /// some deliveries succeeded.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<ResendResult> ResendAsync(
        ResendRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string body = JsonSerializer.Serialize(
            new ResendRequestWire { OrderUID = request.OrderUid, VoucherID = request.VoucherId },
            HuurayJsonContext.Default.ResendRequestWire);

        HuurayResponse<ResendResponseWire> response = await _client.SendAsync(
                HttpMethod.Post,
                "/v4/Resend",
                body,
                query: null,
                retryable: false,
                HuurayJsonContext.Default.ResendResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        return new ResendResult(response.Data?.NumberOfResends, response.HttpStatus == 206);
    }

    /// <summary>
    /// Cancels an order, or one voucher from it.
    /// </summary>
    /// <param name="request">The order, and optionally a single voucher.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The per-voucher outcome, and whether the outcome was partial.</returns>
    /// <remarks>
    /// <c>DELETE /v4/Cancel</c> — with a JSON request body, which is unusual but is what
    /// the specification declares.
    /// <para>
    /// <strong>Never retried.</strong> Check <see cref="CancelResult.Partial"/>: the API
    /// answers <c>206</c> when only some vouchers could be cancelled, and the per-voucher
    /// outcome is in <see cref="CancelResult.Vouchers"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<CancelResult> CancelAsync(
        CancelRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string body = JsonSerializer.Serialize(
            new CancelRequestWire { OrderUID = request.OrderUid, VoucherID = request.VoucherId },
            HuurayJsonContext.Default.CancelRequestWire);

        HuurayResponse<CancelResponseWire> response = await _client.SendAsync(
                HttpMethod.Delete,
                "/v4/Cancel",
                body,
                query: null,
                retryable: false,
                HuurayJsonContext.Default.CancelResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        List<CancelVoucherWire> items = response.Data?.Vouchers ?? new List<CancelVoucherWire>();
        CancelledVoucher[] vouchers = new CancelledVoucher[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            vouchers[i] = new CancelledVoucher(items[i].ID, items[i].Cancelled);
        }

        return new CancelResult(
            response.Data?.OrderUID,
            response.Data?.OrderCancelled ?? false,
            vouchers,
            response.HttpStatus == 206);
    }

    /* ---------------------------------------------------------------- private */

    private static string BuildOrderBody(CreateOrderRequest request, bool sync)
    {
        if (request.Quantity < 1)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Quantity must be a positive integer, received {0}.",
                    request.Quantity),
                nameof(request));
        }

        if (request.TemplateId is not null)
        {
            int count = request.Recipients?.Count ?? 0;
            if (count == 0)
            {
                throw new ArgumentException(
                    "Recipients is required when TemplateId is set — the template needs somewhere to deliver to.",
                    nameof(request));
            }

            if (count != 1 && count != request.Quantity)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Recipients must contain either 1 entry or exactly Quantity ({0}); received {1}.",
                        request.Quantity,
                        count),
                    nameof(request));
            }
        }

        List<OrderRecipientWire>? recipients = null;
        if (request.Recipients is not null)
        {
            recipients = new List<OrderRecipientWire>(request.Recipients.Count);
            foreach (Recipient recipient in request.Recipients)
            {
                recipients.Add(new OrderRecipientWire
                {
                    Name = recipient.Name,
                    Email = recipient.Email,
                    Phone = recipient.Phone,
                    RefID = recipient.RefId,
                });
            }
        }

        OrderRequestWire wire = new()
        {
            Product = new OrderProductWire
            {
                Token = request.ProductToken,
                Value = request.Value,
                Currency = request.Currency,
                Quantity = request.Quantity,
                Expires = SpecFormats.ToSpecDateTime(request.Expires),
            },
            Sync = sync,
            RefID = request.RefId,
            DeliveryTemplateId = request.TemplateId,
            DeliveryDatetime = SpecFormats.ToSpecDateTime(request.DeliveryDatetime),
            PersonalMessage = request.PersonalMessage,
            Recipients = recipients,
        };

        return JsonSerializer.Serialize(wire, HuurayJsonContext.Default.OrderRequestWire);
    }

    private async Task<OrderResponseWire?> PostOrderAsync(
        string body,
        string? refId,
        CancellationToken cancellationToken)
    {
        try
        {
            HuurayResponse<OrderResponseWire> response = await _client.SendAsync(
                    HttpMethod.Post,
                    "/v4/Order",
                    body,
                    query: null,
                    retryable: false,
                    HuurayJsonContext.Default.OrderResponseWire,
                    cancellationToken)
                .ConfigureAwait(false);

            return response.Data;
        }
        catch (Exception exception) when (
            exception is HuurayConnectionException
                or HuurayServerException
                or OperationCanceledException)
        {
            // The request may already have been processed. Never retry it here; make the
            // caller reconcile instead. A 4xx is deliberately not caught: that order was
            // definitively rejected and nothing was created.
            //
            // OperationCanceledException is included on purpose. A caller-supplied token
            // firing mid-flight — an ASP.NET Core request abort, say — leaves the order in
            // exactly the same unknown state as a timeout: the request was already on the
            // wire. Letting a bare TaskCanceledException escape would hand the caller no
            // RefId, no "do not retry" signal, and no pointer at SearchAsync, and a generic
            // resilience handler would then happily re-issue the order.
            throw new HuurayIndeterminateOrderException(refId, exception);
        }
    }

    private static IReadOnlyList<Voucher> MapVouchers(List<VoucherWire>? wires)
    {
        if (wires is null || wires.Count == 0)
        {
            return Array.Empty<Voucher>();
        }

        Voucher[] vouchers = new Voucher[wires.Count];
        for (int i = 0; i < wires.Count; i++)
        {
            VoucherWire v = wires[i];
            Recipient? recipient = v.Recipient is null
                ? null
                : new Recipient
                {
                    Name = v.Recipient.Name,
                    Email = v.Recipient.Email,
                    Phone = v.Recipient.Phone,
                    RefId = v.Recipient.RefID,
                };

            vouchers[i] = new Voucher(v.ID, v.Code, v.CVV, v.RedeemLink, v.Expires, recipient);
        }

        return vouchers;
    }
}
