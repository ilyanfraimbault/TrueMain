<!--
  The match row of a player's history — the desktop dashboard's, now shared by
  the site's profile and the app: one line of 54 px, the result as
  an edge mark and a wash rather than a full tint, the queue beside it, the build
  as on the site (spells over each other, keystone over the secondary tree, the
  item grid), both compositions, and the performance score where TrueMain scored
  the game (the client's own games carry none). The accordion opens the shared
  `MatchDetailPanel`, which reads the game through the page's
  `MATCH_DETAIL_SOURCE`. Host-specific controls (the app's "Watch") go in the
  `actions` slot.
-->
<script setup lang="ts">
import type { MatchSummaryResponse } from '#shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { formatDuration } from '#common/utils/relativeTime'

const props = defineProps<{
  match: MatchSummaryResponse
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
  queueLabel: string
  /** The player's Riot ID as the site's slug, for the detail panel. */
  nameTag: string
}>()

const expanded = ref(false)
const hasOpened = ref(false)
function toggle() {
  expanded.value = !expanded.value
  if (expanded.value) hasOpened.value = true
}

const self = computed(() => props.match.self)
const championById = computed(() => new Map(props.champions.map(c => [c.championId, c])))
const championName = (id: number) => championById.value.get(id)?.name ?? `Champion ${id}`

// Both sides in lane order, so the two strips pair laner against laner; an
// unknown lane goes last.
const POSITION_ORDER = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY']
const laneIndex = (p: { position: string | null }) => {
  const index = POSITION_ORDER.indexOf(p.position ?? '')
  return index === -1 ? POSITION_ORDER.length : index
}
const byLane = (a: { position: string | null }, b: { position: string | null }) => laneIndex(a) - laneIndex(b)
const teams = computed(() => [
  props.match.participants.filter(p => p.teamId === self.value.teamId).sort(byLane),
  props.match.participants.filter(p => p.teamId !== self.value.teamId).sort(byLane),
])

const result = computed(() => self.value.win
  ? { label: 'Victory', text: 'text-sky-300', edge: 'bg-sky-400', wash: 'from-sky-500/12' }
  : { label: 'Defeat', text: 'text-red-400', edge: 'bg-red-400', wash: 'from-red-500/12' })

const kdaRatio = computed(() => {
  const { kills, deaths, assists } = self.value
  return deaths === 0 ? 'Perfect' : `${((kills + assists) / deaths).toFixed(2)} KDA`
})
// The site row's grading: gold for a standout, the data axis for a solid game.
const kdaColor = computed(() => {
  const { kills, deaths, assists } = self.value
  const ratio = deaths === 0 ? Infinity : (kills + assists) / deaths
  if (ratio >= 5) return 'text-gold'
  if (ratio >= 3) return 'text-data-good'
  return 'text-muted'
})

const csPerMin = computed(() => (self.value.cs / Math.max(props.match.gameDurationSeconds / 60, 1)).toFixed(1))
const lp = computed(() => self.value.lpDelta)

</script>

<template>
  <article
    class="@container surface overflow-hidden rounded-lg"
    :aria-label="`${result.label} as ${championName(self.championId)}, ${self.kills}/${self.deaths}/${self.assists}`"
  >
    <div
      role="button"
      tabindex="0"
      :aria-expanded="expanded"
      class="relative flex h-[54px] cursor-pointer items-center gap-2.5 pl-4 pr-3 transition-colors hover:bg-white/[0.025]"
      @click="toggle"
      @keydown.enter.prevent="toggle"
      @keydown.space.prevent="toggle"
    >
      <span class="pointer-events-none absolute inset-y-0 left-0 w-40 bg-linear-to-r to-transparent" :class="result.wash" />
      <span class="absolute inset-y-2 left-0 w-[3px] rounded-r-full" :class="result.edge" />

      <div class="relative shrink-0">
        <SkeletonImage
          :src="championById.get(self.championId)?.iconUrl ?? null"
          :alt="championName(self.championId)"
          :title="championName(self.championId)"
          :width="36"
          :height="36"
          class="size-9 rounded-md"
        />
        <span
          class="absolute -bottom-1 -right-1 flex size-4 items-center justify-center rounded-full bg-default text-[9px] font-bold ring-1 ring-default"
          :title="self.position ? POSITION_BY_VALUE.get(self.position)?.label : undefined"
        >
          <img v-if="self.position" :src="getPositionIconUrl(self.position)" alt="" class="size-3">
          <template v-else>{{ self.championLevel }}</template>
        </span>
      </div>

      <div class="relative w-20 shrink-0 leading-tight">
        <p class="text-[13px] font-semibold" :class="result.text">{{ result.label }}</p>
        <p class="truncate text-[11px] text-muted" :title="queueLabel">{{ queueLabel }}</p>
      </div>

      <div class="w-[4.5rem] shrink-0 leading-tight">
        <p class="flex items-baseline gap-0.5 text-sm font-semibold tabular-nums text-highlighted">
          {{ self.kills }}<span class="text-dimmed">/</span><span class="text-red-400">{{ self.deaths }}</span><span class="text-dimmed">/</span>{{ self.assists }}
        </p>
        <p class="text-[11px] font-medium tabular-nums" :class="kdaColor">{{ kdaRatio }}</p>
      </div>

      <div class="w-14 shrink-0 text-right leading-tight tabular-nums">
        <p v-if="lp !== null" class="text-xs font-semibold" :class="lp >= 0 ? 'text-data-good' : 'text-data-bad'">
          {{ lp > 0 ? '+' : '' }}{{ lp }} LP
        </p>
        <p v-else class="text-xs text-muted">{{ formatDuration(match.gameDurationSeconds) }}</p>
        <p class="text-[11px] text-dimmed">{{ csPerMin }} CS/m</p>
      </div>

      <div class="flex shrink-0 items-center gap-1">
        <div class="flex flex-col gap-0.5">
          <GameTooltipSummonerSpellIcon :spell="summonerSpells[self.summoner1Id] ?? null" :width="18" :height="18" class="size-[18px] rounded" />
          <GameTooltipSummonerSpellIcon :spell="summonerSpells[self.summoner2Id] ?? null" :width="18" :height="18" class="size-[18px] rounded" />
        </div>
        <div class="flex flex-col items-center gap-0.5">
          <GameTooltipPerkIcon :perk="runeTree.perks[self.keystoneId] ?? null" :width="18" :height="18" class="size-[18px] rounded-full bg-black/40" />
          <GameTooltipPerkStyleIcon :style="runeTree.perkStyles[self.subStyleId] ?? null" :width="14" :height="14" class="size-3.5" />
        </div>
        <MatchItemGrid
          class="ml-0.5"
          :item-ids="self.items"
          :trinket-item-id="self.trinketItemId"
          :role-bound-item-id="self.roleBoundItemId"
          :items="items"
          :size="20"
        />
      </div>

      <div v-if="match.participants.length" class="ml-auto hidden shrink-0 flex-col gap-0.5 @[38rem]:flex">
        <div v-for="(team, side) in teams" :key="side" class="flex gap-0.5">
          <SkeletonImage
            v-for="(p, index) in team"
            :key="index"
            :src="championById.get(p.championId)?.iconUrl ?? null"
            :alt="championName(p.championId)"
            :title="p.gameName ? `${championName(p.championId)} — ${p.gameName}` : championName(p.championId)"
            :width="16"
            :height="16"
            class="size-4 rounded-sm"
          />
        </div>
      </div>

      <MatchRowPerformance
        v-if="self.performanceScore"
        :self="self"
        class="shrink-0"
        :class="match.participants.length ? 'ml-auto @[38rem]:ml-0' : 'ml-auto'"
      />

      <slot name="actions" />

      <UIcon
        name="i-lucide-chevron-down"
        class="size-4 shrink-0 text-dimmed transition-transform duration-200"
        :class="[expanded ? 'rotate-180' : '', match.participants.length || self.performanceScore || $slots.actions ? '' : 'ml-auto']"
      />
    </div>

    <div class="grid transition-[grid-template-rows] duration-300 ease-out" :class="expanded ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'">
      <div class="min-h-0 overflow-hidden" :inert="!expanded">
        <MatchDetailPanel
          v-if="hasOpened"
          :name-tag="nameTag"
          :match-id="match.matchId"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
          :self-champion-id="self.championId"
        />
      </div>
    </div>
  </article>
</template>
