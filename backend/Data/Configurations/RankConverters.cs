using Core.Lol.Ranking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Data.Configurations;

/// <summary>
/// Stores a <see cref="RankTier"/> as Riot's upper-case name (<c>"DIAMOND"</c>) — the text
/// the columns already held before the enum existed, so no data migration (#239). Text per
/// the enum rule in <c>decisions/backend-conventions.md</c>: rank is read in ad-hoc SQL.
/// A named class rather than inline lambdas, so the compiled model can instantiate it.
/// </summary>
public sealed class RankTierConverter() : ValueConverter<RankTier, string>(
    tier => tier.ToRiotName(),
    value => RankTiers.ParseTier(value));

/// <summary>
/// Stores a <see cref="RankDivision"/> as its Roman numeral (<c>"II"</c>), the text the
/// columns already held (#239).
/// </summary>
/// <remarks>
/// A blank stored division reads as <see cref="RankDivision.I"/>: no writer produces one
/// any more (every ingestion path skips an entry without a division), but an apex row could
/// carry one from before, and Riot's own division for an apex tier is <c>I</c> — which every
/// frontend hides for Master and above. Reading it rather than throwing keeps such a row from
/// failing the whole page it is on.
/// </remarks>
public sealed class RankDivisionConverter() : ValueConverter<RankDivision, string>(
    division => division.ToRiotName(),
    value => string.IsNullOrWhiteSpace(value) ? RankDivision.I : RankTiers.ParseDivision(value));
