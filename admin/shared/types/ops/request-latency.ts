// `GET /api/ops/request-latency` (#2307): how long the Api took to answer each ops route,
// from the per-minute `request_latency_rollups` its middleware writes. p50/p95 are estimated
// from a fixed histogram of buckets and never exceed the measured max.

export type RequestLatencyWindow = '24h' | '7d'
