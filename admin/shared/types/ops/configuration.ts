// Effective configuration viewer — `GET /api/ops/configuration` (#1034).

/**
 * Where a bound value came from (#1034). `default` — no provider supplies the key,
 * the value is the class default. `override` — a provider supplies it; `source`
 * names which one. `derived` — no provider supplies it, yet the value differs from
 * the class default, so something computed it at boot.
 */
export type EffectiveConfigurationOrigin = 'default' | 'override' | 'derived'
