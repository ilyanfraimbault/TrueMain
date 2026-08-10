<script setup lang="ts">
import type { MatchSummaryParticipant, MatchSummaryResponse } from '~~/shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { formatPercentage, getPositionIconUrl } from '~~/shared/utils/ddragon'
import { isBootsItem, isNonBuildItem } from '~~/shared/utils/build'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { formatDuration } from '~/utils/relativeTime'

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

// The lane glyph overlaying the champion portrait renders as a plain <img>
// (one component instance per icon is not worth it at this count), so it needs
// the canonical URL built explicitly — without it the raw `/positions/*.png`
// was served full-size for a 12 px box, and as a fourth distinct cache entry
// for a glyph the rest of the page already had.
const canonicalIcon = useCanonicalIcon()

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

// ─── Team compositions ─────────────────────────────────────────────────────
// Both sides sorted into canonical role order (TOP → SUPPORT) so the two
// columns line up laner-vs-laner, with a shared position-icon gutter between
// them. Positions can be missing on old rows ingested before the field was
// exposed — those keep the server's participant order and the gutter hides,
// so the columns never pretend to a pairing the data can't back.
const POSITION_ORDER = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const

function sortByPosition(team: MatchSummaryParticipant[]): MatchSummaryParticipant[] {
  return [...team].sort((a, b) => {
    const ia = POSITION_ORDER.indexOf(a.position as typeof POSITION_ORDER[number])
    const ib = POSITION_ORDER.indexOf(b.position as typeof POSITION_ORDER[number])
    return (ia === -1 ? POSITION_ORDER.length : ia) - (ib === -1 ? POSITION_ORDER.length : ib)
  })
}

const allies = computed(() =>
  sortByPosition(props.match.participants.filter(p => p.teamId === self.value.teamId)))
const enemies = computed(() =>
  sortByPosition(props.match.participants.filter(p => p.teamId !== self.value.teamId)))

function participantTooltip(p: MatchSummaryParticipant): string {
  const champ = championById.value.get(p.championId)?.name ?? `Champion ${p.championId}`
  const role = p.position ? POSITION_BY_VALUE.get(p.position)?.label : null
  const who = p.gameName ? ` — ${p.gameName}${p.tagLine ? `#${p.tagLine}` : ''}` : ''
  return role ? `${champ} · ${role}${who}` : `${champ}${who}`
}

const summoner1: ComputedRef<StaticSummonerSpellData | null> = computed(
  () => props.summonerSpells[self.value.summoner1Id] ?? null,
)
const summoner2: ComputedRef<StaticSummonerSpellData | null> = computed(
  () => props.summonerSpells[self.value.summoner2Id] ?? null,
)

const keystone: ComputedRef<StaticPerkData | null> = computed(() => {
  if (!self.value.keystoneId) return null
  return props.runeTree.perks[self.value.keystoneId] ?? null
})

const subStyle: ComputedRef<StaticPerkStyleData | null> = computed(() => {
  if (!self.value.subStyleId) return null
  return props.runeTree.perkStyles[self.value.subStyleId] ?? null
})

// Inventory slots: three-state model so the row can tell apart a slot the
// player never bought (Item* = 0 in the DB — render an invisible same-sized
// placeholder) from one whose static data hasn't resolved yet (id > 0 but no
// entry in the items map — keep the skeleton, that's a real loading state).
// Without this distinction, every zero in the participant row renders as a
// "skeleton" square and looks like the page is stuck loading half the items.
//
// Real items are left-aligned and the remaining cells become invisible
// placeholders so the six-slot grid stays the same width on every row — the
// trinket column lines up vertically across the whole match history even
// when a row only has three items.
type InventorySlot =
  | { kind: 'empty' }
  | { kind: 'loading' }
  | { kind: 'item', item: StaticItemData }

const INVENTORY_SLOT_COUNT = 6

