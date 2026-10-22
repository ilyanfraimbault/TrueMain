<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { riotIdLabel, truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { isOneTrick } from '#common/utils/leaderboard-row'

// One row of the champion page's truemains. The whole row navigates to the
// player's profile via a stretched overlay link, while the top-champion icons
// are their own links to that champion's player-scoped build page — siblings
// of the overlay, never nested <a> inside <a>. Its columns are the `/truemains`
// table's (#2028): the same lanes, champions, score and rank emblem.
const props = defineProps<{
  row: LeaderboardRowResponse
  championsById: Map<number, ChampionStaticListItem>
  /** Static rune tree + item map, to draw the main champion's keystone + first item. */
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  patch: string | null
  /** True when the leaderboard is ranked by dedication — accents the column that drives the order. */
  highlightDedication?: boolean
  /**
   * Deepest ordinal in the list this row belongs to. Sizes the rank slot for
   * the whole page rather than for this row, so a page straddling a decade
   * boundary (76…100) keeps one slot width. Falls back to the row's own rank.
   */
  maxRank?: number
}>()

// Slug for this player's truemain pages — `{gameName}-{tagLine}` (or just the
// name when untagged). Drives the profile link and the player-scoped champion
// links; the slug is URL-encoded by the path helper and <ChampionLink>.
const rowNameTag = computed(() => truemainNameTag(props.row.identity.gameName, props.row.identity.tagLine))
const profileHref = computed(() => truemainProfilePath(rowNameTag.value))

// The stretched profile link is an empty overlay (no text), so it needs an
// explicit accessible name.
const profileAriaLabel = computed(() => riotIdLabel(props.row.identity.gameName, props.row.identity.tagLine))

const isOtp = computed(() => isOneTrick(props.row.topChampions))

const { championName, mainBuild } = useLeaderboardChampions(() => props.championsById, () => props.runeTree, () => props.itemsMap)
const main = computed(() => mainBuild(props.row))
</script>

<template>
  <!-- Fixed column rhythm so the row never reflows with how much data a
       player happens to have: only the name and the two spacers grow, and
       every data column between them (positions, champion build, dedication,
       rank, stats) holds a fixed width that lines up down the list. The row
       is its own @container so the columns respond to the width it's actually
       given — compact in the champion-page sidebar — instead of the viewport.
       "Fixed" has to hold all the way down, not just at the column boundary:
       a cell that disappears (no KDA) or a cluster that shrinks (no aggregated
       build, no sub-mains) hands its width back to the growers, which then
       re-split it and drag every column between them off the grid. So each
       column below reserves its sub-slots too, and a figure the aggregate
       can't supply renders as an em dash rather than as nothing. -->
  <ListRowSurface dense class="group @container relative gap-1.5">
    <!-- Stretched profile link: a sibling overlay (not a wrapper) so the
         champion icons can be their own links without nesting <a> in <a>.
         Static content falls through to it; the controls above it opt out
         with `relative z-10`. -->
    <NuxtLink
      :to="profileHref"
      :aria-label="profileAriaLabel"
      class="absolute inset-0 z-[1] rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
    />
    <!-- Narrow rows (the champion page's sidebar) get the tighter slot: every
         pixel the ordinal gives back goes to the Riot ID, which is the column
         that actually truncates there. -->
    <LeaderboardRowPlace :rank="row.rank" :max-rank="maxRank" />

    <!-- The player, through the shared identity (#1734) with no link of its
         own — the stretched overlay above is the row's link. Given a heavier
         flex-grow than the centring spacers so the Riot ID claims roughly half
         the free space (fitting untruncated) while the spacers still keep the
         champion roughly centred; capped so it can't run away on ultra-wide
         screens. The basis and caps carry the 40px avatar + gap. The tag never
         truncates — the name absorbs the clipping. -->
    <Account
      :identity="row.identity"
      :region="row.region"
      :patch="patch"
      size="lg"
      :to="false"
      loading="lazy"
      class="flex-[3_1_3.25rem] @2xl:max-w-[21.25rem] @4xl:max-w-[23.25rem] @5xl:max-w-[27.25rem]"
    >
      <!-- Lifted above the stretched profile link so its tooltip is reachable. -->
      <template v-if="isOtp" #badge>
        <OtpBadge class="relative z-10 self-center" />
      </template>
    </Account>

    <!-- Room for two lane icons on every row, so the layout never shifts
         whether a player has a secondary lane or no position data at all.
         Hidden on narrow rows to keep them readable.
         The tiers below are sized against the row's fixed columns, not picked
         by eye: the name is the only column that shrinks, so a tier that turns
         on more fixed width than the row has left crushes it to nothing and the
         next column paints over the avatar. Lanes join at @2xl (the name keeps
         ~200px next to the stat block), the 256px champion build only at @5xl
         (~734px of fixed columns once it's in, ~290px left for the name). -->
    <LeaderboardLanes :positions="row.positions" class="hidden w-16 shrink-0 @2xl:flex" />

    <!-- Left spacer: with the right spacer below, the two centre the champion
         column between the name and the stat block on wide rows. -->
    <div class="hidden flex-1 @5xl:block" />

    <!-- Champion build (fixed-width slot, centred). Every sub-slot inside it
         holds its width too, so the column measures the same on a row whose
         player has one champion and no aggregated build as on one with three
         champions and a full build. -->
    <LeaderboardChampions
      :main="main"
      :top-champions="row.topChampions"
      :champions-by-id="championsById"
      :name-tag="rowNameTag"
      class="relative z-10 hidden w-64 shrink-0 overflow-hidden @5xl:flex"
    />

    <!-- Truemain score. Always reserved (empty slot when the account has no
         main-champion analysis yet) so the rank and stat columns never shift.
         Kept visible at every row width, unlike the games/KDA/WR cluster: it is
         the leaderboard's signature column. No verdict word under the number:
         the OTP pill next to the name already says it, from the same flag. -->
    <LeaderboardScore
      v-if="row.dedication"
      :dedication="row.dedication"
      :champion-name="championName(row.dedication.championId)"
      :highlighted="highlightDedication"
      labelled
      class="relative z-10 w-14 shrink-0 text-sm text-default @xl:w-16"
    />
    <div v-else class="w-14 shrink-0 @xl:w-16" />

    <!-- The crest carries the visual weight; the LP figure is dropped (too
         wide for the row) and the full standing is on hover. -->
    <RankEmblem
      v-if="row.ranked"
      :rank="row.ranked"
      :record="row.stats"
      class="relative z-10 w-12 justify-end"
    />
    <div v-else class="w-12 shrink-0" />

    <!-- Flex spacer pushes the stat block to the far right while the columns
         above stay fixed. -->
    <div class="hidden flex-1 @xl:block" />

    <LeaderboardRowStats :stats="row.stats" class="hidden @xl:flex" />

    <!-- Follow toggle, pinned to the row's trailing edge on every breakpoint
         (the stat block above collapses on narrow rows, the star does not). -->
    <FavoriteToggle
      :game-name="row.identity.gameName"
      :tag-line="row.identity.tagLine"
      :region="row.region"
      :profile-icon-id="row.identity.profileIconId"
    />
  </ListRowSurface>
</template>
