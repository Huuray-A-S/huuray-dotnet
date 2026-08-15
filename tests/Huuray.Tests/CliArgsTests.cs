using System;
using System.Collections.Generic;
using Huuray.Cli;
using Xunit;

namespace Huuray.Tests;

public class CliParsingTests
{
    [Fact]
    public void ReadsABareCommand()
    {
        ParsedArgs parsed = CliArgs.Parse(new[] { "balance" });

        Assert.Equal("balance", parsed.Command);
        Assert.Empty(parsed.Flags);
    }

    [Fact]
    public void ReadsACommandWithABooleanFlag()
    {
        ParsedArgs parsed = CliArgs.Parse(new[] { "catalogue", "--all" });

        Assert.Equal("catalogue", parsed.Command);
        Assert.True(CliArgs.HasFlag(parsed.Flags, "all"));
    }

    [Fact]
    public void ReadsACommandWithAValuedFlag()
    {
        ParsedArgs parsed = CliArgs.Parse(new[] { "stock", "--token", "abc" });

        Assert.Equal("stock", parsed.Command);
        Assert.Equal("abc", CliArgs.OptionalString(parsed.Flags, "token"));
    }

    [Fact]
    public void HandlesAFlagBeforeTheCommand()
    {
        // Regression: --help used to be swallowed as the command name, so
        // `huuray --help` demanded credentials before printing usage.
        ParsedArgs parsed = CliArgs.Parse(new[] { "--help" });

        Assert.Null(parsed.Command);
        Assert.True(CliArgs.WantsHelp(parsed.Flags));
    }

    [Fact]
    public void FindsTheCommandEvenWhenFlagsComeFirst()
    {
        Assert.Equal("balance", CliArgs.Parse(new[] { "--json", "balance" }).Command);
    }

    [Fact]
    public void SupportsTheShortHelpFlag()
    {
        Assert.True(CliArgs.WantsHelp(CliArgs.Parse(new[] { "-h" }).Flags));
        Assert.False(CliArgs.WantsHelp(CliArgs.Parse(new[] { "balance" }).Flags));
    }

    [Fact]
    public void RejectsAValuedFlagWithNoValueInsteadOfSilentlyDegrading()
    {
        // `huuray search --ref-id --json` must not quietly run a FILTERLESS search: the
        // user typed a filter, so dropping it changes which API query is sent.
        Assert.Throws<HuurayException>(() => CliArgs.Parse(new[] { "search", "--ref-id" }));
        Assert.Throws<HuurayException>(() => CliArgs.Parse(new[] { "search", "--ref-id", "--json" }));
    }

    [Fact]
    public void SupportsGnuFlagEqualsValueSyntax()
    {
        Assert.Equal("abc", CliArgs.OptionalString(CliArgs.Parse(new[] { "search", "--ref-id=abc" }).Flags, "ref-id"));

        ParsedArgs rates = CliArgs.Parse(new[] { "rates", "--from=EUR", "--to", "DKK" });
        Assert.Equal("EUR", CliArgs.OptionalString(rates.Flags, "from"));
        Assert.Equal("DKK", CliArgs.OptionalString(rates.Flags, "to"));
    }

