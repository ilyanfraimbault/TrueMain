<!--
  The one match row of every match list — the profile's history (site and app),
  the favorites, the player-scoped champion page, the builder's games drawer and
  the app's dashboard. A 54 px line: the result as an edge mark and a wash, then
  one component per column (`MatchRowPortrait`, `MatchRowResult`, `MatchRowKda`,
  `MatchRowStats`, `MatchRowLoadout`, `MatchRowTeams`, `MatchRowPerformance`).
  With a `nameTag` the row is an accordion over the shared `MatchDetailPanel`,
  which reads the game through the page's `MATCH_DETAIL_SOURCE`; without one it
  stays a static article. Host-specific controls (the app's "Watch") go in the
  `actions` slot.
-->
<script setup lang="ts">
import { championDisplayName } from '#shared/utils/champion-name'
import type { MatchSummaryResponse } from '#shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { resultTone } from '#common/utils/result-tone'

const props = defineProps<{
  match: MatchSummaryResponse
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
  /** The player's Riot ID as the site's slug; the detail is fetched against it, so it gates the accordion. */
  nameTag?: string | null
  /**
   * `champions` keyed by id, built once by a host drawing many rows
   * (`MatchHistoryList`, #2129). Built here from `champions` when absent.
   */
  championById?: Map<number, ChampionStaticListItem>
}>()

const canExpand = computed(() => !!props.nameTag)
const expanded = ref(false)
// Mounted on first open and kept afterwards, so re-opening never re-fetches.
const hasOpened = ref(false)
function toggle() {
  if (!canExpand.value) return
  expanded.value = !expanded.value
  if (expanded.value) hasOpened.value = true
}

const self = computed(() => props.match.self)
const championById = computed(() => props.championById ?? new Map(props.champions.map(c => [c.championId, c])))
const champion = computed(() => championById.value.get(self.value.championId))
const championName = computed(() => championDisplayName(self.value.championId, champion.value))

const result = computed(() => resultTone(self.value.win))
</script>

<template>
  <!-- `content-visibility: auto` skips style, layout and paint of a row off
       screen (#2129): a page of 20 rows lays out only the ~9 in view. It stays
       in the accessibility tree, the focus order and in-page find. The 54 px
       stands in for a row never rendered yet; `auto` makes the browser keep the
       last height it rendered instead, so an opened accordion scrolled away
       keeps its size. Block size only: the width is the list's. -->
  <article
    class="@container surface overflow-hidden rounded-lg [content-visibility:auto] [contain-intrinsic-block-size:auto_54px]"
    :aria-label="`${result.label} as ${championName}, ${self.kills}/${self.deaths}/${self.assists}`"
  >
    <!-- The row sizes off its own width, not the viewport (#967): it sits
         full-width on a page, in a ~33rem drawer and on a phone. Columns come
         back as it widens — compositions first (they are what a provenance row
         is read for), then CS/m and duration, then the result and queue label,
         which only repeat the edge mark at a glance. The skeleton mirrors these
         tiers (`MatchRowSkeleton`). `isolate` keeps the wash and the edge under
         the content without positioning every column. -->
    <div
      :role="canExpand ? 'button' : undefined"
      :tabindex="canExpand ? 0 : undefined"
      :aria-expanded="canExpand ? expanded : undefined"
      class="relative isolate flex h-[54px] items-center gap-1.5 pl-3 pr-2 transition-colors @sm:gap-2.5 @sm:pl-4 @sm:pr-3"
      :class="canExpand ? 'cursor-pointer hover:bg-white/[0.025]' : ''"
      @click="toggle"
      @keydown.enter.prevent="toggle"
      @keydown.space.prevent="toggle"
    >
      <span class="pointer-events-none absolute inset-y-0 left-0 -z-10 w-40 bg-linear-to-r to-transparent" :class="result.wash" />
      <span class="absolute inset-y-2 left-0 -z-10 w-[3px] rounded-r-full" :class="result.edge" />

      <MatchRowPortrait
        :icon-url="champion?.iconUrl ?? null"
        :champion-name="championName"
        :position="self.position ?? null"
        :champion-level="self.championLevel"
      />
      <div class="hidden shrink-0 @[39rem]:block">
        <MatchRowResult :win="self.win" :queue-id="match.queueId" :game-mode="match.gameMode" />
      </div>
      <MatchRowKda :self="self" />
      <div class="hidden shrink-0 @[33rem]:block">
        <MatchRowStats :self="self" :duration-seconds="match.gameDurationSeconds" />
      </div>
      <MatchRowLoadout
        :self="self"
        :items="items"
        :summoner-spells="summonerSpells"
        :rune-tree="runeTree"
      />

      <!-- Right edge, one flex item pushed out by `ml-auto`. The chevron is
           the first thing a phone drops: the whole line is the toggle. -->
      <div class="ml-auto flex shrink-0 items-center gap-1.5 @sm:gap-2.5">
        <div v-if="match.participants.length" class="hidden @[29rem]:block">
          <MatchRowTeams
            :participants="match.participants"
            :self-team-id="self.teamId"
            :champion-by-id="championById"
          />
        </div>
        <!-- A game nobody graded (a remake, or a client game whose scoreboard
             could not be read) carries no score. -->
        <MatchRowPerformance v-if="self.performanceScore" :self="self" />
        <slot name="actions" />
        <UIcon
          v-if="canExpand"
          name="i-lucide-chevron-down"
          class="hidden size-4 text-dimmed transition-transform duration-200 @[22rem]:block"
          :class="expanded ? 'rotate-180' : ''"
          aria-hidden="true"
        />
      </div>
    </div>

    <!-- grid-rows 0fr→1fr animates the height; `inert` takes the collapsed
         panel's tabs and links out of the keyboard order. -->
    <div
      v-if="canExpand"
      class="grid transition-[grid-template-rows] duration-300 ease-out"
      :class="expanded ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'"
    >
      <div class="min-h-0 overflow-hidden" :inert="!expanded">
        <MatchDetailPanel
          v-if="hasOpened"
          :name-tag="nameTag ?? ''"
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
