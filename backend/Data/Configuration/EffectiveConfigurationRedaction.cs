namespace Data.Configuration;

/// <summary>
/// The belt to the allow-list's braces (#1034): a name-shaped test for "this property holds
/// a credential".
///
/// <para>
/// The section allow-list is what actually keeps secrets out of the response — this is the
/// backstop for a mis-authored catalog entry, and the predicate a guard test uses to fail the
/// build when a newly exposed section carries a credential. It is deliberately blunt: a false
/// positive costs one hidden knob, a false negative costs the Riot key.
/// </para>
/// </summary>
public static class EffectiveConfigurationRedaction
{
    private static readonly string[] SecretFragments =
    [
        "password",
        "secret",
        "token",
        "credential",
        "passphrase",
        "connectionstring",
        "apikey",
        "privatekey"
    ];

    /// <summary>
    /// True when <paramref name="propertyName"/> looks like it holds a credential: it contains
    /// one of the known fragments, or it ends in <c>Key</c> (which catches <c>ApiKey</c>,
    /// <c>SigningKey</c> and anything else shaped like them).
    /// </summary>
    public static bool IsSecretName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        foreach (var fragment in SecretFragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return propertyName.EndsWith("Key", StringComparison.Ordinal);
    }
}
