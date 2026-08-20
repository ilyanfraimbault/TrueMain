<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage, getPositionIconUrl, getProfileIconUrl } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { isApexTier } from '~/utils/tiers'
import { winRateTone } from '~/utils/rate-tone'

// One row of the leaderboard. The whole row navigates to the player's profile
// via a stretched overlay link, while the top-champion icons are their own
// links to that champion's player-scoped build page — siblings of the overlay,
// never nested <a> inside <a>.
const props = defineProps<{
  row: LeaderboardRowResponse
  championsById: Map<number, ChampionStaticListItem>
  /** Static rune tree + item map, to draw the main champion's keystone + first item. */
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  patch: string | null
  /** True when the leaderboard is ranked by dedication — accents the column that drives the order. */
  highlightDedication?: boolean
}>()

const profileHref = computed(() => {
  const tag = props.row.identity.tagLine
  return tag
    ? `/truemains/${encodeURIComponent(`${props.row.identity.gameName}-${tag}`)}`
    : `/truemains/${encodeURIComponent(props.row.identity.gameName)}`
})

// Slug for this player's truemain pages — `{gameName}-{tagLine}` (or just the
// name when untagged). Drives the player-scoped champion links below; the slug
// is URL-encoded by <ChampionLink>.
const rowNameTag = computed(() => {
  const { gameName, tagLine } = props.row.identity
  return tagLine ? `${gameName}-${tagLine}` : gameName
})

// The stretched profile link is an empty overlay (no text), so it needs an
// explicit accessible name.
const profileAriaLabel = computed(() => {
  const { gameName, tagLine } = props.row.identity
  return tagLine ? `${gameName} #${tagLine}` : gameName
})

const profileIconUrl = computed(() =>
  getProfileIconUrl(props.row.identity.profileIconId, props.patch))

// One-trick pony marker next to the name. A player can be an OTP of at most one
// champion (play rate ≥ 85% leaves no room for a second main), so any flagged
// top champion makes the whole player an OTP — mirrors the profile page's
// per-champion amber pill, surfaced here at the player level.
const isOtp = computed(() => props.row.topChampions.some(champion => champion.isOtp))

// The rank cell is a fixed slot so the ordinals line up down the list, which
// means a long ordinal has to fit the slot rather than the slot fitting the
// ordinal — `#172` was already spilling out of it and over the avatar. Both the
// slot and the label size are picked from the ordinal's digit count: the slot
// only grows where it has to (the champion-page sidebar lists a top ten, so it
// keeps the tight two-digit slot that leaves its width to the Riot ID), and
// past four digits the label steps down instead of widening the column further.
// Rows in one list share a digit count except across a decade boundary, so this
// stays a single width per list in practice.
const rankClass = computed(() => {
  switch (String(props.row.rank).length) {
    case 1:
    case 2:
      return 'w-6 text-xs @xl:w-8 @xl:text-sm'
    case 3:
      return 'w-8 text-xs @xl:w-10 @xl:text-sm'
    case 4:
      return 'w-9 text-[11px] @xl:w-10 @xl:text-xs'
    case 5:
      return 'w-10 text-[10px] @xl:w-12 @xl:text-xs'
    default:
      return 'w-10 text-[8px] @xl:w-12 @xl:text-[10px]'
  }
})

const ranked = computed(() => props.row.ranked)
const showDivision = computed(() => ranked.value !== null && !isApexTier(ranked.value.tier))

// An em dash rather than a hidden cell: the stat trio is a fixed three-column
// block, and dropping a cell for a player whose aggregate has no KDA yet
// (accounts whose tracked games haven't been ingested — half the leaderboard's
// eligible population at any time) widened the free space the row's spacers
// share and slid every column left of them out of line with its neighbours.
const EMPTY_STAT = '—'

const winRateLabel = computed(() => {
  const wr = props.row.stats.winRate
  return wr === null ? EMPTY_STAT : formatPercentage(wr, 0)
})
const kdaLabel = computed(() => {
  const kda = props.row.stats.kda
  return kda === null ? EMPTY_STAT : kda.toFixed(1)
})

// Shared with the homepage teaser — resolve build ids the same way the
// fetching composable does.
const { perk, perkStyle, item: buildItem } = useBuildResolvers(() => props.runeTree, () => props.itemsMap)

function championName(id: number): string {
  return props.championsById.get(id)?.name ?? `#${id}`
}
function championIcon(id: number): string | null {
  return props.championsById.get(id)?.iconUrl ?? null
}

// Riot-stored position string → label for the role-icon tooltips. Reuses the
// canonical POSITION_BY_VALUE (shared with the role picker and tier list) so
// the leaderboard label never drifts from the rest of the app.
function positionLabel(position: string): string {
  return POSITION_BY_VALUE.get(position)?.label ?? position
}

