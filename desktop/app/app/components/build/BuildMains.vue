<script setup lang="ts">
import type { LeaderboardResponse, LeaderboardRowResponse, RegionSlug } from '#shared/types/leaderboard'
import type { ProfileIdentity } from '#shared/types/profile'
import type { SearchResult } from '#shared/types/search'
import { getProfileIconUrl } from '#shared/utils/ddragon'
import { isApexTier } from '#common/utils/tiers'

/**
 * The champion's best true mains — what the reference client lists as pro
 * builds. The row is the site's compact one (the home page's "Top truemains",
 * `home/TruemainsPanel.vue`), fitted to the build view's narrow column — the
 * tag under the name, beside the flag, so the name keeps the width: every row mains the
 * champion on screen, so its icon gives way to the keystone and first item the
 * main runs on it, and the Riot ID keeps the width it needs. The full
 * leaderboard row does not fit here — it would leave the name 50 px. A row
 * selects that main's own build on the champion, shown like any other build.
 * The search above the list reaches any other main of the champion by name.
 *
 * The mains the player follows come first, starred (#1733): their keystone and
 * first item are read from their own build on the champion, which a click then
 * shows without another request. A main picked in the search joins the top of
 * the list while the view is open (#1985), read the same way.
 */
const props = defineProps<{
  championId: number
  /** The lane the view is for — what a main's own build is asked on first. */
  position: string
  /** The main whose build is on screen, by Riot ID slug. */
  selectedNameTag?: string | null
}>()

const emit = defineEmits<{ select: [main: { identity: ProfileIdentity }] }>()

const TOP_N = 5

const cache = useState<Record<number, LeaderboardRowResponse[]>>('champion-mains', () => ({}))
const failed = ref(false)

const topRows = computed(() => cache.value[props.championId] ?? null)

watch(() => props.championId, async (id) => {
  failed.value = false
  if (cache.value[id]) return
  try {
    const answer = await apiGet<LeaderboardResponse>('/truemains', { championId: id, pageSize: TOP_N })
    cache.value = { ...cache.value, [id]: answer.rows }
  }
  catch {
    failed.value = true
  }
}, { immediate: true })

const { runeTree, items } = useStaticData()
const { patch } = useChampionStatics()
const { perk, item } = useBuildResolvers(runeTree, items)

const favoriteMains = useFavoriteMains(() => props.championId)
const truemainBuilds = useTruemainBuild()

/** The mains picked in the search, per champion, latest first — kept while the view is open, never saved. */
const searched = ref<Record<number, SearchResult[]>>({})
const searchedHere = computed(() => searched.value[props.championId] ?? [])

const nameTagOf = (identity: ProfileIdentity) => favoriteNameTag(identity.gameName, identity.tagLine)

function pickSearched(result: SearchResult) {
  const key = nameTagOf(result.identity).toLowerCase()
  const others = searchedHere.value.filter(entry => nameTagOf(entry.identity).toLowerCase() !== key)
  searched.value = { ...searched.value, [props.championId]: [result, ...others] }
  emit('select', result)
}

// A followed or searched main's own build gives their row its keystone and first item.
watch([favoriteMains, searchedHere, () => props.position], ([mains, picked, position]) => {
  for (const { favorite } of mains) truemainBuilds.load(favorite.nameTag, props.championId, position)
  for (const result of picked) truemainBuilds.load(nameTagOf(result.identity), props.championId, position)
}, { immediate: true })

/** One row of the list, whichever source it comes from. */
interface MainRow {
  nameTag: string
  identity: ProfileIdentity
  region: RegionSlug | null
  ranked: { tier: string, division: string, leaguePoints: number, wins: number | null, losses: number | null, winRate: number | null } | null
  keystoneId: number | null
  firstItemId: number | null
  followed: boolean
}

const rows = computed<MainRow[] | null>(() => {
  const followed: MainRow[] = favoriteMains.value.map(({ favorite, profile }) => {
    const build = truemainBuilds.answerOf(favorite.nameTag, props.championId, props.position)?.builds[0]
    return {
      nameTag: favorite.nameTag,
      identity: profile.identity,
      region: favorite.region,
      ranked: profile.ranked,
      keystoneId: build?.primaryKeystoneId ?? null,
      firstItemId: build?.firstItemId ?? null,
      followed: true,
    }
  })
  const fromSearch: MainRow[] = searchedHere.value.map((result) => {
    const nameTag = nameTagOf(result.identity)
    const build = truemainBuilds.answerOf(nameTag, props.championId, props.position)?.builds[0]
    return {
      nameTag,
      identity: result.identity,
      region: result.region,
      ranked: result.ranked && { ...result.ranked, wins: null, losses: null, winRate: null },
      keystoneId: build?.primaryKeystoneId ?? null,
      firstItemId: build?.firstItemId ?? null,
      followed: false,
    }
  })
  const followedKeys = new Set(followed.map(row => row.nameTag.toLowerCase()))
  // A searched main already followed or in the top keeps that row: it is selected there, not repeated.
  const listedKeys = new Set([...followedKeys, ...(topRows.value ?? []).map(row => nameTagOf(row.identity).toLowerCase())])
  const extra = fromSearch.filter(row => !listedKeys.has(row.nameTag.toLowerCase()))
  if (topRows.value === null) return extra.length || followed.length ? [...extra, ...followed] : null
  const top: MainRow[] = topRows.value
    .filter(row => !followedKeys.has(nameTagOf(row.identity).toLowerCase()))
    .map((row) => {
      const onChampion = row.topChampions.find(champion => champion.championId === props.championId)
      return {
        nameTag: nameTagOf(row.identity),
        identity: row.identity,
        region: row.region,
        ranked: row.ranked && { ...row.ranked, wins: row.stats.wins, losses: row.stats.losses, winRate: row.stats.winRate },
        keystoneId: onChampion?.primaryKeystoneId ?? null,
        firstItemId: onChampion?.firstItemId ?? null,
        followed: false,
      }
    })
  return [...extra, ...followed, ...top]
})
</script>

