<script setup lang="ts">
import type { MatchSummaryResponse } from '#shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { formatDuration } from '#common/utils/relativeTime'

// The row is the layout and the accordion; each column is its own component
// along the row's visual blocks: `MatchRowPortrait`, `MatchRowKda`,
// `MatchRowLoadout` (spells, runes and the shared `MatchItemGrid`),
// `MatchRowTeams` and `MatchRowPerformance`. Expanding opens the shared
// `MatchDetailPanel` (scoreboard + player panel).
const props = defineProps<{
  match: MatchSummaryResponse
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
  /**
   * When set, the row becomes an accordion whose header expands an inline
   * match-detail panel. Omitted on surfaces with no resolved player identity
   * to fetch the detail against, where the row stays a static, non-interactive
   * article.
   */
  nameTag?: string | null
}>()

// A nameTag is required to fetch the detail payload, so it gates the whole
// expand affordance — without it the row degrades to a plain article.
const canExpand = computed(() => !!props.nameTag)

const expanded = ref(false)
// The detail panel is mounted on first open and kept mounted afterwards, so
// collapsing and re-opening a row never re-runs the (large) detail fetch.
const hasOpened = ref(false)

function toggle() {
  if (!canExpand.value) return
  expanded.value = !expanded.value
  if (expanded.value) hasOpened.value = true
}

const self = computed(() => props.match.self)

const championById = computed(
  () => new Map(props.champions.map(c => [c.championId, c])),
)

const championIconUrl = computed(
  () => championById.value.get(self.value.championId)?.iconUrl ?? null,
)

const championName = computed(
  () => championById.value.get(self.value.championId)?.name
    ?? `Champion ${self.value.championId}`,
)

const durationLabel = computed(() => formatDuration(props.match.gameDurationSeconds))

const csPerMin = computed(() => {
  const minutes = props.match.gameDurationSeconds / 60
  if (minutes <= 0) return '0.0'
  return (self.value.cs / minutes).toFixed(1)
})

// Only consumed by the article's aria-label: the visible row states the
// result through its tint alone (see the header comment in the template).
const resultLabel = computed(() => (self.value.win ? 'Victory' : 'Defeat'))

// Row-level tint: subtle sky for wins (the LoL-tracker convention
// across OP.GG / Mobalytics / DPM.LOL), red for losses. We deliberately
// don't use the brand emerald here — emerald is the primary UI accent
// (logo, buttons, active pagination) and overloading it as the win
// signal made every "this is a win" cue blur into every "this is a
// brand surface" cue. Sky reads as "result axis", emerald stays as
// "brand". Numbers tuned low (8% / 12% alpha) so the row body still
// reads as card.
const rowTint = computed(() =>
  self.value.win
    ? 'bg-sky-500/8 hover:bg-sky-500/12'
    : 'bg-red-500/8 hover:bg-red-500/12',
)
</script>

