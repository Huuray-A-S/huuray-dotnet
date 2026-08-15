using System;
using System.Globalization;

namespace Huuray;

/// <summary>
/// Guards for money crossing into this SDK.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every amount in the Huuray API is an integer in minor units.</strong>
/// 50.00 is <c>5000</c>. Passing a major-unit amount orders <strong>1/100th</strong> of
/// what you meant.
/// </para>
/// <para>
/// In C# the request models type these amounts as <see cref="int"/>, so the compiler
/// rejects a fractional literal outright — a stronger guarantee than a run-time check.
/// The helpers here exist for the boundary the compiler cannot see: an amount arriving
/// as <see cref="decimal"/> or <see cref="double"/> from a database column, a
/// spreadsheet, a JSON payload, or a command-line argument.
/// </para>
/// </remarks>
public static class MinorUnits
{
    /// <summary>
    /// The explanation attached to every rejection, spelled out once.
    /// </summary>
    public const string MajorUnitWarning =
        "Amounts must be integers in minor units (50.00 is 5000). A fractional value " +
        "always means major units were passed by mistake — which would order 1/100th of " +
        "the intended amount. Note that no guard can catch every mixup: 50.00 IS the " +
        "integer 50, so it passes this check and orders 0.50.";

    /// <summary>
    /// Validates a <see cref="decimal"/> amount that is already in minor units and
    /// returns it as an <see cref="int"/>.
    /// </summary>
    /// <param name="amount">The amount, in minor units.</param>
    /// <param name="paramName">Name to report in the exception. Defaults to <c>amount</c>.</param>
    /// <returns>The same amount as an <see cref="int"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="amount"/> has a fractional part, or does not fit in an <see cref="int"/>.
    /// </exception>
    public static int FromDecimal(decimal amount, string paramName = "amount")
    {
        if (decimal.Truncate(amount) != amount)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Received {0}, which is not a whole number of minor units. {1}",
                    amount,
                    MajorUnitWarning),
                paramName);
        }

        if (amount < int.MinValue || amount > int.MaxValue)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Received {0}, which does not fit in the 32-bit integer the API declares for this field.",
                    amount),
                paramName);
        }

        return (int)amount;
    }

    /// <summary>
    /// Validates a <see cref="double"/> amount that is already in minor units and
    /// returns it as an <see cref="int"/>.
    /// </summary>
    /// <param name="amount">The amount, in minor units.</param>
    /// <param name="paramName">Name to report in the exception. Defaults to <c>amount</c>.</param>
    /// <returns>The same amount as an <see cref="int"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="amount"/> is not finite, has a fractional part, or does not fit
    /// in an <see cref="int"/>.
    /// </exception>
    public static int FromDouble(double amount, string paramName = "amount")
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "Received {0}, which is not a finite amount.", amount),
                paramName);
        }

        if (Math.Truncate(amount) != amount)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Received {0}, which is not a whole number of minor units. {1}",
                    amount,
                    MajorUnitWarning),
                paramName);
        }

        if (amount < int.MinValue || amount > int.MaxValue)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Received {0}, which does not fit in the 32-bit integer the API declares for this field.",
                    amount),
                paramName);
        }

        return (int)amount;
    }

    /// <summary>
    /// Parses an amount in minor units from text, rejecting anything fractional.
    /// </summary>
    /// <param name="text">The text to parse, in the invariant culture.</param>
    /// <param name="paramName">Name to report in the exception. Defaults to <c>text</c>.</param>
    /// <returns>The parsed amount, in minor units.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not a whole number of minor units.</exception>
    public static int Parse(string text, string paramName = "text")
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "\"{0}\" is not a number.", text),
                paramName);
        }

        return FromDecimal(parsed, paramName);
    }
}