<template>
  <div class="flex flex-col gap-0.5">
    <h3 class="px-2 pb-1 stat-label">Truemains</h3>
    <LeaderboardTruemainSearch
      :champion-id="championId"
      placeholder="Search a truemain…"
      class="mb-1 px-1"
      @player="pickSearched"
    />

    <template v-if="rows === null && !failed">
      <div v-for="index in 3" :key="index" class="flex items-center gap-2 px-2 py-1.5">
        <USkeleton class="size-8 rounded-lg" />
        <USkeleton class="h-4 w-32" />
        <USkeleton class="ml-auto h-4 w-12" />
      </div>
    </template>
    <p v-else-if="failed && !rows?.length" class="px-2 text-xs text-muted">The truemains could not be loaded</p>
    <UEmpty v-else-if="rows && rows.length === 0" size="sm" icon="i-lucide-trophy" description="No tracked truemains on this champion yet." />

    <template v-else>
      <div
        v-for="row in rows ?? []"
        :key="row.nameTag"
        class="relative flex items-center gap-2 rounded-lg px-2 py-1.5 transition-colors"
        :class="selectedNameTag === row.nameTag ? 'bg-accented' : 'hover:bg-elevated'"
      >
        <span v-if="selectedNameTag === row.nameTag" class="absolute inset-y-2 left-0 w-0.5 rounded-full bg-primary" />
        <button
          type="button"
          :aria-label="`Build of ${row.identity.gameName}${row.identity.tagLine ? ` #${row.identity.tagLine}` : ''}`"
          :aria-pressed="selectedNameTag === row.nameTag"
          class="absolute inset-0 rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          @click="emit('select', row)"
        />
        <SkeletonImage
          :src="getProfileIconUrl(row.identity.profileIconId, patch)"
          :alt="row.identity.gameName"
          width="32"
          height="32"
          class="size-8 shrink-0 rounded-lg"
        />
        <!-- The tag goes under the name, beside the flag: the narrow column keeps its width for the name. -->
        <div class="min-w-0 flex-1 leading-tight">
          <p class="truncate text-[13px] font-semibold text-default">{{ row.identity.gameName }}</p>
          <div class="mt-0.5 flex items-center gap-1">
            <UIcon v-if="row.followed" mode="svg" name="i-lucide-star" class="size-3 shrink-0 text-primary [&_path]:fill-current" aria-label="Followed" />
            <LeaderboardRegionFlag v-if="row.region" :region="row.region" :width="14" />
            <span v-if="row.identity.tagLine" class="truncate text-[11px] text-muted">#{{ row.identity.tagLine }}</span>
          </div>
        </div>
        <div class="relative z-10 flex shrink-0 items-center gap-0.5">
          <GameTooltipPerkIcon :perk="perk(row.keystoneId)" :width="22" :height="22" class="size-[22px] shrink-0 rounded-full" />
          <GameTooltipItemIcon :item="item(row.firstItemId)" :width="22" :height="22" class="size-[22px] shrink-0 rounded" />
        </div>
        <UTooltip
          v-if="row.ranked"
          :delay-duration="150"
          :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
        >
          <div class="relative z-10 flex shrink-0 items-center gap-0.5">
            <RankIcon :tier="row.ranked.tier" :size="24" />
            <span v-if="!isApexTier(row.ranked.tier)" class="text-xs font-semibold tabular-nums">{{ row.ranked.division }}</span>
          </div>
          <template #content>
            <GameTooltipSurface>
              <RankSummary
                :tier="row.ranked.tier"
                :division="row.ranked.division"
                :league-points="row.ranked.leaguePoints"
                :wins="row.ranked.wins"
                :losses="row.ranked.losses"
                :win-rate="row.ranked.winRate"
                :size="32"
              />
            </GameTooltipSurface>
          </template>
        </UTooltip>
      </div>
    </template>
  </div>
</template>