<template>
  <article
    class="group @container relative overflow-hidden rounded-md bg-elevated"
    :aria-label="`${resultLabel} as ${championName}, ${self.kills}/${self.deaths}/${self.assists}`"
  >
    <!-- The row is its own @container: every column below sizes off the width
         the row is actually given, not the viewport. It has to — the same
         component renders full-width on the profile page and inside a ~33rem
         drawer (builder/GamesDrawer), and viewport breakpoints had the drawer
         copy laying itself out as if it had the whole page, spilling into the
         `overflow-hidden` clip above. Three tiers: base is the compact
         drawer/mobile layout, @xl adds the team compositions, @2xl restores
         the full-size icons and columns, @3xl adds the secondary stats
         (CS/m, KP, PERF) — those come last on purpose: the compositions are
         what a provenance row is read for, so when only one of the two fits,
         the compositions win. -->

    <!-- Row header: tinted clickable summary. The win/loss signal is carried
         by the row tint alone — no edge strip, it read as heavy against the
         row surface, and no result label, which only repeated the tint. -->
    <div class="flex transition-colors" :class="rowTint">
      <!-- Expand affordance: a role=button div (not a native <button>) so the
           hover-only GameTooltip triggers inside — themselves UTooltip buttons
           — aren't nested inside an interactive button, which is invalid HTML
           and swallows clicks. Keyboard support is wired manually. When no
           nameTag is provided the row degrades to a static, non-interactive
           block. -->
      <div
        :role="canExpand ? 'button' : undefined"
        :tabindex="canExpand ? 0 : undefined"
        :aria-expanded="canExpand ? expanded : undefined"
        class="flex flex-1 flex-wrap items-center gap-2 px-2 py-2 @md:flex-nowrap @md:justify-between @2xl:gap-3 @2xl:px-3 @2xl:py-2.5"
        :class="canExpand ? 'cursor-pointer' : ''"
        @click="toggle"
        @keydown.enter.prevent="toggle"
        @keydown.space.prevent="toggle"
      >
        <!-- The meta column (result label + duration) is gone: the row tint
             already says win or loss, so spelling it out again cost 3.5rem of
             every row to repeat what the colour states. Screen readers still
             get the result from the article's aria-label above, which is the
             only consumer the colour alone doesn't serve. Duration and LP
             delta moved into the stats cluster below. -->

        <!-- From @md up every column is fixed-width and the row is
             `justify-between`: free width is shared evenly between columns
             instead of pooling as two dead gaps around the build (#1610).
             Portrait + KDA stay wrapped so the spread never splits them. -->
        <div class="flex shrink-0 items-center gap-2 @2xl:gap-3">
          <MatchRowPortrait
            :icon-url="championIconUrl"
            :champion-name="championName"
            :position="self.position ?? null"
            :champion-level="self.championLevel"
          />
          <MatchRowKda :self="self" :duration-label="durationLabel" />
        </div>

        <!-- Secondary stats only once the row clears @3xl — the last thing
             to come back, because the 4rem they cost is exactly what the
             team compositions (added at @xl) need. Below that the KDA
             cluster, the build and the compositions are the scan targets.
             Centred (not left-aligned) in its own fixed-width column, like the
             KDA cluster. Kill participation is left to the scoreboard: it
             says little on its own, and the performance score moved to the
             right edge. -->
        <div class="hidden w-14 shrink-0 flex-col items-center gap-0.5 text-[11px] text-muted tabular-nums @3xl:flex">
          <span>{{ csPerMin }} CS/m</span>
          <span>{{ durationLabel }}</span>
        </div>

        <MatchRowLoadout
          :self="self"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
        />

        <!-- Right-edge group: team compositions + MVP/ACE accolade + expand
             chevron, pinned together as one unit. From @md up no margin is
             needed — the row's `justify-between` already puts the last column
             flush against the right edge. Below @md the loadout has wrapped
             away onto its own line and the row keeps its default justification,
             so the group needs `ml-auto` to stay on the edge. -->
        <div class="ml-auto flex shrink-0 items-center gap-2 @md:ml-0 @2xl:gap-3">
          <MatchRowTeams
            :participants="match.participants"
            :self-team-id="self.teamId"
            :champion-by-id="championById"
          />

          <!-- Performance slot + chevron. -->
          <div class="flex shrink-0 items-center gap-1 @2xl:gap-2">
            <MatchRowPerformance :self="self" />
            <UIcon
              v-if="canExpand"
              name="i-lucide-chevron-down"
              class="size-4 text-muted transition-transform duration-200"
              :class="expanded ? 'rotate-180' : ''"
              aria-hidden="true"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- Inline detail panel. Animated open/close via a grid-rows 0fr→1fr
         transition; the inner wrapper clips overflow while collapsed. Mounted
         lazily on first open (hasOpened) so the detail fetch only fires for
         rows the user actually expands, then kept mounted so re-toggling
         doesn't re-fetch. -->
    <div
      v-if="canExpand"
      class="grid transition-[grid-template-rows] duration-300 ease-out"
      :class="expanded ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'"
    >
      <!-- inert while collapsed: the panel stays mounted (0fr height, clipped)
           so re-opening doesn't re-fetch, but its tabs/links must leave the
           keyboard tab order when hidden. -->
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
