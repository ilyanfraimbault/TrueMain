import type { EffectiveConfigurationValueReadModel } from '#site-shared/contract/api'
import type { EffectiveConfigurationOrigin } from '~~/shared/types/ops'

interface OriginMeta {
  label: string
  color: 'neutral' | 'primary' | 'warning'
}

// Stated in words, not just colour: an operator scanning for "did someone
// override this" should not have to learn a colour code first.
const ORIGIN_META: Record<EffectiveConfigurationOrigin, OriginMeta> = {
  default: { label: 'Default', color: 'neutral' },
  override: { label: 'Override', color: 'primary' },
  derived: { label: 'Derived', color: 'warning' },
}

/** How a value's origin reads; an origin the API grows later reads as its own name. */
export function originMeta(origin: string): OriginMeta {
  return ORIGIN_META[origin as EffectiveConfigurationOrigin] ?? { label: origin, color: 'neutral' }
}

/** The origin in words, with the provider that supplied an override. */
export function originLabel(value: Pick<EffectiveConfigurationValueReadModel, 'origin' | 'source'>): string {
  const meta = originMeta(value.origin)
  return value.source ? `${meta.label} — ${value.source}` : meta.label
}