// Dedication score for the row's signature champion. Every figure here comes
// straight from the API payload — the breakdown is never recomputed client-side,
// so the tooltip can't drift from the number the backend ranked on.
const dedicationLabel = computed(() =>
  props.row.dedication === null ? null : formatDedicationScore(props.row.dedication.score))

// Tier word + colour replace the old static "dedication" caption, so the
// score reads (rose-gold→iron, best→worst) without needing the hover below.
const dedicationTierLabel = computed(() =>
  props.row.dedication === null ? null : dedicationTier(props.row.dedication.score))

const dedicationColorClass = computed(() =>
  props.row.dedication === null ? 'text-muted' : dedicationTierColor(props.row.dedication.score))

const dedicationChampionName = computed(() => {
  const dedication = props.row.dedication
  return dedication ? championName(dedication.championId) : null
})

const dedicationBreakdown = computed(() => {
  const dedication = props.row.dedication
  return dedication ? dedicationComponents(dedication) : []
})

const dedicationScoreLabel = computed(() =>
  props.row.dedication === null ? null : props.row.dedication.score.toFixed(1))

// Sub-mains shown beside the signature champion. The API returns up to three
// top champions, so there are always exactly two slots here — padded with
// nulls for players who have fewer, because the column sits between the row's
// two spacers and a variable number of icons would move everything around it.
const SUB_CHAMPION_SLOTS = 2

const subChampions = computed(() => {
  const subs = props.row.topChampions.slice(1, SUB_CHAMPION_SLOTS + 1)
  return Array.from({ length: SUB_CHAMPION_SLOTS }, (_, index) => subs[index] ?? null)
})

// Plain <img> + a URL built here instead of <NuxtImg> — same `_ipx/…` URL,
// minus the responsive srcset machinery a fixed 22px icon never needed. See
// SkeletonImage.vue for the profiling rationale. The URL itself comes from the
// shared helper so this glyph resolves to the same cache entry as every other
// place that renders it, at whatever size they display it.
const canonicalIcon = useCanonicalIcon()

// Primary + secondary lane icons. Each entry carries its icon URL and a
// tooltip. The list is empty when the backend has no position data (no main
// analysis yet), so the slot collapses without shifting the row.
const positionIcons = computed(() => {
  const positions = props.row.positions
  if (!positions) {
    return []
  }
  const icons = [{
    position: positions.primary,
    iconUrl: getPositionIconUrl(positions.primary),
    title: `Primary: ${positionLabel(positions.primary)}`,
    primary: true,
  }]
  if (positions.secondary) {
    icons.push({
      position: positions.secondary,
      iconUrl: getPositionIconUrl(positions.secondary),
      title: `Secondary: ${positionLabel(positions.secondary)}`,
      primary: false,
    })
  }
  return icons
})
</script>

