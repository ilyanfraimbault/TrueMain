/**
 * Below this many games a build slice is thin enough that single games swing every
 * dimension. The draft tool's recommendation panel warns under it, and so does the
 * champion page's matchup slice (`?vs=`, #2355). The directory and the champion
 * page's default slice use the API's own floor (`ChampionsList:MinBuildSampleGames`,
 * the same 20), surfaced as `minSampleMet`.
 */
export const LOW_SAMPLE_GAMES = 20
