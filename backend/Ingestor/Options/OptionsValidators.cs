using Core.Options;
using Microsoft.Extensions.Options;

namespace Ingestor.Options;

// Source-generated validators (#271): each one checks the data-annotation attributes declared on
// its options class at compile time — no reflection over the attributes at boot. Cross-field and
// otherwise non-declarative rules stay as Validate(...) lambdas in OptionsConfigurationExtensions,
// and both kinds run together under ValidateOnStart().
//
// The attributes carry no ErrorMessage on purpose: the generator substitutes its own copy of
// RangeAttribute, which always formats the stock message ("The field ScoringOptions.BatchSize must
// be between 1 and 2147483647.") and ignores a custom one. That message already names the class and
// property, so the boot error stays actionable; what a bound means lives in each property's docs.

[OptionsValidator]
internal sealed partial class RiotOptionsValidator : IValidateOptions<RiotOptions>;

[OptionsValidator]
internal sealed partial class RiotRateLimitOptionsValidator : IValidateOptions<RiotRateLimitOptions>;

[OptionsValidator]
internal sealed partial class CommunityDragonOptionsValidator : IValidateOptions<CommunityDragonOptions>;

[OptionsValidator]
internal sealed partial class DiscoveryOptionsValidator : IValidateOptions<DiscoveryOptions>;

[OptionsValidator]
internal sealed partial class LadderSyncOptionsValidator : IValidateOptions<LadderSyncOptions>;

[OptionsValidator]
internal sealed partial class ManualSeedOptionsValidator : IValidateOptions<ManualSeedOptions>;

[OptionsValidator]
internal sealed partial class ScoringOptionsValidator : IValidateOptions<ScoringOptions>;

[OptionsValidator]
internal sealed partial class HarvestOptionsValidator : IValidateOptions<HarvestOptions>;

[OptionsValidator]
internal sealed partial class CoverageOptionsValidator : IValidateOptions<CoverageOptions>;

[OptionsValidator]
internal sealed partial class MatchIngestionOptionsValidator : IValidateOptions<MatchIngestionOptions>;

[OptionsValidator]
internal sealed partial class MainActivityOptionsValidator : IValidateOptions<MainActivityOptions>;

[OptionsValidator]
internal sealed partial class MainAnalysisOptionsValidator : IValidateOptions<MainAnalysisOptions>;

[OptionsValidator]
internal sealed partial class AccountRefreshOptionsValidator : IValidateOptions<AccountRefreshOptions>;

[OptionsValidator]
internal sealed partial class MatchDataRetentionOptionsValidator : IValidateOptions<MatchDataRetentionOptions>;

[OptionsValidator]
internal sealed partial class CandidatePruningOptionsValidator : IValidateOptions<CandidatePruningOptions>;

[OptionsValidator]
internal sealed partial class IntakeOptionsValidator : IValidateOptions<IntakeOptions>;

[OptionsValidator]
internal sealed partial class MatchupLeadAggregationOptionsValidator : IValidateOptions<MatchupLeadAggregationOptions>;

[OptionsValidator]
internal sealed partial class SynergyAggregationOptionsValidator : IValidateOptions<SynergyAggregationOptions>;

[OptionsValidator]
internal sealed partial class LaneOutcomeAggregationOptionsValidator : IValidateOptions<LaneOutcomeAggregationOptions>;

[OptionsValidator]
internal sealed partial class BanAggregationOptionsValidator : IValidateOptions<BanAggregationOptions>;

[OptionsValidator]
internal sealed partial class ChampionProfileAggregationOptionsValidator : IValidateOptions<ChampionProfileAggregationOptions>;

[OptionsValidator]
internal sealed partial class ItemContextAggregationOptionsValidator : IValidateOptions<ItemContextAggregationOptions>;
