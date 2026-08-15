using System;
using System.Globalization;

namespace Huuray.Serialization;

/// <summary>
/// Formatting for the specification's <c>date-time</c> fields.
/// </summary>
internal static class SpecFormats
{
    /// <summary>
    /// ISO 8601 in UTC with millisecond precision — <c>2027-01-01T00:00:00.000Z</c>.
    /// </summary>
    /// <remarks>
    /// Written explicitly rather than left to the serialiser so the bytes on the wire are
    /// identical on every platform and every framework version. Offsets are normalised to
    /// UTC, which removes any question of how the API resolves a local time.
    /// </remarks>
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";

    internal static string? ToSpecDateTime(DateTimeOffset? value) =>
        value is null ? null : value.Value.ToUniversalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture);
}
