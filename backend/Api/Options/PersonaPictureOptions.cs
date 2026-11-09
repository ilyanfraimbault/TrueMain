using Data.Configuration;
using TrueMain.Configuration;

namespace TrueMain.Options;

/// <summary>
/// The credentials a known player's picture is read with (#2446), bound from <c>PersonaPictures:*</c>.
///
/// <para>
/// Off unless YouTube and Twitch are both configured: they head the priority, so a run missing one
/// would give its players a lesser channel's picture and keep it until the next refresh. All three
/// are credentials: they come from the stack's environment only, are never committed, never logged,
/// and never shown on the configuration page, whose allow-list reads <see cref="Enabled"/> in their place.
/// </para>
/// </summary>
public sealed class PersonaPictureOptions : IApiOptions
{
    public const string SectionName = "PersonaPictures";

    static string IApiOptions.Section => SectionName;

    /// <summary>
    /// Its entry on the admin configuration page. Every setting is a credential, so the include-list
    /// shows only <see cref="Enabled"/>: whether pictures are on, never what with.
    /// </summary>
    public static EffectiveConfigurationSectionDescriptor ConfigurationPage { get; } = new()
    {
        SectionName = SectionName,
        OptionsType = typeof(PersonaPictureOptions),
        Title = "Known players' pictures",
        Description = "Whether the worker that reads each known player's channel picture is running.",
        IncludeProperties = [nameof(Enabled)],
        Notices =
        [
            new EffectiveConfigurationNotice(
                nameof(Enabled),
                UnsetCondition.ZeroOrNegative,
                "Known players keep the placeholder: set PERSONA_PICTURES_YOUTUBE_API_KEY, "
                + "PERSONA_PICTURES_TWITCH_CLIENT_ID and PERSONA_PICTURES_TWITCH_CLIENT_SECRET in the stack environment.")
        ]
    };

    /// <summary>A YouTube Data API key.</summary>
    public string? YouTubeApiKey { get; set; }

    /// <summary>The client id of a Twitch application.</summary>
    public string? TwitchClientId { get; set; }

    /// <summary>The client secret of that Twitch application.</summary>
    public string? TwitchClientSecret { get; set; }

    /// <summary>Whether pictures are read: every credential is set.</summary>
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(YouTubeApiKey)
        && !string.IsNullOrWhiteSpace(TwitchClientId)
        && !string.IsNullOrWhiteSpace(TwitchClientSecret);
}
