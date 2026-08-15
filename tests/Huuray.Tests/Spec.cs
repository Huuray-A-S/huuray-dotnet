using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;

namespace Huuray.Tests;

/// <summary>
/// The vendored OpenAPI document, and a validator for checking requests against it.
/// </summary>
/// <remarks>
/// The SDK's central promise is that it invents nothing: it calls only documented
/// operations and sends only documented fields. That promise has to be mechanical, not a
/// matter of discipline, or it quietly decays.
/// </remarks>
internal static class Spec
{
    private static readonly Lazy<JsonObject> Lazily = new(Load);

    internal static JsonObject Document => Lazily.Value;

    internal static JsonObject Paths => Document["paths"]!.AsObject();

    internal static JsonObject Schemas => Document["components"]!["schemas"]!.AsObject();

    /// <summary>Every operation the API documents, as <c>POST /v4/Order</c> style keys.</summary>
    internal static HashSet<string> Operations()
    {
        HashSet<string> operations = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> path in Paths)
        {
            foreach (KeyValuePair<string, JsonNode?> verb in path.Value!.AsObject())
            {
                if (string.Equals(verb.Key, "parameters", StringComparison.Ordinal))
                {
                    continue;
                }

                operations.Add(verb.Key.ToUpperInvariant() + " " + path.Key);
            }
        }

