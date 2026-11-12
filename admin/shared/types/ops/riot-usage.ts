// Riot API usage, rate limits and headroom — `GET /api/ops/riot-usage`.

/** Relative window for `GET /api/ops/riot-usage`. Also fixes the chart bucket size. */
export type RiotUsageWindow = '1h' | '24h' | '7d' | '30d'