<template>
  <!-- Fixed column rhythm so the row never reflows with how much data a
       player happens to have: only the name and the two spacers grow, and
       every data column between them (positions, champion build, dedication,
       rank, stats) holds a fixed width that lines up down the list. The row
       is its own @container so the columns respond to the width it's actually
       given — full-width on /truemains, compact in the champion-page sidebar
       — instead of the viewport.
       "Fixed" has to hold all the way down, not just at the column boundary:
       a cell that disappears (no KDA) or a cluster that shrinks (no aggregated
       build, no sub-mains) hands its width back to the growers, which then
       re-split it and drag every column between them off the grid. So each
       column below reserves its sub-slots too, and a figure the aggregate
       can't supply renders as an em dash rather than as nothing. -->
  <ListRowSurface
    class="group @container relative gap-1.5"
  >
    <!-- Stretched profile link: a sibling overlay (not a wrapper) so the
         champion icons can be their own links without nesting <a> in <a>.
         Static content falls through to it; the top-champion links opt out
         with `relative z-10`. -->
    <NuxtLink
      :to="profileHref"
      :aria-label="profileAriaLabel"
      class="absolute inset-0 z-[1] rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
    />
    <!-- Rank. Narrow rows (the champion page's sidebar) get the tighter slot:
         the ordinal is the least informative column in the row, and every pixel
         it gives back goes to the Riot ID, which is the column that actually
         truncates there. Deeper ordinals widen the slot a step at a time (out
         of the flex spacers) and then shrink the label — see `rankClass`. -->
    <span
      class="shrink-0 text-center font-semibold tabular-nums leading-none text-muted"
      :class="rankClass"
    >
      #{{ row.rank }}
    </span>

    <!-- Avatar -->
    <!-- Rows appear in long lists (/truemains) and in the champion page's
         sidebar, always below the build panel: lazy so they leave the initial
         request burst to the above-the-fold icons. Rows near the viewport are
         unaffected — the browser fetches lazy images well before they scroll
         in. Same call the match-history rows already make. -->
    <SkeletonImage
      v-if="profileIconUrl"
      :src="profileIconUrl"
      :alt="row.identity.gameName"
      class="size-10 shrink-0 rounded"
      width="40"
      height="40"
      loading="lazy"
    />
    <div v-else class="size-10 shrink-0 rounded bg-elevated/60" aria-hidden="true" />

    <!-- Name + tag, region flag sits under the name as a small badge. Given a
         heavier flex-grow than the centring spacers so the Riot ID claims
         roughly half the free space (fitting untruncated) while the spacers
         still keep the champion roughly centred. Capped so it can't run away on
         ultra-wide screens. -->
    <div class="min-w-0 flex-[3] @2xl:max-w-72 @4xl:max-w-80 @5xl:max-w-96">
      <!-- Game name and tag are one identifier, so the tag never truncates: it
           holds its width (`shrink-0`) and the name absorbs the clipping. The
           name is set a notch below the row's body size and the tag a notch
           below that — the hierarchy reads without the name having to be big,
           which is what let the pair fit in the sidebar at all. -->
      <div class="flex items-baseline gap-1 truncate">
        <span class="truncate text-sm font-bold text-default">{{ row.identity.gameName }}</span>
        <span v-if="row.identity.tagLine" class="shrink-0 text-[11px] text-muted">#{{ row.identity.tagLine }}</span>
        <!-- One-trick pony marker. `relative z-10` lifts it above the stretched
             profile-link overlay so its tooltip is reachable on hover. -->
        <span
          v-if="isOtp"
          class="relative z-10 shrink-0 self-center rounded-full bg-amber-400/25 px-1.5 py-0.5 text-[9px] font-bold uppercase leading-none tracking-wide text-amber-200 ring-1 ring-amber-400/50"
          title="One-trick pony"
        >
          OTP
        </span>
      </div>
      <LeaderboardRegionFlag :region="row.region" :width="18" class="mt-0.5" />
    </div>

    <!-- Primary / secondary lane. Same 22px icon as the champion list's
         position column; the primary lane matches its full opacity, and only
         the secondary lane (a concept the champion list has no equivalent of)
         stays dimmed to read as lower priority. Fixed-width slot (room for two
         22px icons + gap) reserved on every row so the layout never shifts
         whether a player has a secondary lane, or no position data at all.
         Hidden on narrow rows to keep them readable. -->
    <div class="hidden w-16 shrink-0 items-center gap-1 @xl:flex">
      <img
        v-for="role in positionIcons"
        :key="role.position"
        :src="canonicalIcon(role.iconUrl)"
        :alt="role.title"
        :title="role.title"
        class="size-[22px] shrink-0"
        :class="role.primary ? undefined : 'opacity-40'"
        width="22"
        height="22"
        loading="lazy"
      >
    </div>

    <!-- Left spacer: with the right spacer below, the two centre the champion
         column between the name and the stat block on wide rows. -->
    <div class="hidden flex-1 @2xl:block" />

    <!-- Champion build (fixed-width slot, centred). Every sub-slot inside it
         holds its width too — the build cluster via `reserve-slots`, the
         sub-mains via the padded `subChampions` list — so the column measures
         the same on a row whose player has one champion and no aggregated
         build as on one with three champions and a full build. Reserving only
         the outer 16rem was not enough: a narrower cluster centred itself
         differently, which moved the signature champion out of line down the
         list. -->
    <div class="relative z-10 hidden w-64 shrink-0 items-center justify-center gap-3 overflow-hidden @2xl:flex">
      <!-- 9rem: 30px icon + 40px play rate + two 22px build icons + the three
           0.5rem gaps between them, i.e. the cluster at its widest. -->
      <div class="flex w-36 shrink-0 justify-center">
        <LeaderboardChampionBuild
          v-if="row.topChampions[0]"
          :champion="row.topChampions[0]"
          :name="championName(row.topChampions[0].championId)"
          :icon-url="championIcon(row.topChampions[0].championId)"
          :name-tag="rowNameTag"
          :keystone="perk(row.topChampions[0].primaryKeystoneId)"
          :secondary-style="perkStyle(row.topChampions[0].secondaryStyleId)"
          :first-item="buildItem(row.topChampions[0].firstItemId)"
          reserve-slots
          loading="lazy"
        />
      </div>

      <!-- Two 1.5rem icons + the 0.25rem gap between them. -->
      <div class="hidden w-[52px] shrink-0 items-center gap-1 @5xl:flex">
        <template v-for="(champ, index) in subChampions" :key="champ?.championId ?? `empty-${index}`">
          <ChampionLink
            v-if="champ && championIcon(champ.championId)"
            :champion-id="champ.championId"
            :name="championName(champ.championId)"
            :icon-url="championIcon(champ.championId)"
            :name-tag="rowNameTag"
            :title="`${championName(champ.championId)} · ${champ.games} games`"
            class="size-6"
          />
          <div
            v-else-if="champ"
            class="size-6 shrink-0 rounded bg-elevated/60"
            :title="`#${champ.championId} · ${champ.games} games`"
            aria-hidden="true"
          />
          <!-- No sub-main in this slot: an empty box, not a placeholder tile —
               the row has nothing to show there, and a filled square would read
               as a champion whose icon failed to load. -->
          <div v-else class="size-6 shrink-0" aria-hidden="true" />
        </template>
      </div>
    </div>

    <!-- Dedication. Always reserved (empty slot when the account has no
         main-champion analysis yet) so the LP and stat columns never shift.
         Kept visible at every row width, unlike the games/KDA/WR cluster: it is
         the leaderboard's signature column, and the sort key when the board is
         ranked by it. Coloured by tier (rose-gold→iron, same scale as
         `TierBadge`'s S..D) so the score reads without a hover; the tooltip
         underneath still carries the full component breakdown. `relative z-10`
         lifts the trigger above the stretched profile-link overlay — like the
         champion column below — so it actually receives the hover/focus that
         opens it. -->
    <UTooltip
      v-if="row.dedication"
      :delay-duration="150"
      :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
    >
      <div class="relative z-10 flex w-14 shrink-0 flex-col items-end @xl:w-16">
        <span
          class="text-sm font-semibold tabular-nums"
          :class="[dedicationColorClass, { 'underline decoration-dotted underline-offset-2': highlightDedication }]"
        >{{ dedicationLabel }}</span>
        <span
          class="text-[10px] font-medium"
          :class="dedicationColorClass"
        >{{ dedicationTierLabel }}</span>
      </div>

      <template #content>
        <GameTooltipSurface>
          <p class="mb-2 text-xs font-semibold text-default">
            Dedication {{ dedicationScoreLabel }}/100 · {{ dedicationChampionName }}
          </p>
          <DedicationBreakdown :components="dedicationBreakdown" />
        </GameTooltipSurface>
      </template>
    </UTooltip>
    <div v-else class="w-14 shrink-0 @xl:w-16" />

    <!-- Rank emblem. The tier crest carries the visual weight; the LP figure
         is dropped (too wide for the row) and only the division survives for
         the few non-apex rows, since the crest alone can't show it. Full LP
         is still available on hover via the title. -->
    <div
      v-if="ranked"
      class="flex w-12 shrink-0 items-center justify-end gap-1"
      :title="`${ranked.tier}${showDivision ? ' ' + ranked.division : ''} · ${ranked.leaguePoints.toLocaleString('en-US')} LP`"
    >
      <RankIcon :tier="ranked.tier" :size="26" loading="lazy" />
      <span v-if="showDivision" class="text-sm font-semibold tabular-nums">
        {{ ranked.division }}
      </span>
    </div>
    <div v-else class="w-12 shrink-0" />

    <!-- Flex spacer pushes the stat block to the far right while the columns
         above stay fixed. -->
    <div class="hidden flex-1 @xl:block" />

    <!-- Games / KDA / WR (far right, fixed widths — the widths are load-bearing
         for the @container tiers above, so they stay exactly as they were).
         All three cells are always rendered, an em dash standing in for a
         figure the aggregate can't supply: dropping a cell handed its width
         back to the row's flex spacers, which the name column and both spacers
         then re-split, nudging every column between them off the grid the rest
         of the list sits on.
         Only the win rate takes a colour: games is a count and KDA has no
         agreed neutral point, so neither has a better/worse reading to encode.
         `rate-tone` owns the win-rate band, the same as everywhere else — and
         returns the muted tone for a null rate, which is exactly what the em
         dash wants. -->
    <div class="hidden shrink-0 items-center gap-4 @xl:flex">
      <div class="flex w-12 justify-end">
        <StatBlock
          :value="row.stats.games.toLocaleString()"
          label="games"
          size="sm"
          align="end"
        />
      </div>
      <div class="flex w-12 justify-end">
        <StatBlock
          :value="kdaLabel"
          label="KDA"
          size="sm"
          align="end"
        />
      </div>
      <div class="flex w-12 justify-end">
        <StatBlock
          :value="winRateLabel"
          label="WR"
          size="sm"
          align="end"
          :value-class="winRateTone(row.stats.winRate)"
        />
      </div>
    </div>

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
