<script setup lang="ts">
import type { LeaderboardTopChampion } from '~~/shared/types/leaderboard'
import type { StaticItemData, StaticPerkData, StaticPerkStyleData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

// The "this is what they main" cluster used on the truemains leaderboard and
// the homepage teaser: champion icon + play rate + the player's keystone (with
// secondary tree overlay) + their first item — mirroring how the champion list
// renders a top build. Build ids are resolved to icon objects by the caller
// (via useBuildAssets) so this component stays free of data fetching.
//
// When `nameTag` is set the whole cluster becomes one link to that player's
// scoped champion page (`/truemains/{nameTag}/champions/{id}`) — clicking
// anywhere on it (icon, play rate, runes, item) navigates there. The inner
// rune/item tooltips resolve to non-button spans, so the click still bubbles
// to the link. Omit `nameTag` (homepage, where the whole row is already a
// link) to render a plain, non-interactive cluster.
const props = withDefaults(defineProps<{
  champion: LeaderboardTopChampion
  name: string
  iconUrl: string | null
  keystone: StaticPerkData | null
  secondaryStyle: StaticPerkStyleData | null
  firstItem: StaticItemData | null
  nameTag?: string | null
  /** Tighter sizing + no "play rate" label, for the narrow homepage panel. */
  compact?: boolean
  /**
   * Hold every sub-slot's width even when the datum behind it is missing, so
   * the cluster measures the same on every row.
   *
   * A player's keystone / first item come from an aggregated build that a
   * freshly tracked account may not have yet, and the play rate is 2–4
   * characters wide. Left to collapse, those three make the cluster's width a
   * function of how much data the row happens to have — and in a list, a
   * variable-width cluster drags every column beside it out of alignment.
   * Callers that lay the cluster out in a fixed column (the leaderboard row)
   * set this; the homepage teaser, where the cluster is pinned to a flex edge,
   * does not need it.
   */
  reserveSlots?: boolean
  /**
   * Native lazy-loading hint for the champion icon. The two callers sit in
   * different places: leaderboard rows are always below the build panel and
   * pass `'lazy'`, while the homepage teaser renders near the top of the
   * landing page — so this defaults to unset rather than being hardcoded here.
   */
  loading?: 'lazy' | 'eager'
}>(), {
  nameTag: null,
  compact: false,
  reserveSlots: false,
  loading: undefined,
})

const NuxtLinkComponent = resolveComponent('NuxtLink')

const { truemainPathFor } = useChampionSlugs()

const championHref = computed(() =>
  props.nameTag
    ? truemainPathFor(props.nameTag, props.champion.championId)
    : undefined)

const playRatePct = computed(() => {
  const rate = props.champion.playRate
  return Number.isFinite(rate) ? formatPercentage(rate, 0) : null
})

const iconSize = computed(() => (props.compact ? 28 : 30))
const buildSize = computed(() => (props.compact ? 20 : 22))
const secondaryOverlaySize = computed(() => (props.compact ? 11 : 13))
const championTitle = computed(() => `${props.name} · ${props.champion.games} games`)

// Play-rate box under `reserveSlots`: wide enough for the longest value the
// figure can take ("100%") at each size, so a 23% row and a 100% row put the
// keystone at the same x. Literal class strings — Tailwind only emits what it
// can see (see DESIGN_SYSTEM.md).
const playRateSlotClass = computed(() =>
  props.reserveSlots ? (props.compact ? 'w-8 shrink-0' : 'w-10 shrink-0') : undefined)

// Build icons only render when the aggregate exists; with `reserveSlots` the
// box is emitted regardless and simply stays empty.
const showKeystoneSlot = computed(() => props.keystone !== null || props.reserveSlots)
const showItemSlot = computed(() => props.firstItem !== null || props.reserveSlots)
const showPlayRateSlot = computed(() => playRatePct.value !== null || props.reserveSlots)
</script>

<template>
  <component
    :is="nameTag ? NuxtLinkComponent : 'div'"
    :to="championHref"
    :aria-label="nameTag ? `${name} — view ${name} build` : undefined"
    class="flex items-center gap-2 rounded-lg"
    :class="nameTag ? 'surface-hover -mx-1 px-1 py-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary' : ''"
  >
    <SkeletonImage
      :src="iconUrl"
      :alt="name"
      :title="championTitle"
      :width="iconSize"
      :height="iconSize"
      :loading="loading"
      class="shrink-0 rounded-md ring-1 ring-default/40"
    />

    <!-- Play rate with a "PR" label, mirroring the "WR" label under the row's
         win rate so the two percentages read as a matched pair. -->
    <div
      v-if="showPlayRateSlot"
      class="flex flex-col leading-none"
      :class="playRateSlotClass"
    >
      <template v-if="playRatePct !== null">
        <span
          class="font-semibold tabular-nums"
          :class="compact ? 'text-xs' : 'text-sm'"
        >{{ playRatePct }}</span>
        <span class="mt-0.5 text-[10px] font-normal uppercase tracking-wide text-muted">PR</span>
      </template>
    </div>

    <!-- Keystone with the secondary tree as a small overlay badge — same
         presentation as the champion list's top-build column. -->
    <div
      v-if="showKeystoneSlot"
      class="relative shrink-0"
      :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
    >
      <template v-if="keystone">
        <GameTooltipPerkIcon
          :perk="keystone"
          :width="buildSize"
          :height="buildSize"
          :loading="loading"
          class="rounded-full"
          :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
        />
        <GameTooltipPerkStyleIcon
          v-if="secondaryStyle"
          :style="secondaryStyle"
          :width="secondaryOverlaySize"
          :height="secondaryOverlaySize"
          :loading="loading"
          class="absolute -bottom-1 -right-1.5"
          :class="compact ? 'size-[11px]' : 'size-[13px]'"
        />
      </template>
    </div>

    <div
      v-if="showItemSlot"
      class="shrink-0"
      :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
    >
      <GameTooltipItemIcon
        v-if="firstItem"
        :item="firstItem"
        :width="buildSize"
        :height="buildSize"
        :loading="loading"
        class="rounded"
        :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
      />
    </div>
  </component>
</template>
