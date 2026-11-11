namespace Data.Logging;

/// <summary>Bounds free text before it is stored in an operational document.</summary>
public static class StoredText
{
    /// <summary>The longest error message a run or a seed request keeps.</summary>
    public const int MaxErrorLength = 2048;

    /// <summary><paramref name="value"/> cut to <paramref name="maxLength"/> characters, unchanged when shorter.</summary>
    public static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
