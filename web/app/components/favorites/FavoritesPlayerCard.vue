<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import type { FavoriteTruemain } from '~/utils/favorites'
import { formatPercentage, getProfileIconUrl } from '~~/shared/utils/ddragon'
import { platformIdToRegion } from '~~/shared/utils/region'
import { isApexTier } from '~/utils/tiers'

// One followed player on the favorites view: identity + ranked summary from
// the profile endpoint, then their latest games rendered with the shared
// <MatchRow> used by the profile page's match history.
const props = withDefaults(defineProps<{
  favorite: FavoriteTruemain
  /** Static bundle, fetched once by the page and shared across every card. */
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
  patch: string | null
  /** How many recent games to show per player. */
  matchCount?: number
}>(), {
  matchCount: 3,
})

const nameTag = computed(() => props.favorite.nameTag)

// ─── Fan-out bound (#872) ──────────────────────────────────────────────────
// Both fetches below are per-card, so a full list of 30 favorites used to fire
// ~60 requests in one tick — the only navigation in the app that multiplies
// like that. They are gated on the card approaching the viewport instead: the
// stored identity already draws the header, and the ranked line plus the match
// rows stay on their skeletons until the card's own fetches resolve, so a
// not-yet-fetched card reads as loading and never as a zeroed one.
//
// `hydrate-on-visible` cannot do this job here: the list is rendered after the
// `localStorage` read in `onMounted`, so these cards are never hydrated from
// server markup and Vue never consults a lazy-hydration strategy — see
// `useVisibleOnce`.
const cardEl = ref<HTMLElement | null>(null)
const visible = useVisibleOnce(cardEl)

const {
  data: profile,
  isInitialLoading: profileLoading,
  notFound: profileNotFound,
} = useTruemainProfile(nameTag, { enabled: visible })

const {
  matches,
  isInitialLoading: matchesLoading,
  notFound: matchesNotFound,
} = useTruemainMatches(nameTag, 1, { pageSize: props.matchCount, enabled: visible })

const profileHref = computed(() => `/truemains/${encodeURIComponent(nameTag.value)}`)

// Identity falls back to what was stored when the player was followed, so the
// card has a name to show before (and even if) the profile fetch resolves.
const gameName = computed(() => profile.value?.identity.gameName ?? props.favorite.gameName)
const tagLine = computed(() => profile.value?.identity.tagLine ?? props.favorite.tagLine)

const region = computed(() =>
  platformIdToRegion(profile.value?.identity.platformId) ?? props.favorite.region)

const profileIconUrl = computed(() =>
  getProfileIconUrl(profile.value?.identity.profileIconId ?? props.favorite.profileIconId ?? 0, props.patch))

const ranked = computed(() => profile.value?.ranked ?? null)
const showDivision = computed(() => ranked.value !== null && !isApexTier(ranked.value.tier))

const rankedLabel = computed(() => {
  const value = ranked.value
  if (!value) return null
  const tier = value.tier.charAt(0) + value.tier.slice(1).toLowerCase()
  const division = showDivision.value ? ` ${value.division}` : ''
  return `${tier}${division} · ${value.leaguePoints.toLocaleString('en-US')} LP`
})

const winRateLabel = computed(() => {
  const value = ranked.value
  return value?.winRate == null ? null : formatPercentage(value.winRate, 0)
})

// MatchRow needs the whole static bundle to draw items, spells and runes —
// keep the skeletons up until every part has landed.
const staticBundleReady = computed(() =>
  props.champions.length > 0
  && Object.keys(props.items).length > 0
  && Object.keys(props.summonerSpells).length > 0
  && (props.runeTree?.styles.length ?? 0) > 0,
)
</script>

<template>
  <section ref="cardEl" class="surface overflow-hidden rounded-lg">
    <!-- Player header -->
    <div class="flex items-center gap-3 border-b border-default/60 px-3 py-2.5">
      <NuxtLink
        :to="profileHref"
        class="surface-hover -m-1 flex min-w-0 flex-1 items-center gap-3 rounded-md border border-transparent p-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      >
        <SkeletonImage
          :src="profileIconUrl"
          :alt="gameName"
          class="size-10 shrink-0 rounded"
          width="40"
          height="40"
        />
        <div class="min-w-0">
          <div class="flex items-baseline gap-1 truncate">
            <span class="truncate font-bold text-default">{{ gameName }}</span>
            <span v-if="tagLine" class="shrink-0 text-xs text-muted">#{{ tagLine }}</span>
          </div>
          <div class="mt-0.5 flex items-center gap-2">
            <LeaderboardRegionFlag :region="region" :width="18" />
            <USkeleton v-if="profileLoading" class="h-3 w-28" />
            <span v-else-if="rankedLabel" class="text-xs text-muted tabular-nums">
              {{ rankedLabel }}
              <template v-if="winRateLabel"> · {{ winRateLabel }} WR</template>
            </span>
            <span v-else class="text-xs text-muted">Unranked</span>
          </div>
        </div>
      </NuxtLink>

      <RankIcon v-if="ranked" :tier="ranked.tier" :size="28" class="shrink-0" />

      <FavoriteToggle
        :game-name="favorite.gameName"
        :tag-line="favorite.tagLine"
        :region="favorite.region"
        :profile-icon-id="favorite.profileIconId"
      />
    </div>

    <!-- Latest games -->
    <div class="space-y-1.5 p-2">
      <template v-if="profileNotFound">
        <p class="px-1 py-3 text-center text-sm text-muted">
          This player is no longer tracked. Unfollow them to clean up the list.
        </p>
      </template>
      <template v-else-if="matchesLoading || !staticBundleReady">
        <MatchRowSkeleton v-for="i in matchCount" :key="`fav-skel-${i}`" />
      </template>
      <template v-else-if="matchesNotFound || matches.length === 0">
        <p class="px-1 py-3 text-center text-sm text-muted">
          No tracked matches yet.
        </p>
      </template>
      <template v-else>
        <MatchRow
          v-for="match in matches"
          :key="match.matchId"
          :match="match"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree!"
          :name-tag="nameTag"
        />
        <NuxtLink
          :to="profileHref"
          class="block px-1 pt-1 text-center text-xs font-medium text-primary hover:underline"
        >
          Full match history
        </NuxtLink>
      </template>
    </div>
  </section>
</template>
