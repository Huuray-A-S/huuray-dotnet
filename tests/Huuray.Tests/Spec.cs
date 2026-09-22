using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    internal static JsonObject? RequestBody(string method, string path) =>
        Operation(method, path)?["requestBody"]?.AsObject();

    /// <summary>
    /// Checks a captured request's body against the operation's <c>requestBody</c>; an empty
    /// list means it conforms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Understands exactly two media types: <c>application/json</c>, validated with
    /// <see cref="Validate"/>, and <c>multipart/form-data</c>, validated part by part with
    /// <see cref="ValidateMultipart"/>. Any other media type, and any key this method does not
    /// know, <strong>fails closed</strong>.
    /// </para>
    /// <para>
    /// The body is parsed as JSON only when it was sent as <c>application/json</c>, so a
    /// multipart body reaching a JSON operation is a failure message, never an exception
    /// that takes the gate down with it.
    /// </para>
    /// </remarks>
    internal static List<string> ValidateRequestBody(JsonObject? requestBody, CapturedRequest call)
    {
        string at = $"{call.Method} {call.Path}";
        List<string> errors = new();

        if (requestBody is null)
        {
            // The spec declares no body for this operation, so the SDK must send none.
            if (!call.BodyOmitted)
            {
                errors.Add($"{at}: the spec declares no requestBody, but the SDK sent one");
            }

            return errors;
        }

        foreach (KeyValuePair<string, JsonNode?> member in requestBody)
        {
            if (member.Key is not ("content" or "required" or "description"))
            {
                errors.Add($"{at}: requestBody has \"{member.Key}\", which this validator does not handle — extend Spec before trusting this run");
            }
        }

        JsonObject? content = requestBody["content"]?.AsObject();
        if (content is null || content.Count == 0)
        {
            errors.Add($"{at}: requestBody declares no content — extend Spec before trusting this run");
            return errors;
        }

        foreach (KeyValuePair<string, JsonNode?> declared in content)
        {
            if (declared.Key is not ("application/json" or "multipart/form-data"))
            {
                errors.Add($"{at}: requestBody declares {declared.Key}, which this validator does not handle — extend Spec before trusting this run");
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        if (call.BodyOmitted)
        {
            errors.Add($"{at}: the spec declares a requestBody, but the SDK sent none");
            return errors;
        }

        string? mediaType = call.MediaType;
        if (mediaType is null || !content.TryGetPropertyValue(mediaType, out JsonNode? mediaNode) || mediaNode is null)
        {
            errors.Add($"{at}: sent {mediaType ?? "a body with no Content-Type"}, but the spec declares only {string.Join(", ", content.Select(c => c.Key))}");
            return errors;
        }

        JsonObject media = mediaNode.AsObject();
        if (mediaType == "multipart/form-data")
        {
            errors.AddRange(ValidateMultipart(media, call, at));
            return errors;
        }

        foreach (KeyValuePair<string, JsonNode?> member in media)
        {
            if (member.Key != "schema")
            {
                errors.Add($"{at}: application/json has \"{member.Key}\", which this validator does not handle — extend Spec before trusting this run");
            }
        }

        if (media["schema"] is not JsonNode schema)
        {
            errors.Add($"{at}: application/json declares no schema — extend Spec before trusting this run");
            return errors;
        }

        JsonNode? body;
        try
        {
            body = JsonNode.Parse(call.Body!);
        }
        catch (JsonException)
        {
            errors.Add($"{at}: sent as application/json, but the body is not JSON");
            return errors;
        }

        errors.AddRange(Validate(schema, body, at));
        return errors;
    }

    /// <summary>
    /// Checks a captured <c>multipart/form-data</c> body against its media object: every part
    /// declared, every required part sent, no part twice, and every binary property sent as a
    /// file — with a file name and its own Content-Type, as raw bytes.
    /// </summary>
    /// <remarks>
    /// <strong>Fails closed.</strong> It understands an object schema whose properties are all
    /// <c>{ "type": "string", "format": "binary" }</c>, and an <c>encoding</c> of
    /// <c>{ "style": "form" }</c> per declared property. Anything else — a text part, a nested
    /// object, <c>allOf</c>, an encoding <c>contentType</c>, <c>explode</c> — is reported as a
    /// failure, and the parts are not checked against a shape the validator does not understand.
    /// The bytes themselves are never interpreted.
    /// </remarks>
    internal static List<string> ValidateMultipart(JsonObject media, CapturedRequest call, string at = "$")
    {
        List<string> errors = new();
        string extend = " — extend Spec.ValidateMultipart before trusting this run";

        foreach (KeyValuePair<string, JsonNode?> member in media)
        {
            if (member.Key is not ("schema" or "encoding"))
            {
                errors.Add($"{at}: multipart/form-data has \"{member.Key}\", which this validator does not handle{extend}");
            }
        }

        if (media["schema"] is not JsonNode schemaNode)
        {
            errors.Add($"{at}: multipart/form-data declares no schema{extend}");
            return errors;
        }

        JsonObject schema = Deref(schemaNode);
        foreach (KeyValuePair<string, JsonNode?> member in schema)
        {
            if (member.Key is not ("type" or "properties" or "required" or "title" or "description"))
            {
                errors.Add($"{at}: the multipart schema has \"{member.Key}\", which this validator does not handle{extend}");
            }
        }

        if (Text(schema["type"]) != "object")
        {
            errors.Add($"{at}: the multipart schema is not \"type\": \"object\"{extend}");
        }

        JsonObject properties = schema["properties"]?.AsObject() ?? new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> property in properties)
        {
            JsonObject part = Deref(property.Value!);
            foreach (KeyValuePair<string, JsonNode?> member in part)
            {
                if (member.Key is not ("type" or "format" or "description"))
                {
                    errors.Add($"{at}.{property.Key}: the part schema has \"{member.Key}\", which this validator does not handle{extend}");
                }
            }

            if (Text(part["type"]) != "string" || Text(part["format"]) != "binary")
            {
                errors.Add($"{at}.{property.Key}: only binary file parts (string/binary) are handled{extend}");
            }
        }

        JsonObject encoding = media["encoding"]?.AsObject() ?? new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> entry in encoding)
        {
            if (!properties.ContainsKey(entry.Key))
            {
                errors.Add($"{at}: encoding names \"{entry.Key}\", which is not a declared part{extend}");
            }

            foreach (KeyValuePair<string, JsonNode?> member in entry.Value!.AsObject())
            {
                bool formStyle = member.Key == "style"
                    && member.Value is JsonValue style
                    && style.TryGetValue(out string? text)
                    && text == "form";
                if (!formStyle)
                {
                    errors.Add($"{at}.{entry.Key}: encoding has \"{member.Key}\", which this validator does not handle{extend}");
                }
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        if (call.Parts is not IReadOnlyList<CapturedPart> parts)
        {
            errors.Add($"{at}: the body is not well-formed multipart/form-data: {call.PartsError ?? "it could not be read"}");
            return errors;
        }

        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        foreach (CapturedPart part in parts)
        {
            if (part.Name is null)
            {
                errors.Add($"{at}: sent a part with no name");
                continue;
            }

            seen[part.Name] = seen.TryGetValue(part.Name, out int count) ? count + 1 : 1;

            if (!properties.ContainsKey(part.Name))
            {
                errors.Add($"{at}.{part.Name}: not defined in the spec — the SDK must not send undocumented parts");
                continue;
            }

            if (!string.Equals(part.Disposition, "form-data", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{at}.{part.Name}: sent with Content-Disposition \"{part.Disposition}\", not form-data");
            }

            // Every declared property is a binary file part, checked above.
            if (string.IsNullOrEmpty(part.FileNameParameter))
            {
                errors.Add($"{at}.{part.Name}: a binary part must be sent as a file, with a filename");
            }

            if (part.ContentType is null)
            {
                errors.Add($"{at}.{part.Name}: a binary part must carry its own Content-Type");
            }

            if (part.Headers.ContainsKey("Content-Transfer-Encoding"))
            {
                errors.Add($"{at}.{part.Name}: sent with a Content-Transfer-Encoding, not as raw binary");
            }
        }

        foreach (KeyValuePair<string, int> name in seen)
        {
            if (name.Value > 1)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0}.{1}: sent {2} times; the spec declares one part", at, name.Key, name.Value));
            }
        }

        foreach (JsonNode? name in schema["required"]?.AsArray() ?? new JsonArray())
        {
            string key = name!.GetValue<string>();
            if (!seen.ContainsKey(key))
            {
                errors.Add($"{at}.{key}: required by the spec but not sent");
            }
        }

        return errors;
    }

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

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

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
