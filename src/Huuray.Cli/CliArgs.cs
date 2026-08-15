using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Huuray.Cli;

/// <summary>
/// One parsed command line: the command, and the flags that came with it.
/// </summary>
/// <remarks>
/// A flag present with a <see langword="null"/> value is a boolean flag such as
/// <c>--json</c>; a flag with a string value is one that takes an argument.
/// </remarks>
internal sealed record ParsedArgs(string? Command, IReadOnlyDictionary<string, string?> Flags);

/// <summary>
/// Argument parsing for the read-only CLI.
/// </summary>
internal static class CliArgs
{
    /// <summary>Flags that never take a value.</summary>
    private static readonly HashSet<string> BooleanFlags =
        new(StringComparer.Ordinal) { "json", "all", "help", "h" };

    /// <summary>
    /// Flags that always take a value.
    /// </summary>
    /// <remarks>
    /// Declared explicitly so a missing value is an error, never a silent downgrade:
    /// <c>huuray search --ref-id --json</c> must not quietly run a filterless search —
    /// the user typed a filter, so dropping it changes which API query is sent.
    /// </remarks>
    private static readonly HashSet<string> ValuedFlags =
        new(StringComparer.Ordinal) { "token", "value", "from", "to", "ref-id", "order-uid", "voucher-id" };

    /// <summary>
    /// Parses <paramref name="argv"/> into a command and flags.
    /// </summary>
    /// <remarks>
    /// Flags may appear anywhere, including before the command. Both <c>--flag value</c>
    /// and <c>--flag=value</c> are accepted. A valued flag with no value, or a flag the
    /// CLI does not know, is an error rather than a guess.
    /// </remarks>
    internal static ParsedArgs Parse(IReadOnlyList<string> argv)
    {
        Dictionary<string, string?> flags = new(StringComparer.Ordinal);
        List<string> positionals = new();

        for (int i = 0; i < argv.Count; i++)
        {
            string arg = argv[i];
            if (!arg.StartsWith('-'))
            {
                positionals.Add(arg);
                continue;
            }

            string key = TrimDashes(arg);
            string? inlineValue = null;
            int equals = key.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                inlineValue = key[(equals + 1)..];
                key = key[..equals];
            }

            if (BooleanFlags.Contains(key))
            {
                if (inlineValue is not null)
                {
                    throw new HuurayException($"Option --{key} does not take a value.");
                }

                flags[key] = null;
                continue;
            }

            if (ValuedFlags.Contains(key))
            {
                if (inlineValue is not null)
                {
                    flags[key] = inlineValue;
                    continue;
                }

                string? next = i + 1 < argv.Count ? argv[i + 1] : null;

                // The next token is the value even when it starts with '-', so negative
                // numbers work; only a missing token or another known flag is an error.
                if (next is null || NameOf(next) == key || IsKnownFlag(next))
                {
                    throw new HuurayException($"Option --{key} requires a value. Run \"huuray --help\".");
                }

                flags[key] = next;
                i++;
                continue;
            }

            throw new HuurayException($"Unknown option --{key}. Run \"huuray --help\".");
        }

        return new ParsedArgs(positionals.Count > 0 ? positionals[0] : null, flags);
    }

    internal static bool WantsHelp(IReadOnlyDictionary<string, string?> flags) =>
        flags.ContainsKey("help") || flags.ContainsKey("h");

    internal static bool HasFlag(IReadOnlyDictionary<string, string?> flags, string name) =>
        flags.ContainsKey(name);

    internal static string RequireFlag(IReadOnlyDictionary<string, string?> flags, string name)
    {
        if (!flags.TryGetValue(name, out string? value) || string.IsNullOrEmpty(value))
        {
            throw new HuurayException($"Missing required option --{name}. Run \"huuray --help\".");
        }

        return value;
    }

    internal static string? OptionalString(IReadOnlyDictionary<string, string?> flags, string name) =>
        flags.TryGetValue(name, out string? value) ? value : null;

    internal static int? OptionalInt(IReadOnlyDictionary<string, string?> flags, string name)
    {
        string? raw = OptionalString(flags, name);
        if (raw is null)
        {
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            throw new HuurayException($"--{name} must be an integer, got \"{raw}\".");
        }

        return parsed;
    }

    /// <summary>
    /// Reads an amount in minor units, refusing anything fractional.
    /// </summary>
    /// <remarks>
    /// This is the one place the CLI takes money from a human, so it goes through the
    /// same guard the library exposes rather than a plain integer parse — the error
    /// explains what went wrong instead of just refusing.
    /// </remarks>
    internal static int? OptionalMinorUnits(IReadOnlyDictionary<string, string?> flags, string name)
    {
        string? raw = OptionalString(flags, name);
        if (raw is null)
        {
            return null;
        }

        try
        {
            return MinorUnits.Parse(raw, name);
        }
        catch (ArgumentException exception)
        {
            throw new HuurayException($"--{name}: {exception.Message}", exception);
        }
    }

    /// <summary>Minimal fixed-width table. Kept local so the tool ships no dependencies.</summary>
    internal static string Table(IReadOnlyList<IReadOnlyList<KeyValuePair<string, string>>> rows)
    {
        if (rows.Count == 0)
        {
            return "(no results)";
        }

        List<string> columns = new();
        foreach (IReadOnlyList<KeyValuePair<string, string>> row in rows)
        {
            foreach (KeyValuePair<string, string> cell in row)
            {
                if (!columns.Contains(cell.Key))
                {
                    columns.Add(cell.Key);
                }
            }
        }

        Dictionary<string, int> widths = new(StringComparer.Ordinal);
        foreach (string column in columns)
        {
            int width = column.Length;
            foreach (IReadOnlyList<KeyValuePair<string, string>> row in rows)
            {
                width = Math.Max(width, ValueFor(row, column).Length);
            }

            widths[column] = width;
        }

        StringBuilder builder = new();
        AppendLine(builder, columns, column => column, widths);
        AppendLine(builder, columns, column => new string('─', widths[column]), widths);

        foreach (IReadOnlyList<KeyValuePair<string, string>> row in rows)
        {
            AppendLine(builder, columns, column => ValueFor(row, column), widths);
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static void AppendLine(
        StringBuilder builder,
        IReadOnlyList<string> columns,
        Func<string, string> cell,
        IReadOnlyDictionary<string, int> widths)
    {
        StringBuilder line = new();
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                line.Append("  ");
            }

            line.Append(cell(columns[i]).PadRight(widths[columns[i]]));
        }

        builder.Append(line.ToString().TrimEnd()).Append('\n');
    }

    private static string ValueFor(IReadOnlyList<KeyValuePair<string, string>> row, string column)
    {
        foreach (KeyValuePair<string, string> cell in row)
        {
            if (string.Equals(cell.Key, column, StringComparison.Ordinal))
            {
                return cell.Value;
            }
        }

        return string.Empty;
    }

    private static bool IsKnownFlag(string token)
    {
        if (!token.StartsWith('-'))
        {
            return false;
        }

        string name = NameOf(token);
        return BooleanFlags.Contains(name) || ValuedFlags.Contains(name);
    }

    private static string NameOf(string token)
    {
        string trimmed = TrimDashes(token);
        int equals = trimmed.IndexOf('=', StringComparison.Ordinal);
        return equals >= 0 ? trimmed[..equals] : trimmed;
    }

    private static string TrimDashes(string token) =>
        token.StartsWith("--", StringComparison.Ordinal) ? token[2..]
        : token.StartsWith('-') ? token[1..]
        : token;
}