    [Fact]
    public void RejectsAValueOnABooleanFlag()
    {
        HuurayException error = Assert.Throws<HuurayException>(() =>
            CliArgs.Parse(new[] { "catalogue", "--all=yes" }));

        Assert.Contains("does not take a value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsNegativeNumbersAsFlagValues()
    {
        ParsedArgs parsed = CliArgs.Parse(new[] { "stock", "--token", "x", "--value", "-500" });

        Assert.Equal("x", CliArgs.OptionalString(parsed.Flags, "token"));
        Assert.Equal("-500", CliArgs.OptionalString(parsed.Flags, "value"));
    }

    [Fact]
    public void RejectsUnknownFlagsInsteadOfIgnoringThem()
    {
        HuurayException error = Assert.Throws<HuurayException>(() =>
            CliArgs.Parse(new[] { "balance", "--verbose" }));

        Assert.Contains("Unknown option --verbose", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsHyphenatedFlagNamesIntact()
    {
        ParsedArgs parsed = CliArgs.Parse(new[] { "search", "--ref-id", "payroll-2026-08" });

        Assert.Equal("payroll-2026-08", CliArgs.OptionalString(parsed.Flags, "ref-id"));
    }

    [Fact]
    public void ReturnsNoCommandForEmptyArgv()
    {
        Assert.Null(CliArgs.Parse(Array.Empty<string>()).Command);
    }
}

public class CliFlagReaderTests
{
    [Fact]
    public void RequireFlagExplainsWhatIsMissing()
    {
        Dictionary<string, string?> empty = new(StringComparer.Ordinal);
        Dictionary<string, string?> boolean = new(StringComparer.Ordinal) { ["token"] = null };
        Dictionary<string, string?> valued = new(StringComparer.Ordinal) { ["token"] = "abc" };

        Assert.Contains("--token", Assert.Throws<HuurayException>(() => CliArgs.RequireFlag(empty, "token")).Message, StringComparison.Ordinal);
        Assert.Contains("--token", Assert.Throws<HuurayException>(() => CliArgs.RequireFlag(boolean, "token")).Message, StringComparison.Ordinal);
        Assert.Equal("abc", CliArgs.RequireFlag(valued, "token"));
    }

    [Fact]
    public void OptionalIntRejectsANonIntegerRatherThanSilentlyTruncating()
    {
        Dictionary<string, string?> fractional = new(StringComparer.Ordinal) { ["voucher-id"] = "50.5" };
        Dictionary<string, string?> whole = new(StringComparer.Ordinal) { ["voucher-id"] = "5000" };
        Dictionary<string, string?> empty = new(StringComparer.Ordinal);

        Assert.Throws<HuurayException>(() => CliArgs.OptionalInt(fractional, "voucher-id"));
        Assert.Equal(5000, CliArgs.OptionalInt(whole, "voucher-id"));
        Assert.Null(CliArgs.OptionalInt(empty, "voucher-id"));
    }

    [Fact]
    public void OptionalMinorUnitsExplainsAFractionalAmount()
    {
        Dictionary<string, string?> fractional = new(StringComparer.Ordinal) { ["value"] = "50.5" };

        HuurayException error = Assert.Throws<HuurayException>(() =>
            CliArgs.OptionalMinorUnits(fractional, "value"));

        Assert.Contains("1/100th of the intended amount", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalStringIgnoresBooleanFlags()
    {
        Dictionary<string, string?> boolean = new(StringComparer.Ordinal) { ["ref-id"] = null };
        Dictionary<string, string?> valued = new(StringComparer.Ordinal) { ["ref-id"] = "r" };

        Assert.Null(CliArgs.OptionalString(boolean, "ref-id"));
        Assert.Equal("r", CliArgs.OptionalString(valued, "ref-id"));
    }
}

public class CliTableTests
{
    [Fact]
    public void SaysSoPlainlyWhenThereIsNothingToShow()
    {
        Assert.Equal("(no results)", CliArgs.Table(Array.Empty<IReadOnlyList<KeyValuePair<string, string>>>()));
    }

    [Fact]
    public void AlignsColumnsAndIncludesAHeaderRule()
    {
        string table = CliArgs.Table(new IReadOnlyList<KeyValuePair<string, string>>[]
        {
            new[]
            {
                new KeyValuePair<string, string>("currency", "DKK"),
                new KeyValuePair<string, string>("balance", "50000"),
            },
            new[]
            {
                new KeyValuePair<string, string>("currency", "EUR"),
                new KeyValuePair<string, string>("balance", "1234"),
            },
        });

        string[] lines = table.Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.Matches("^currency\\s+balance$", lines[0]);
        Assert.Matches("^─+\\s+─+$", lines[1]);
        Assert.Equal("DKK       50000", lines[2]);
    }
}
