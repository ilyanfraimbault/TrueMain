<script setup lang="ts">
import type { LeaderboardResponse, LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ProfileIdentity } from '~~/shared/types/profile'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { isApexTier } from '~/utils/tiers'

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
 */
const props = defineProps<{
  championId: number
  /** The main whose build is on screen, by Riot ID slug. */
  selectedNameTag?: string | null
}>()

const emit = defineEmits<{ select: [main: { identity: ProfileIdentity }] }>()

const TOP_N = 5

const cache = useState<Record<number, LeaderboardRowResponse[]>>('champion-mains', () => ({}))
const failed = ref(false)

const rows = computed(() => cache.value[props.championId] ?? null)

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

/** What the main runs on this champion: the entry of their top champions that is this one. */
const onChampion = (row: LeaderboardRowResponse) => row.topChampions.find(champion => champion.championId === props.championId) ?? null

const nameTagOf = (row: LeaderboardRowResponse) => favoriteNameTag(row.identity.gameName, row.identity.tagLine)
</script>

<template>
  <div class="flex flex-col gap-0.5">
    <h3 class="px-2 pb-1 stat-label">Truemains</h3>
    <LeaderboardTruemainSearch
      :champion-id="championId"
      placeholder="Search a truemain…"
      class="mb-1 px-1"
      @player="emit('select', $event)"
    />

    <template v-if="rows === null && !failed">
      <div v-for="index in 3" :key="index" class="flex items-center gap-2 px-2 py-1.5">
        <USkeleton class="size-8 rounded-lg" />
        <USkeleton class="h-4 w-32" />
        <USkeleton class="ml-auto h-4 w-12" />
      </div>
    </template>
    <p v-else-if="failed" class="px-2 text-xs text-muted">The truemains could not be loaded</p>
    <UEmpty v-else-if="rows && rows.length === 0" size="sm" icon="i-lucide-trophy" description="No tracked truemains on this champion yet." />

    <template v-else>
      <div
        v-for="row in rows ?? []"
        :key="`${row.region}-${row.identity.gameName}-${row.identity.tagLine}`"
        class="relative flex items-center gap-2 rounded-lg px-2 py-1.5 transition-colors"
        :class="selectedNameTag === nameTagOf(row) ? 'bg-accented' : 'hover:bg-elevated'"
      >
        <span v-if="selectedNameTag === nameTagOf(row)" class="absolute inset-y-2 left-0 w-0.5 rounded-full bg-primary" />
        <button
          type="button"
          :aria-label="`Build of ${row.identity.gameName}${row.identity.tagLine ? ` #${row.identity.tagLine}` : ''}`"
          :aria-pressed="selectedNameTag === nameTagOf(row)"
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
            <LeaderboardRegionFlag :region="row.region" :width="14" />
            <span v-if="row.identity.tagLine" class="truncate text-[11px] text-muted">#{{ row.identity.tagLine }}</span>
          </div>
        </div>
        <div class="relative z-10 flex shrink-0 items-center gap-0.5">
          <GameTooltipPerkIcon :perk="perk(onChampion(row)?.primaryKeystoneId)" :width="22" :height="22" class="size-[22px] shrink-0 rounded-full" />
          <GameTooltipItemIcon :item="item(onChampion(row)?.firstItemId)" :width="22" :height="22" class="size-[22px] shrink-0 rounded" />
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
                :wins="row.stats.wins"
                :losses="row.stats.losses"
                :win-rate="row.stats.winRate"
                :size="32"
              />
            </GameTooltipSurface>
          </template>
        </UTooltip>
      </div>
    </template>
  </div>
</template>