// The boots are pulled out of the six inventory slots into their own cell
// under the trinket (scoreboard convention), so the main grid holds only the
// non-boots items. The Eye of the Herald — a Rift Herald summon that sits in
// the trinket slot, never a real build item — is dropped everywhere.
const bootsItem = computed<StaticItemData | null>(() => {
  for (const id of self.value.items) {
    const item = props.items[id]
    if (item && isBootsItem(item)) return item
  }
  return null
})

const inventoryItems = computed<InventorySlot[]>(() => {
  const bootsId = bootsItem.value?.id
  const filled: InventorySlot[] = self.value.items
    .filter(id => id > 0 && id !== bootsId && !isNonBuildItem(id))
    .map((id) => {
      const item = props.items[id]
      return item ? { kind: 'item', item } : { kind: 'loading' }
    })
  const empties: InventorySlot[] = Array.from(
    { length: Math.max(0, INVENTORY_SLOT_COUNT - filled.length) },
    () => ({ kind: 'empty' }),
  )
  return [...filled, ...empties]
})
const trinket = computed<StaticItemData | null>(() => {
  const id = self.value.trinketItemId
  if (id <= 0 || isNonBuildItem(id)) return null
  return props.items[id] ?? null
})

// The viewing player's assigned role, taken straight from the PUUID-matched
// self read-model so the portrait can badge a position icon instead of the
// champion level. Null when Riot never assigned one (old rows, non-SR modes) —
// the level shows instead. Resolved server-side, so it stays correct even in
// queues that allow duplicate champions on a team.
const selfPosition = computed<string | null>(() => self.value.position ?? null)

const durationLabel = computed(() => formatDuration(props.match.gameDurationSeconds))

const kdaRatio = computed(() => {
  const { kills, deaths, assists } = self.value
  if (deaths === 0) return 'Perfect'
  return `${((kills + assists) / deaths).toFixed(2)} KDA`
})

// Value-graded accent on the KDA ratio (op.gg-style): amber for standout
// games (Perfect or 5+), sky for solid ones (3+), muted otherwise. Sky
// intentionally matches the win axis; amber is a warm standout accent, close
// to but not the same token as the `gold` MVP crown below.
const kdaColor = computed(() => {
  const { kills, deaths, assists } = self.value
  const ratio = deaths === 0 ? Infinity : (kills + assists) / deaths
  if (ratio >= 5) return 'text-amber-300'
  if (ratio >= 3) return 'text-sky-300'
  return 'text-muted'
})

const csPerMin = computed(() => {
  const minutes = props.match.gameDurationSeconds / 60
  if (minutes <= 0) return '0.0'
  return (self.value.cs / minutes).toFixed(1)
})

const kpPercent = computed(() => formatPercentage(self.value.killParticipation, 0))

// Performance score (#918): the number the MVP/ACE crown two columns right is
// derived from, printed so the badge stops being the only way to read it — and
// so a good game that just missed the crown still shows as one. Same value the
// expanded detail panel shows for this player.
const perfScore = computed(() => self.value.performanceScore)

const perfTooltip = computed(() =>
  `Performance score ${perfScore.value}/100 — ${ordinal(self.value.placement)} of 10 in this game`)

function ordinal(n: number): string {
  const rest = n % 100
  if (rest >= 11 && rest <= 13) return `${n}th`
  switch (n % 10) {
    case 1: return `${n}st`
    case 2: return `${n}nd`
    case 3: return `${n}rd`
    default: return `${n}th`
  }
}

// Graded on the same three-tone ramp as the KDA ratio above, and on the same
// thresholds the model's own bands imply: 50 is "average on every available
// component", so a standout has to clear it comfortably.
const perfColor = computed(() => {
  if (perfScore.value >= 75) return 'text-amber-300'
  if (perfScore.value >= 60) return 'text-sky-300'
  return 'text-muted'
})

const resultLabel = computed(() => (self.value.win ? 'Victory' : 'Defeat'))

