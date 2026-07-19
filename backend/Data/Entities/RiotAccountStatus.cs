namespace Data.Entities;

public enum RiotAccountStatus
{
    /// <summary>The account resolves normally against the Riot API.</summary>
    Active = 0,

    /// <summary>
    /// The account no longer resolves by PUUID (account-v1 returns 404) and
    /// could not be recovered by Riot ID — it was deleted, banned, or the PUUID
    /// was rotated with no known GameName/TagLine to look it up. Invalid rows are
    /// kept for history but excluded from every refresh/ingest selection so the
    /// pipeline stops re-hitting the same 404.
    /// </summary>
    Invalid = 1
}
