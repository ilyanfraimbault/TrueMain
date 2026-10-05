<script setup lang="ts">
import type { DamageProfile } from '~/utils/damage-profile'
import type { DamageKind } from '~/utils/draft-damage'
import { teamDamage } from '~/utils/damage-profile'
import { dominant, isUsable } from '~/utils/draft-damage'

/**
 * One team's damage mix in champion select (#1907): physical, magic and true
 * damage as one thin bar, the type it deals most of as the only figure, the
 * pick-by-pick split on hover. Each pick weighs by the damage it deals per
 * game — the item-context fold's own arithmetic (`utils/draft-damage`).
 *
 * Locked picks only, plus our own pick being weighed as a preview: the bar is
 * drawn with it, hatched, and a tick marks where the mix stood without it.
 * An unmeasured pick is listed, never given a split; two of them on a side
 * leave the bar without a figure, as they leave the build advice without an axis.
 */
const props = defineProps<{
  team: 'ally' | 'enemy'
  picks: { championId: number, position: string | null, preview?: boolean }[]
}>()

const { profileOf } = useDamageProfiles()
const { nameOf } = useChampionStatics()

const rows = computed(() => props.picks.map(pick => ({ ...pick, profile: profileOf(pick.championId, pick.position) })))
const preview = computed(() => rows.value.find(row => row.preview) ?? null)

const mix = computed(() => teamDamage(rows.value.map(row => row.profile)))
const withoutPreview = computed(() => (preview.value ? teamDamage(rows.value.filter(row => !row.preview).map(row => row.profile)) : null))
const usable = computed(() => isUsable(mix.value))
const lead = computed(() => (isUsable(mix.value) ? dominant(mix.value) : null))

const SEGMENT: Record<DamageKind, string> = {
  physical: 'bg-(--color-stat-ad)',
  magic: 'bg-(--color-stat-mr)',
  true: 'bg-(--color-stat-true)',
}
const TEXT: Record<DamageKind, string> = {
  physical: 'text-stat-ad',
  magic: 'text-stat-mr',
  true: 'text-stat-true',
}

const segments = computed(() => {
  const team = mix.value
  if (!isUsable(team)) return []
  return ([['physical', team.physicalShare], ['magic', team.magicShare], ['true', team.trueShare]] as [DamageKind, number][])
    .filter(([, share]) => share > 0.005)
    .map(([kind, share]) => ({ kind, width: `${share * 100}%` }))
})

/** Where the physical share stood before the preview, when both are stated. */
const tick = computed(() => (preview.value && isUsable(withoutPreview.value) ? `${withoutPreview.value.physicalShare * 100}%` : null))

const percent = (share: number | null) => (share === null ? '–' : `${Math.round(share * 100)}%`)

/** A pick's line in the hover: its split, or why it has none. */
function detail(profile: DamageProfile | null) {
  if (!profile) return 'unmeasured'
  if (profile.source === 'fallback') return profile.damageClass ? `unmeasured · ${profile.damageClass}` : 'unmeasured'
  return null
}

const label = computed(() => {
  const side = props.team === 'ally' ? 'Your team' : 'Enemy team'
  if (!lead.value) return `${side}: damage mix not known yet`
  return `${side}: ${percent(lead.value.share)} ${lead.value.kind} damage`
})
</script>

<template>
  <UPopover mode="hover" :open-delay="150" :content="{ side: 'bottom', align: team === 'ally' ? 'start' : 'end' }">
    <div class="flex h-5 cursor-default items-center gap-2" :class="team === 'enemy' && 'flex-row-reverse'" role="img" :aria-label="label">
      <div class="relative h-1.5 flex-1 overflow-hidden rounded-full bg-elevated">
        <div class="flex h-full" :class="preview && 'opacity-80'">
          <div
            v-for="segment in segments"
            :key="segment.kind"
            class="h-full transition-[width] duration-300"
            :class="[SEGMENT[segment.kind], preview && 'bg-[repeating-linear-gradient(135deg,transparent_0_3px,rgb(0_0_0/0.35)_3px_5px)] bg-blend-multiply']"
            :style="{ width: segment.width }"
          />
        </div>
        <div v-if="tick" class="absolute inset-y-0 w-px bg-ink-950" :style="{ left: tick }" />
      </div>
      <span v-if="lead" class="shrink-0 text-xs tabular-nums text-muted">
        {{ percent(lead.share) }} <span :class="TEXT[lead.kind]">{{ lead.kind === 'true' ? 'true' : lead.kind === 'physical' ? 'AD' : 'AP' }}</span>
      </span>
      <span v-else-if="rows.length" class="shrink-0 text-xs text-dimmed">–</span>
    </div>

    <template #content>
      <div class="w-72 space-y-1.5 p-2.5">
        <p class="stat-label">{{ team === 'ally' ? 'Your team' : 'Enemy team' }} · damage</p>
        <p v-if="!rows.length" class="text-xs text-muted">No pick locked yet.</p>
        <div v-for="row in rows" :key="row.championId" class="flex items-center gap-2" :class="row.preview && 'opacity-70'">
          <ChampionPortrait :champion-id="row.championId" size="sm" class="size-6! shrink-0 rounded!" />
          <span class="min-w-0 flex-1 truncate text-xs text-highlighted">
            {{ nameOf(row.championId) }}
            <span v-if="row.preview" class="text-dimmed">· your pick</span>
            <UBadge v-if="row.profile?.flexDamage" color="neutral" variant="soft" size="sm" class="ml-1 px-1 py-0 text-[10px]" title="Built both ways: this is the blend of its AD and AP games">AD/AP</UBadge>
          </span>
          <span v-if="detail(row.profile)" class="shrink-0 text-[11px] text-dimmed">{{ detail(row.profile) }}</span>
          <span v-else class="shrink-0 text-[11px] tabular-nums">
            <span class="text-stat-ad">{{ percent(row.profile!.physicalShare) }}</span>
            <span class="text-dimmed"> / </span>
            <span class="text-stat-mr">{{ percent(row.profile!.magicShare) }}</span>
            <template v-if="(row.profile!.trueShare ?? 0) >= 0.05">
              <span class="text-dimmed"> / </span>
              <span class="text-stat-true">{{ percent(row.profile!.trueShare) }}</span>
            </template>
          </span>
        </div>
        <p v-if="rows.length && !usable" class="pt-1 text-[11px] text-dimmed">{{ (mix?.unmeasured ?? 0) >= 2 ? 'Two picks are not measured: no team figure.' : 'No measured pick yet.' }}</p>
        <p class="pt-1 text-[11px] text-dimmed">Physical / magic / true, each pick weighted by the damage it deals. High-elo games.</p>
      </div>
    </template>
  </UPopover>
</template>
