using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Huuray;

/// <summary>
/// Keeps bearer instruments and personal data out of logs, errors and terminal output.
/// </summary>
/// <remarks>
/// <para>
/// Voucher codes are bearer instruments: whoever holds the code holds the value. They
/// must never reach a log file, an error report, a CI fixture, or a bug report pasted
/// into a public issue.
/// </para>
/// <para>
/// Redaction is this library's job, not the caller's — anything this SDK prints or
/// attaches to an exception goes through here first. It is exported so you can do the
/// same on your side.
/// </para>
/// </remarks>
public static class Redaction
{
    /// <summary>The marker written in place of a value that can be redeemed.</summary>
    public const string SecretMarker = "[redacted: bearer value]";

    /// <summary>How deep <see cref="Redact"/> walks before giving up.</summary>
    public const int MaxDepth = 12;

    private const string TooDeepMarker = "[redacted: too deep]";

    private static readonly HashSet<string> SecretFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "CVV", "RedeemLink" };

    private static readonly HashSet<string> SensitiveFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            RequestSigner.TokenHeaderName,
            RequestSigner.HashHeaderName,
            "ApiToken",
            "ApiSecret",
            "Email",
            "Phone",
        };

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>
    /// Response fields that carry redeemable value and are never logged.
    /// </summary>
    /// <remarks>Matching is case-insensitive, so <c>Code</c> also covers <c>code</c>.</remarks>
    public static IReadOnlyCollection<string> SecretFieldNames => SecretFields;

    /// <summary>
    /// Fields carrying credentials or personal data, masked in any diagnostic output.
    /// </summary>
    /// <remarks>Matching is case-insensitive.</remarks>
    public static IReadOnlyCollection<string> SensitiveFieldNames => SensitiveFields;

    /// <summary>
    /// Returns a deep copy of <paramref name="node"/> with secret and sensitive values
    /// replaced by markers.
    /// </summary>
    /// <param name="node">The JSON to redact. May be <see langword="null"/>.</param>
    /// <returns>A new tree; the input is never modified.</returns>
    /// <remarks>
    /// Deliberately lossy: a redacted voucher code cannot be recovered from the output.
    /// </remarks>
    public static JsonNode? Redact(JsonNode? node) => RedactCore(node, 0);

    /// <summary>
    /// Redacts a JSON document supplied as text.
    /// </summary>
    /// <param name="json">A JSON document.</param>
    /// <param name="indented">Whether to pretty-print the result.</param>
    /// <returns>The redacted document, serialised back to text.</returns>
    /// <exception cref="JsonException"><paramref name="json"/> is not valid JSON.</exception>
    public static string RedactJson(string json, bool indented = false) =>
        SafeStringify(JsonNode.Parse(json), indented);

    /// <summary>
    /// Serialises a JSON tree with redaction applied. Safe to log.
    /// </summary>
    /// <param name="node">The JSON to serialise. May be <see langword="null"/>.</param>
    /// <param name="indented">Whether to pretty-print the result.</param>
    /// <returns>Redacted JSON text; <c>null</c> for a null input.</returns>
    public static string SafeStringify(JsonNode? node, bool indented = false)
    {
        JsonNode? redacted = Redact(node);
        if (redacted is null)
        {
            return "null";
        }

        return indented ? redacted.ToJsonString(IndentedOptions) : redacted.ToJsonString();
    }

    private static JsonNode? RedactCore(JsonNode? node, int depth)
    {
        if (depth > MaxDepth)
        {
            return JsonValue.Create(TooDeepMarker);
        }

        switch (node)
        {
            case null:
                return null;

            case JsonArray array:
            {
                JsonArray result = new();
                foreach (JsonNode? item in array)
                {
                    result.Add(RedactCore(item, depth + 1));
                }

                return result;
            }

            case JsonObject obj:
            {
                JsonObject result = new();
                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    if (SecretFields.Contains(pair.Key))
                    {
                        result[pair.Key] = IsNullOrEmpty(pair.Value)
                            ? pair.Value?.DeepClone()
                            : JsonValue.Create(SecretMarker);
                    }
                    else if (SensitiveFields.Contains(pair.Key))
                    {
                        result[pair.Key] = IsNullOrEmpty(pair.Value)
                            ? pair.Value?.DeepClone()
                            : JsonValue.Create(MaskPartial(AsText(pair.Value)));
                    }
                    else
                    {
                        result[pair.Key] = RedactCore(pair.Value, depth + 1);
                    }
                }

                return result;
            }

            default:
                return node.DeepClone();
        }
    }

    /// <summary>Keeps just enough of a value to recognise it, never enough to use it.</summary>
    internal static string MaskPartial(string value) =>
        value.Length <= 4 ? "***" : value[..2] + "***" + value[^2..];

    private static bool IsNullOrEmpty(JsonNode? node)
    {
        if (node is null)
        {
            return true;
        }

        return node is JsonValue value && value.TryGetValue(out string? text) && text is { Length: 0 };
    }

    private static string AsText(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? text) && text is not null)
        {
            return text;
        }

        return node?.ToJsonString() ?? string.Empty;
    }
}
