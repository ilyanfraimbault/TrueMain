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
}>(), {
  nameTag: null,
  compact: false,
})

const NuxtLinkComponent = resolveComponent('NuxtLink')

const championHref = computed(() =>
  props.nameTag
    ? `/truemains/${encodeURIComponent(props.nameTag)}/champions/${props.champion.championId}`
    : undefined)

const playRatePct = computed(() => {
  const rate = props.champion.playRate
  return Number.isFinite(rate) ? formatPercentage(rate, 0) : null
})

const iconSize = computed(() => (props.compact ? 32 : 36))
const buildSize = computed(() => (props.compact ? 24 : 28))
const championTitle = computed(() => `${props.name} · ${props.champion.games} games`)
</script>

<template>
  <component
    :is="nameTag ? NuxtLinkComponent : 'div'"
    :to="championHref"
    :aria-label="nameTag ? `${name} — view ${name} build` : undefined"
    class="flex items-center gap-2 rounded-lg"
    :class="nameTag ? 'glass-hover -mx-1 px-1 py-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary' : ''"
  >
    <SkeletonImage
      :src="iconUrl"
      :alt="name"
      :title="championTitle"
      :width="iconSize"
      :height="iconSize"
      class="shrink-0 rounded-md ring-1 ring-default/40"
    />

    <div
      v-if="playRatePct !== null"
      class="flex flex-col leading-none"
    >
      <span
        class="font-semibold tabular-nums"
        :class="compact ? 'text-xs' : 'text-sm'"
      >{{ playRatePct }}</span>
      <span
        v-if="!compact"
        class="mt-0.5 text-[11px] text-muted"
      >play rate</span>
    </div>

    <!-- Keystone with the secondary tree as a small overlay badge — same
         presentation as the champion list's top-build column. -->
    <div
      v-if="keystone"
      class="relative shrink-0"
      :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
    >
      <GameTooltipPerkIcon
        :perk="keystone"
        :width="buildSize"
        :height="buildSize"
        class="rounded-full"
        :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
      />
      <GameTooltipPerkStyleIcon
        v-if="secondaryStyle"
        :style="secondaryStyle"
        :width="compact ? 13 : 15"
        :height="compact ? 13 : 15"
        class="absolute -bottom-1 -right-1.5"
        :class="compact ? 'size-[13px]' : 'size-[15px]'"
      />
    </div>

    <GameTooltipItemIcon
      v-if="firstItem"
      :item="firstItem"
      :width="buildSize"
      :height="buildSize"
      class="shrink-0 rounded"
      :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
    />
  </component>
</template>
