using System;
using AutomationMonitor.Core.Providers.Dataverse;
using Xunit;

namespace AutomationMonitor.Core.Tests;

/// <summary>
/// What a source value is allowed to mean. The plugin's SDK adapter unwraps the provider shapes it
/// owns (an aliased value, an option set) and hands the plain remainder here, so these are the rules
/// that decided the first real Windows session: a direct string column must be readable as text, a
/// genuinely aliased column must keep working, and a value nobody understands must be named instead
/// of quietly blanked or cast into an exception.
/// </summary>
public class DataverseValueTests
{
    [Fact]
    public void DirectStringColumn_IsReadAsText()
    {
        Assert.Equal("Capybara_Monitor", DataverseValue.AsText("Capybara_Monitor", "uniquename"));
    }

    [Fact]
    public void AliasedStringColumn_ReachesTheReaderAsTheSamePlainString()
    {
        object? unwrapped = AliasedValueProbe.Unwrap("publisher_Capybara");

        Assert.Equal("publisher_Capybara", DataverseValue.AsText(unwrapped, "solution.uniquename"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void BlankText_IsNotStatedRatherThanAnEmptyName(string? value)
    {
        Assert.Null(DataverseValue.AsText(value, "friendlyname"));
    }

    [Fact]
    public void AbsentOrNullValue_IsNotStatedAndNeverThrows()
    {
        Assert.Null(DataverseValue.AsText(null, "friendlyname"));
        Assert.Null(DataverseValue.AsInt32(null, "category"));
        Assert.Null(DataverseValue.AsInt64(null, "duration"));
        Assert.Null(DataverseValue.AsBoolean(null, "ismanaged"));
        Assert.Null(DataverseValue.AsUtc(null, "installedon"));
    }

    [Fact]
    public void UnexpectedTypeInATextColumn_NamesTheColumnAndTheShape()
    {
        DataverseMappingException error = Assert.Throws<DataverseMappingException>(
            () => DataverseValue.AsText(Guid.Empty, "uniquename"));

        Assert.Equal("uniquename", error.Attribute);
        Assert.Equal("Guid", error.ActualType);
        Assert.Contains("uniquename", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticNeverQuotesTheValueItCouldNotRead()
    {
        Guid value = new Guid("a6f0b0e0-1111-2222-3333-444444444444");

        DataverseMappingException error = Assert.Throws<DataverseMappingException>(
            () => DataverseValue.AsText(value, "description"));

        // The type is safe to show; the content may be business data and must not reach a log or a grid.
        Assert.DoesNotContain("a6f0b0e0", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Guid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextValueThatIsNotTextForThisReader_IsStillNamedByColumn()
    {
        DataverseMappingException error = Assert.Throws<DataverseMappingException>(
            () => DataverseValue.AsBoolean("true", "ismanaged"));

        Assert.Equal("ismanaged", error.Attribute);
        Assert.Equal("String", error.ActualType);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(-1)]
    public void WholeNumberColumns_AcceptAnIntegerShape(int value)
    {
        Assert.Equal(value, DataverseValue.AsInt32(value, "category"));
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData(" 5 ", 5)]
    [InlineData("-2", -2)]
    public void WholeNumberColumns_AcceptNumericTextFromATextTypedColumn(string? text, int expected)
    {
        Assert.Equal(expected, DataverseValue.AsInt32(text, "category"));
    }

    [Fact]
    public void Int32RefusesALongThatDoesNotFit_InsteadOfOverflowing()
    {
        long tooBig = int.MaxValue + 1L;

        DataverseMappingException error = Assert.Throws<DataverseMappingException>(
            () => DataverseValue.AsInt32(tooBig, "category"));

        Assert.Equal("category", error.Attribute);
    }

    [Fact]
    public void Int64KeepsADurationLargerThanInt32CanHold()
    {
        const long ms = 1234567890123L;

        Assert.Equal(ms, DataverseValue.AsInt64(ms, "duration"));
        Assert.Equal(42L, DataverseValue.AsInt64(42, "duration"));
        Assert.Equal(ms, DataverseValue.AsInt64(ms.ToString(), "duration"));
    }

    [Fact]
    public void ALegendInANumberColumnStatesNoNumber_WithoutErasingTheRow()
    {
        // A category column that answers with a label instead of an option value has not stated a
        // number. Null keeps the row and its exclusion count visible; guessing would invent a fact.
        Assert.Null(DataverseValue.AsInt32("Application", "category"));
    }

    [Fact]
    public void AForeignShapeInANumberColumn_IsADiagnostic()
    {
        DataverseMappingException error = Assert.Throws<DataverseMappingException>(
            () => DataverseValue.AsInt32(true, "category"));

        Assert.Equal("category", error.Attribute);
        Assert.Equal("Boolean", error.ActualType);
    }

    [Fact]
    public void BooleanColumns_AcceptOnlyABooleanShape()
    {
        Assert.True(DataverseValue.AsBoolean(true, "ismanaged"));
        Assert.False(DataverseValue.AsBoolean(false, "ismanaged"));
    }

    [Fact]
    public void APicklistColumnKeepsItsNumericEvidence_InsteadOfAFabricatedFlag()
    {
        // flowrun.isprimary is a Picklist. The option value travels in its own field, so the Boolean
        // view stays null rather than claiming true for an option this tool has not confirmed.
        Assert.Null(DataverseValue.AsBooleanWhenBoolean(1));
        Assert.Null(DataverseValue.AsBooleanWhenBoolean(0));
        Assert.True(DataverseValue.AsBooleanWhenBoolean(true));
        Assert.False(DataverseValue.AsBooleanWhenBoolean(false));
        Assert.Null(DataverseValue.AsBooleanWhenBoolean(null));
        Assert.Null(DataverseValue.AsBooleanWhenBoolean("Succeeded"));
    }

    [Fact]
    public void UnspecifiedDateTime_IsTheStoredUtcInstant()
    {
        DateTime stored = new DateTime(2026, 3, 4, 1, 2, 3, DateTimeKind.Unspecified);

        DateTimeOffset? read = DataverseValue.AsUtc(stored, "installedon");

        Assert.NotNull(read);
        Assert.Equal(TimeSpan.Zero, read!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 1, 2, 3, TimeSpan.Zero), read.Value);
    }

    [Fact]
    public void OffsetInstant_IsNormalisedToUtcAndNeverKeptAsLocalTime()
    {
        DateTimeOffset withOffset = new DateTimeOffset(2026, 10, 1, 5, 6, 7, TimeSpan.FromHours(2));

        DateTimeOffset? read = DataverseValue.AsUtc(withOffset, "modifiedon");

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 3, 6, 7, TimeSpan.Zero), read);
        Assert.Equal(withOffset.UtcDateTime, read!.Value.UtcDateTime);
    }

    [Fact]
    public void LocalDateTime_BecomesTheSameInstantExpressedInUtc()
    {
        DateTime local = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Local);

        DateTimeOffset? read = DataverseValue.AsUtc(local, "createdon");

        Assert.NotNull(read);
        Assert.Equal(TimeSpan.Zero, read!.Value.Offset);
        Assert.Equal(local.ToUniversalTime(), read.Value.UtcDateTime);
    }

    [Fact]
    public void TextInADateColumn_IsADiagnosticNotASilentNull()
    {
        DataverseMappingException error = Assert.Throws<DataverseMappingException>(
            () => DataverseValue.AsUtc("2026-10-01", "modifiedon"));

        Assert.Equal("modifiedon", error.Attribute);
        Assert.Equal("String", error.ActualType);
    }

    [Fact]
    public void MappingDiagnosticNamesThisToolSoTheClassifierCanTellItApart()
    {
        DataverseMappingException error = new DataverseMappingException("uniquename", "text", "Guid");

        Assert.Contains("Automation Monitor could not read", error.Message, StringComparison.Ordinal);
        Assert.Contains("uniquename", error.Message, StringComparison.Ordinal);
        Assert.Equal("text", error.Expected);
        Assert.IsAssignableFrom<InvalidOperationException>(error);
    }

    /// <summary>
    /// Stands in for the provider wrapper the plugin's adapter unwraps: whatever the shape on the wire,
    /// the value that reaches these readers is the plain inner one. The SDK types themselves cannot be
    /// referenced from a net10.0 test - see docs/provider-capabilities.md for the probe that exercises
    /// the real adapter on .NET Framework.
    /// </summary>
    private static class AliasedValueProbe
    {
        public static object? Unwrap(object? wrapped)
        {
            return wrapped;
        }
    }
}