        return operations;
    }

    internal static JsonObject? Operation(string method, string path)
    {
        if (!Paths.TryGetPropertyValue(path, out JsonNode? item) || item is null)
        {
            return null;
        }

        return item.AsObject().TryGetPropertyValue(method.ToLowerInvariant(), out JsonNode? operation)
            ? operation?.AsObject()
            : null;
    }

    internal static JsonObject? RequestBodySchema(string method, string path) =>
        Operation(method, path)?["requestBody"]?["content"]?["application/json"]?["schema"]?.AsObject();

    internal static HashSet<string> DeclaredQueryParameters(string method, string path)
    {
        HashSet<string> declared = new(StringComparer.Ordinal);
        JsonArray? parameters = Operation(method, path)?["parameters"]?.AsArray();
        if (parameters is null)
        {
            return declared;
        }

        foreach (JsonNode? parameter in parameters)
        {
            if (parameter is null)
            {
                continue;
            }

            if (string.Equals(parameter["in"]?.GetValue<string>(), "query", StringComparison.Ordinal))
            {
                declared.Add(parameter["name"]!.GetValue<string>());
            }
        }

        return declared;
    }

    /// <summary>
    /// Returns human-readable violations; an empty list means the value conforms.
    /// </summary>
    /// <remarks>
    /// <strong>Fails closed.</strong> A schema shape this validator does not understand is
    /// an error, never a silent pass. The spec-drift job re-downloads the live
    /// specification weekly — if a refresh starts using <c>allOf</c> wrappers (standard
    /// Swashbuckle output for nullable <c>$ref</c>s) or drops <c>type</c>, the gates must
    /// break loudly rather than validate nothing while staying green.
    /// </remarks>
    internal static List<string> Validate(JsonNode schemaNode, JsonNode? value, string at = "$")
    {
        List<string> errors = new();
        JsonObject schema = Deref(schemaNode);

        if (schema.ContainsKey("allOf") || schema.ContainsKey("oneOf") || schema.ContainsKey("anyOf"))
        {
            errors.Add(
                $"{at}: schema uses allOf/oneOf/anyOf, which this validator does not handle — " +
                "extend Spec.Validate before trusting this run");
            return errors;
        }

        bool nullable = schema["nullable"] is JsonNode flag && flag.GetValue<bool>();

        if (value is null)
        {
            if (!nullable)
            {
                errors.Add($"{at}: null but the spec does not mark it nullable");
            }

            return errors;
        }

        string? type = schema["type"]?.GetValue<string>();

        switch (type)
        {
            case "object":
            {
                if (value is not JsonObject obj)
                {
                    errors.Add($"{at}: expected object, got {Describe(value)}");
                    break;
                }

                JsonObject? properties = schema["properties"]?.AsObject();
                HashSet<string> known = new(StringComparer.Ordinal);
                if (properties is not null)
                {
                    foreach (KeyValuePair<string, JsonNode?> property in properties)
                    {
                        known.Add(property.Key);
                    }
                }

                // The invention detector: a property the specification does not define.
                foreach (KeyValuePair<string, JsonNode?> member in obj)
                {
                    if (!known.Contains(member.Key))
                    {
                        errors.Add(
                            $"{at}.{member.Key}: not defined in the spec — " +
                            "the SDK must not send undocumented fields");
                    }
                }

                JsonArray? required = schema["required"]?.AsArray();
                if (required is not null)
                {
                    foreach (JsonNode? name in required)
                    {
                        string key = name!.GetValue<string>();
                        if (!obj.ContainsKey(key))
                        {
                            errors.Add($"{at}.{key}: required by the spec but not sent");
                        }
                    }
                }

                if (properties is not null)
                {
                    foreach (KeyValuePair<string, JsonNode?> property in properties)
                    {
                        if (obj.TryGetPropertyValue(property.Key, out JsonNode? member))
                        {
                            errors.AddRange(Validate(property.Value!, member, $"{at}.{property.Key}"));
                        }
                    }
                }

                break;
            }

            case "array":
            {
                if (value is not JsonArray array)
                {
                    errors.Add($"{at}: expected array, got {Describe(value)}");
                    break;
                }

                JsonNode? items = schema["items"];
                if (items is not null)
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        errors.AddRange(Validate(
                            items,
                            array[i],
                            string.Format(CultureInfo.InvariantCulture, "{0}[{1}]", at, i)));
                    }
                }

                break;
            }

            case "integer":
                if (value is not JsonValue integer || !integer.TryGetValue(out long _))
                {
                    errors.Add($"{at}: expected integer, got {Describe(value)}");
                }

                break;

            case "number":
                if (value is not JsonValue number || !number.TryGetValue(out double _))
                {
                    errors.Add($"{at}: expected number, got {Describe(value)}");
                }

                break;

            case "boolean":
                if (value is not JsonValue boolean || !boolean.TryGetValue(out bool _))
                {
                    errors.Add($"{at}: expected boolean, got {Describe(value)}");
                }

                break;

            case "string":
                if (value is not JsonValue text || !text.TryGetValue(out string? _))
                {
                    errors.Add($"{at}: expected string, got {Describe(value)}");
                }

                break;

            default:
                errors.Add(
                    $"{at}: schema has {(type is null ? "no \"type\"" : $"unknown type \"{type}\"")} — " +
                    "this validator cannot check it; extend Spec.Validate before trusting this run");
                break;
        }

        return errors;
    }

    private static JsonObject Deref(JsonNode schemaNode)
    {
        JsonObject schema = schemaNode.AsObject();
        if (!schema.TryGetPropertyValue("$ref", out JsonNode? reference) || reference is null)
        {
            return schema;
        }

        string name = reference.GetValue<string>().Replace("#/components/schemas/", string.Empty, StringComparison.Ordinal);
        if (!Schemas.TryGetPropertyValue(name, out JsonNode? target) || target is null)
        {
            throw new InvalidOperationException($"Unresolvable $ref in spec: {reference.GetValue<string>()}");
        }

        return target.AsObject();
    }

    private static string Describe(JsonNode value) => value switch
    {
        JsonArray => "array",
        JsonObject => "object",
        _ => value.ToJsonString(),
    };

    private static JsonObject Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "openapi", "huuray-v4.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The vendored specification was not copied to the test output. " +
                "It is the single source of truth for these gates, so its absence is a failure, not a skip.",
                path);
        }

        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
