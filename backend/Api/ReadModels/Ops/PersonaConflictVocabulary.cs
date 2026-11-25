using System.Text.Json.Serialization;

namespace TrueMain.ReadModels.Ops;

/// <summary>A side of a persona conflict: the persona claiming the account, or the one holding it.</summary>
public enum PersonaConflictSide
{
    [JsonStringEnumMemberName("claimant")]
    Claimant,

    [JsonStringEnumMemberName("owner")]
    Owner
}

/// <summary>What an operator does with a conflict (#2253).</summary>
public enum PersonaConflictAction
{
    /// <summary>The account goes to the claiming persona.</summary>
    [JsonStringEnumMemberName("move")]
    Move,

    /// <summary>It stays with its holder.</summary>
    [JsonStringEnumMemberName("keep")]
    Keep,

    /// <summary>It belongs to neither.</summary>
    [JsonStringEnumMemberName("detach")]
    Detach,

    /// <summary>The two personas are one person.</summary>
    [JsonStringEnumMemberName("merge")]
    Merge
}

/// <summary>What a bulk resolution did with one conflict.</summary>
public enum PersonaConflictBulkStatus
{
    [JsonStringEnumMemberName("resolved")]
    Resolved,

    [JsonStringEnumMemberName("skipped")]
    Skipped
}

/// <summary>Why a bulk resolution left a conflict alone.</summary>
public enum PersonaConflictBulkSkipReason
{
    [JsonStringEnumMemberName("notFound")]
    NotFound,

    /// <summary>Resolved since, often by an earlier merge of the same batch.</summary>
    [JsonStringEnumMemberName("notOpen")]
    NotOpen,

    [JsonStringEnumMemberName("differingChannel")]
    DifferingChannel,

    /// <summary>Under the floor, or no shared social channel.</summary>
    [JsonStringEnumMemberName("belowFloor")]
    BelowFloor,

    /// <summary>One of the known players was merged or removed since.</summary>
    [JsonStringEnumMemberName("changed")]
    Changed
}

/// <summary>Why an account is excluded from a known player.</summary>
public enum PersonaExclusionReason
{
    /// <summary>Lost to another persona in a resolved conflict.</summary>
    [JsonStringEnumMemberName("conflict")]
    Conflict,

    /// <summary>Unlinked by an operator.</summary>
    [JsonStringEnumMemberName("unlinked")]
    Unlinked,

    /// <summary>Taken down on request.</summary>
    [JsonStringEnumMemberName("takedown")]
    Takedown
}