const lpDeltaText = computed(() => {
  const delta = self.value.lpDelta
  if (delta === null || delta === undefined) return null
  return delta > 0 ? `+${delta} LP` : `${delta} LP`
})

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
         by the row tint plus the coloured result label alone — no edge strip,
         it read as heavy against the surface surface. -->
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
        class="flex flex-1 flex-wrap items-center gap-2 px-2 py-2 @md:flex-nowrap @2xl:gap-3 @2xl:px-3 @2xl:py-2.5"
        :class="canExpand ? 'cursor-pointer' : ''"
        @click="toggle"
        @keydown.enter.prevent="toggle"
        @keydown.space.prevent="toggle"
      >
        <!-- Meta column: result + duration only. Queue label and timestamp
             were dropped as low-signal noise — the surrounding page already
             frames the history as ranked solo/duo, and relative time crowds
             the row without helping scan performance. LP delta stays behind a
             guard for when the backend starts deriving it (always null today,
             so it renders nothing in prod). -->
        <div class="flex w-12 shrink-0 flex-col text-[11px] leading-tight @2xl:w-[3.5rem] @2xl:text-xs">
          <div class="font-semibold" :class="self.win ? 'text-sky-400' : 'text-red-400'">
            {{ resultLabel }}
          </div>
          <div class="text-muted tabular-nums">
            {{ durationLabel }}
          </div>
          <div
            v-if="lpDeltaText"
            class="font-semibold tabular-nums"
            :class="(self.lpDelta ?? 0) >= 0 ? 'text-sky-400' : 'text-red-400'"
          >
            {{ lpDeltaText }}
          </div>
        </div>

        <!-- Champion portrait, badged with the player's role (or the champion
             level when Riot assigned no position). Summoner spells and runes
             used to sit next to it; they now live just left of the item block
             (see below) so the whole loadout — spells, runes, items — reads as
             one continuous strip, matching the scoreboard layout. -->
        <div class="relative shrink-0 @2xl:ml-1">
          <SkeletonImage
            :src="championIconUrl"
            :alt="championName"
            :title="championName"
            loading="lazy"
            class="size-10 rounded @2xl:size-12"
          />
          <span
            class="absolute -bottom-1 -right-1 inline-flex items-center justify-center rounded-full bg-default ring-1 ring-default"
            :class="selfPosition
              ? 'size-4 @2xl:size-5'
              : 'size-3.5 text-[9px] font-bold leading-none @2xl:size-4 @2xl:text-[10px]'"
            :title="selfPosition ? (POSITION_BY_VALUE.get(selfPosition)?.label ?? selfPosition) : undefined"
          >
            <img
              v-if="selfPosition"
              :src="canonicalIcon(getPositionIconUrl(selfPosition))"
              loading="lazy"
              :alt="POSITION_BY_VALUE.get(selfPosition)?.label ?? selfPosition"
              class="size-3 @2xl:size-3.5"
            >
            <template v-else>{{ self.championLevel }}</template>
          </span>
        </div>

        <!-- KDA + stats: two-column block. KDA on top with the ratio under
             it; CS/m and KP stacked to the right so they share the vertical
             rhythm of the KDA cluster. Tabular-nums everywhere so digits
             never jitter, and the whole block is a fixed width (not just
             min-width) — a 12/2/15 game and a 4/8/8 game must occupy the
             exact same box, or everything to its right (the centred
             loadout, the right-edge group) drifts left/right row to row and
             the columns stop lining up down the list. -->
        <div class="flex w-28 shrink-0 items-start gap-2 @3xl:w-52 @3xl:gap-3">
          <div class="flex w-28 flex-col items-center">
            <div class="whitespace-nowrap text-base font-bold leading-tight tabular-nums @2xl:text-lg">
              {{ self.kills }}
              <span class="text-muted/70">/</span>
              <span class="text-red-400">{{ self.deaths }}</span>
              <span class="text-muted/70">/</span>
              {{ self.assists }}
            </div>
            <div class="text-[11px] font-semibold tabular-nums" :class="kdaColor">
              {{ kdaRatio }}
            </div>
          </div>
          <!-- Secondary stats only once the row clears @3xl — the last thing
               to come back, because the 4rem they cost is exactly what the
               team compositions (added at @xl) need. Below that the KDA
               cluster, the build and the compositions are the scan targets. -->
          <div class="hidden flex-col gap-0.5 text-[11px] text-muted tabular-nums @3xl:flex">
            <UTooltip :text="perfTooltip">
              <span class="font-semibold" :class="perfColor">
                {{ perfScore }} PERF
              </span>
            </UTooltip>
            <span>{{ csPerMin }} CS/m</span>
            <span>{{ kpPercent }} KP</span>
          </div>
        </div>

        <!-- Loadout strip: summoner spells + runes + item block, tightly
             grouped (small internal gaps) so spells → runes → items read
             left-to-right as one continuous build. Left-aligned inside a
             flex-1 track: the build hugs the KDA cluster and every bit of
             the row's slack collects on the right, where the team
             compositions live. Centring it instead (as it used to) split
             that slack into two half-empty gutters and left no single run
             of space wide enough for the ten composition icons. Summoners
             and runes each stack 2-high to match the two rows of the item
             grid. -->
        <!-- Below @md the loadout wraps onto its own line (`order-last` keeps
             it under the row rather than between the KDA and the accolade).
             A phone can't hold meta + portrait + KDA + a 9rem build strip on
             one line, and the alternative — dropping half the build — throws
             away the thing the row exists to show. -->
        <div class="order-last flex w-full grow items-center justify-center @md:order-none @md:w-auto @md:flex-1 @md:justify-start">
          <div class="flex shrink-0 items-center gap-1">
            <div class="flex flex-col gap-0.5">
              <GameTooltipSummonerSpellIcon
                :spell="summoner1"
                :width="22"
                :height="22"
                loading="lazy"
                class="size-[18px] rounded @2xl:size-[22px]"
              />
              <GameTooltipSummonerSpellIcon
                :spell="summoner2"
                :width="22"
                :height="22"
                loading="lazy"
                class="size-[18px] rounded @2xl:size-[22px]"
              />
            </div>
            <div class="flex flex-col items-center gap-0.5">
              <GameTooltipPerkIcon
                :perk="keystone"
                :width="22"
                :height="22"
                loading="lazy"
                class="size-[18px] rounded-full bg-black/40 @2xl:size-[22px]"
              />
              <GameTooltipPerkStyleIcon
                :style="subStyle"
                :width="18"
                :height="18"
                loading="lazy"
                class="size-[15px] @2xl:size-[18px]"
              />
            </div>

            <!-- Items: dark inset (scoreboard-style) with the six inventory
                 slots as a 3×2 grid, then a trailing column stacking the trinket
                 over the boots — boots are pulled out of the grid so they line up
                 with the trinket the way trackers show them. Empty slots stay as
                 transparent placeholders so the grid keeps its shape. The Eye of
                 the Herald is filtered out upstream (it's not a build item). -->
            <div class="flex items-center gap-1 rounded-lg bg-black/25 p-1 ring-1 ring-white/5 @2xl:gap-1.5 @2xl:p-1.5">
              <div class="grid grid-cols-3 gap-0.5 @2xl:gap-1">
                <template
                  v-for="(slot, idx) in inventoryItems"
                  :key="`item-${idx}`"
                >
                  <div
                    v-if="slot.kind === 'empty'"
                    class="size-5 shrink-0 rounded bg-white/5 @2xl:size-6"
                    aria-hidden="true"
                  />
                  <GameTooltipItemIcon
                    v-else
                    :item="slot.kind === 'item' ? slot.item : null"
                    :width="24"
                    :height="24"
                    class="size-5 rounded @2xl:size-6"
                  />
                </template>
              </div>
              <div class="flex flex-col gap-0.5 @2xl:gap-1">
                <GameTooltipItemIcon
                  :item="trinket"
                  :width="24"
                  :height="24"
                  loading="lazy"
                  class="size-5 rounded-full @2xl:size-6"
                />
                <div
                  v-if="bootsItem"
                  class="size-5 @2xl:size-6"
                >
                  <GameTooltipItemIcon
                    :item="bootsItem"
                    :width="24"
                    :height="24"
                    class="size-5 rounded @2xl:size-6"
                  />
                </div>
                <div
                  v-else
                  class="size-5 @2xl:size-6"
                  aria-hidden="true"
                />
              </div>
            </div>
          </div>
        </div>

        <!-- Right-edge group: team compositions + MVP/ACE accolade + expand
             chevron, pinned together as one unit. From @md up no margin is
             needed — the loadout's flex-1 track above already claims all of
             the row's free space, so this group lands flush against the right
             edge right after it. Below @md the loadout has wrapped away onto
             its own line, taking that slack with it, so the group needs
             `ml-auto` to stay on the edge instead of hugging the KDA. -->
        <div class="ml-auto flex shrink-0 items-center gap-2 @md:ml-0 @2xl:gap-3">
          <!-- Team compositions: two horizontal rows of 5, allies over
               enemies, each sorted TOP → SUPPORT so a column pairs
               laner-vs-laner (ally above enemy = same role). Ten icons need
               ~7rem, which the row has from @xl once the secondary stats are
               held back to @3xl — narrower than that (a phone, a half-width
               card) they still drop.

               Sized with explicit width/height props, not a responsive
               `size-*` class: SkeletonImage reserves its box from those props
               via an inline style, which is what actually pins the item
               icons below to 24px at every width (their own `size-5
               @2xl:size-6` classes never take effect — inline style always
               wins). Without matching props here these rendered at 20px
               until the row cleared the real @2xl breakpoint, visibly
               smaller than the items sitting right next to them. -->
          <div class="hidden shrink-0 flex-col gap-0.5 @xl:flex">
            <div class="flex gap-0.5">
              <SkeletonImage
                v-for="(p, idx) in allies"
                :key="`ally-${idx}`"
                :src="championById.get(p.championId)?.iconUrl ?? null"
                :alt="participantTooltip(p)"
                :title="participantTooltip(p)"
                :width="24"
                :height="24"
                loading="lazy"
                class="size-6 rounded"
              />
            </div>
            <div class="flex gap-0.5">
              <SkeletonImage
                v-for="(p, idx) in enemies"
                :key="`enemy-${idx}`"
                :src="championById.get(p.championId)?.iconUrl ?? null"
                :alt="participantTooltip(p)"
                :title="participantTooltip(p)"
                :width="24"
                :height="24"
                loading="lazy"
                class="size-6 rounded"
              />
            </div>
          </div>

          <!--
            MVP / ACE accolade + expand chevron. MVP = a crown in the brand
            `gold` accent (best player of the game); ACE = an award rosette in
            the rose `primary` (best player of the losing team). Distinct icon
            *and* colour so the two never blur together, and the same two
            tokens the expanded MatchDetailScoreboard uses, so the collapsed
            crown and the scoreboard crown are the same gold. A UTooltip
            spells out which accolade it is on hover/focus. Chevron rotates
            180°. The accolade sits in a fixed-size slot (rendered empty
            rather than omitted when there's no MVP/ACE) so the chevron next
            to it — and everything left of this group — lines up at the same
            spot whether or not a given row has a badge. -->
          <div class="flex shrink-0 items-center gap-1 @2xl:gap-2">
            <div class="flex size-5 shrink-0 items-center justify-center">
              <UTooltip
                v-if="self.isMvp || self.isAce"
                :text="self.isMvp ? 'MVP' : 'ACE'"
              >
                <UIcon
                  :name="self.isMvp ? 'i-lucide-crown' : 'i-lucide-award'"
                  class="size-5 drop-shadow"
                  :class="self.isMvp ? 'text-gold' : 'text-primary'"
                  :aria-label="self.isMvp ? 'MVP' : 'ACE'"
                />
              </UTooltip>
            </div>
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
