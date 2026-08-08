using System.Collections;
using System.Globalization;

namespace Data.Configuration;

/// <summary>A rendered option value: the round-trippable form, the readable form, and how to read it.</summary>
/// <param name="Value">
/// What the option would look like back in configuration. Null when the option is absent —
/// null, blank or an empty list — so the page can say "not set" instead of drawing an empty cell.
/// </param>
/// <param name="ValueLabel">The humanised form, or null when it would just repeat <paramref name="Value"/>.</param>
/// <param name="Unit">One of <see cref="EffectiveConfigurationUnits"/>.</param>
public sealed record RenderedConfigurationValue(string? Value, string? ValueLabel, string Unit);

/// <summary>
/// Turns a bound option value into the two strings the admin page prints (#1034).
///
/// <para>
/// Everything is formatted with <see cref="CultureInfo.InvariantCulture"/>: <c>Value</c> is
/// meant to be pasteable straight back into an environment variable, and a decimal comma or a
/// thousands separator would break that. The humanised <c>ValueLabel</c> carries the reading
/// comfort instead.
/// </para>
/// </summary>
public static class ConfigurationValueRenderer
{
    private static readonly string[] ByteUnits = ["B", "kB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Renders <paramref name="value"/>. <paramref name="propertyName"/> is what tells a
    /// <c>long</c> holding bytes from a <c>long</c> holding a row count — the type cannot,
    /// and the naming convention in this codebase is consistent enough to read.
    /// </summary>
    public static RenderedConfigurationValue Render(string propertyName, object? value)
    {
        switch (value)
        {
            case null:
                return new RenderedConfigurationValue(null, null, InferUnit(propertyName, EffectiveConfigurationUnits.Text));

            case bool flag:
                return new RenderedConfigurationValue(
                    flag ? "true" : "false", null, EffectiveConfigurationUnits.Flag);

            case string text:
                return string.IsNullOrWhiteSpace(text)
                    ? new RenderedConfigurationValue(null, "empty", EffectiveConfigurationUnits.Text)
                    : new RenderedConfigurationValue(text, null, EffectiveConfigurationUnits.Text);

            // Enum before the numeric branch: an enum-typed option is a name, not a number, and
            // MainAnalysis:QueueId printed as 420 would send the reader to the Riot docs.
            case Enum enumValue:
                return new RenderedConfigurationValue(
                    enumValue.ToString(), null, EffectiveConfigurationUnits.Text);

            case TimeSpan duration:
                return new RenderedConfigurationValue(
                    duration.ToString("c", CultureInfo.InvariantCulture),
                    DescribeDuration(duration),
                    EffectiveConfigurationUnits.Duration);

            // After string, which is itself an IEnumerable.
            case IEnumerable sequence:
                return RenderSequence(sequence);
        }

        var unit = InferUnit(propertyName, EffectiveConfigurationUnits.Count);
        var rendered = Convert.ToString(value, CultureInfo.InvariantCulture);

        return new RenderedConfigurationValue(rendered, DescribeNumber(value, unit), unit);
    }

    /// <summary>
    /// Infers the unit from the property-name suffix. <paramref name="fallback"/> is what a
    /// name with no recognised suffix means for this value's type.
    /// </summary>
    private static string InferUnit(string propertyName, string fallback)
    {
        if (propertyName.EndsWith("Bytes", StringComparison.Ordinal))
        {
            return EffectiveConfigurationUnits.Bytes;
        }

        if (propertyName.EndsWith("Percent", StringComparison.Ordinal)
            || propertyName.EndsWith("Percents", StringComparison.Ordinal))
        {
            return EffectiveConfigurationUnits.Percent;
        }

        if (propertyName.EndsWith("Days", StringComparison.Ordinal)
            || propertyName.EndsWith("Hours", StringComparison.Ordinal)
            || propertyName.EndsWith("Minutes", StringComparison.Ordinal)
            || propertyName.EndsWith("Seconds", StringComparison.Ordinal))
        {
            return EffectiveConfigurationUnits.Duration;
        }

        return fallback;
    }

    private static RenderedConfigurationValue RenderSequence(IEnumerable sequence)
    {
        var items = new List<string>();
        foreach (var item in sequence)
        {
            items.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return items.Count == 0
            ? new RenderedConfigurationValue(null, "empty", EffectiveConfigurationUnits.List)
            : new RenderedConfigurationValue(
                string.Join(", ", items), null, EffectiveConfigurationUnits.List);
    }

    private static string? DescribeNumber(object value, string unit)
    {
        if (unit == EffectiveConfigurationUnits.Bytes && value is IConvertible convertibleBytes)
        {
            var bytes = convertibleBytes.ToInt64(CultureInfo.InvariantCulture);
            return bytes <= 0 ? "not set" : HumanizeBytes(bytes);
        }

        if (unit == EffectiveConfigurationUnits.Percent)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) + "%";
        }

        // Only large counts get a label: "100,000" is worth the extra column width,
        // "20" is not.
        if (unit == EffectiveConfigurationUnits.Count
            && value is IConvertible convertibleCount
            && value is not float and not double and not decimal)
        {
            var count = convertibleCount.ToInt64(CultureInfo.InvariantCulture);
            if (Math.Abs(count) >= 10_000)
            {
                return count.ToString("N0", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>
    /// Words for a <c>TimeSpan</c>. Zero or negative is the codebase's "disabled" sentinel for
    /// every retention window, so it is spelled out rather than printed as <c>00:00:00</c>.
    /// </summary>
    private static string DescribeDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "disabled";
        }

        if (duration.TotalDays >= 1 && duration.Ticks % TimeSpan.TicksPerDay == 0)
        {
            return Plural(duration.TotalDays, "day");
        }

        if (duration.TotalHours >= 1 && duration.Ticks % TimeSpan.TicksPerHour == 0)
        {
            return Plural(duration.TotalHours, "hour");
        }

        if (duration.TotalMinutes >= 1 && duration.Ticks % TimeSpan.TicksPerMinute == 0)
        {
            return Plural(duration.TotalMinutes, "minute");
        }

        return duration.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";
    }

    private static string Plural(double count, string noun)
    {
        var whole = (long)count;
        return whole == 1
            ? "1 " + noun
            : whole.ToString(CultureInfo.InvariantCulture) + " " + noun + "s";
    }

    private static string HumanizeBytes(long bytes)
    {
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < ByteUnits.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 ? "0" : "0.##";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + ByteUnits[unitIndex];
    }
}
