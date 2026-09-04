<script setup lang="ts">
import type { ChampionBuild } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import type { ItemContextCard } from '~~/shared/utils/item-context'
import { indexItemContext } from '~~/shared/utils/item-context'

const props = defineProps<{
  builds: ChampionBuild[]
  championStatic: ChampionStaticData
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  /** True while `summonersMap` is still loading — see `ChampionCoreSpells`. */
  summonersPending?: boolean
  runeTree: RuneTreeResponse | null
  // Scope forwarded to the per-build power spikes panel, which fetches its own
  // slice keyed on (champion, position, patch, elo, opponent) + the build it
  // renders. Optional: the builder preview and the player-scoped champion page
  // reuse these tabs without a population slice to attach spikes to.
  championId?: number
  position?: string | null
  patch?: string | null
  eloBracket?: string | null
  // Lane opponent from the champion page's matchup filter (#957). The builds
  // themselves are already re-sliced server-side when it is set (#923), so the
  // tabs shown here belong to the matchup; this carries the same scope down so
  // the spikes describe those games rather than the champion at large.
  opponentChampionId?: number | null
  /**
   * Render the tabs as scaffolding rather than as data: every icon falls back
   * to its loading box and every number is masked. `ChampionBuildTabsSkeleton`
   * is exactly this component in that mode over a placeholder aggregate — see
   * `utils/build-placeholder` for why the skeleton is the real layout and not
   * a hand-drawn copy of it.
   */
  pending?: boolean
}>()

// The situational build context (#1451), fetched once for the whole tab set rather than
// per panel: the verdicts are keyed on (champion, position, patch) and every tab renders
// the same slice of them, so a per-tab fetch would repeat one request four times for one
// answer. Not scoped by rank or by the matchup filter — the verdicts carry neither
// dimension, and `ItemBody` says so on the card instead of implying otherwise.
const { data: itemContext } = useChampionItemContext(
  () => props.championId ?? 0,
  () => props.position,
  () => props.patch,
)

const itemContextIndex = computed<Map<string, ItemContextCard>>(() =>
  // Withheld while scaffolding for the same reason every number is: the placeholder
  // aggregate's item ids are invented, and they would collide with real verdicts.
  props.pending
    ? new Map()
    // `allMatchups` when the page pins an opponent: every panel around the card is
    // re-sliced to that matchup and the verdicts are not, so the card has to say which
    // games its percentages came from rather than let the filter above speak for it.
    : indexItemContext(itemContext.value?.items, { allMatchups: Boolean(props.opponentChampionId) }),
)

const items = computed(() =>
  props.builds.map((build, index) => ({
    value: `build-${index}`,
    slot: `build-${index}` as const,
    build,
  })),
)
</script>

<template>
  <!-- Single card wrapping the whole tab-dependent section. The tab bar is
       docked to the top edge (body padding removed at every breakpoint — a
       bare `p-0` loses to the theme's `sm:p-6` in tw-merge — list background
       and rounding stripped, full-width from the horizontal orientation) with
       a divider separating it from the panel content, which carries the
       card's own padding via the `content` slot. The active pill is a dark
       overlay instead of the theme's white `bg-inverted`. -->
  <UCard
    v-if="items.length"
    :ui="{ body: 'p-0 sm:p-0' }"
  >
    <UTabs
      :items="items"
      :default-value="items[0]?.value"
      variant="pill"
      color="neutral"
      size="md"
      class="w-full"
      :unmount-on-hide="false"
      :ui="{
        list: 'rounded-none border-b border-default bg-transparent p-1.5',
        indicator: 'rounded-lg bg-black/30 shadow-none inset-y-1.5',
        trigger: 'flex-1 gap-1.5 py-2.5 data-[state=active]:text-highlighted',
        content: 'p-3 sm:p-4',
      }"
    >
      <template #leading="{ item }">
        <div class="flex items-center gap-1.5">
          <!-- Rendered unconditionally, on the ids alone: the item and rune
               maps are separate (patch-pinned, deferred) static fetches that
               land after the builds, so gating the slots on a resolved lookup
               made the whole tab bar reflow the moment they arrived.
               `SkeletonImage` already draws the loading box for a null icon. -->
          <GameTooltipItemIcon
            :item="itemsMap[item.build.firstItemId] ?? null"
            :width="24"
            :height="24"
            class="size-6 rounded"
          />
          <div class="relative size-6">
            <GameTooltipPerkIcon
              :perk="runeTree?.perks[item.build.primaryKeystoneId] ?? null"
              :width="24"
              :height="24"
              class="size-6 rounded-full"
            />
            <GameTooltipPerkStyleIcon
              v-if="item.build.core.runePage"
              :style="runeTree?.perkStyles[item.build.core.runePage.secondaryStyleId] ?? null"
              :width="14"
              :height="14"
              class="absolute -bottom-0.5 -right-1 size-3.5"
            />
          </div>
        </div>
      </template>
      <template #default="{ item, index }">
        <!-- The leading item/rune icons already carry their own accessible
             names (see GameTooltip*Icon `alt`), but without this prefix the
             tab's full accessible name reads as just those names + a bare
             percentage — ambiguous with several builds open. sr-only text
             disambiguates without changing the visible design. -->
        <span class="sr-only">Build {{ index + 1 }}, </span>
        <USkeleton
          v-if="pending"
          class="h-4 w-7"
        />
        <span
          v-else
          class="text-xs tabular-nums text-muted"
        >
          {{ formatPercentage(item.build.pickRate, 0) }}
        </span>
      </template>
      <template
        v-for="item in items"
        :key="item.value"
        #[item.slot]
      >
        <!-- In `pending` mode only the first panel is built. The tabs keep
             every panel mounted (`unmount-on-hide="false"`), so scaffolding
             all three would triple the placeholder DOM — and the SSR HTML —
             for two panels nobody can reach behind the skeleton's `inert`. -->
        <ChampionBuildPanel
          v-if="!pending || item.value === items[0]?.value"
          :build="item.build"
          :champion-static="championStatic"
          :items-map="itemsMap"
          :summoners-map="summonersMap"
          :summoners-pending="summonersPending"
          :rune-tree="runeTree"
          :champion-id="championId"
          :position="position"
          :patch="patch"
          :elo-bracket="eloBracket"
          :opponent-champion-id="opponentChampionId"
          :item-context="itemContextIndex"
          :pending="pending"
        />
      </template>
    </UTabs>
  </UCard>
  <p
    v-else
    class="text-sm text-muted"
  >
    No build data
  </p>
</template>
