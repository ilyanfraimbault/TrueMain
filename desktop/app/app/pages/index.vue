<script setup lang="ts">
import type { GameflowPhase } from '~/types/lcu'
import type { QueueFilter } from '~/utils/player-form'
import { METRICS, QUEUE_FILTERS, championLines, counted, gamesIn, laneLines, readMetric } from '~/utils/player-form'
import { toProfileRanked } from '~/utils/match-summary'

/**
 * Home: the player, read from their own client — their profile as the site
 * lays it out, the ranked card with its LP curve and a match history whose rows
 * open the site's detail panel, under a banner of the skin they chose and their
 * recent form, with their champions and roles beside it. The banner and the
 * cards read the latest page of games; the history pages further back. It needs no account on
 * TrueMain: the client knows every player, TrueMain only the true mains
 * (#1682). With no client there is nothing personal to show, so the patch's
 * best picks stand in.
 */
const { state } = useLcuState()
const { record, games: loadedGames, rankHistory, status, refresh, hasOlder, loadingOlder, loadOlder } = usePlayerRecord()
const championsById = useChampionsById()
const { items, summoners, runeTree } = useStaticData()

const filter = ref<QueueFilter>('all')
const latest = computed(() => record.value?.games ?? [])
const games = computed(() => gamesIn(latest.value, filter.value))
const sample = computed(() => counted(games.value).length)
const readings = computed(() => METRICS.map(metric => readMetric(metric, games.value)))
const pending = computed(() => !record.value && (status.value === 'pending' || status.value === 'idle'))

const champions = computed(() => [...championsById.value.values()])
const ranked = computed(() => toProfileRanked(record.value?.ranked.find(queue => queue.queueType === 'RANKED_SOLO_5x5') ?? null))
const nameTag = computed(() => state.value.riotId?.replace('#', '-') ?? '')
const historyGames = computed(() => gamesIn(loadedGames.value, filter.value))
const filterLabel = computed(() => QUEUE_FILTERS.find(option => option.value === filter.value)?.label ?? 'All')

onMounted(() => refresh())
watch(() => state.value.riotId, () => refresh())

// A game that ends reaches the history once the client has its stats: read
// again at the end-of-game screen, and once more on the way back to the lobby.
const POST_GAME: GameflowPhase[] = ['WaitingForStats', 'PreEndOfGame', 'EndOfGame']
watch(() => state.value.phase, (next, previous) => {
  if (next === 'EndOfGame' || (POST_GAME.includes(previous) && !POST_GAME.includes(next))) void refresh(true)
})
</script>

<template>
  <div class="flex h-full flex-col gap-4 overflow-y-auto p-5">
    <div class="flex items-center gap-3">
      <UIcon name="i-lucide-house" class="size-6 text-primary" />
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">Dashboard</h1>

      <div v-if="state.connected && state.riotId" class="ml-auto flex items-center gap-2">
        <div class="flex rounded-lg bg-elevated p-0.5 ring-1 ring-default">
          <button
            v-for="option in QUEUE_FILTERS"
            :key="option.value"
            type="button"
            class="rounded-md px-3 py-1 text-xs font-medium transition-colors"
            :class="filter === option.value ? 'bg-accented text-highlighted' : 'text-muted hover:text-highlighted'"
            @click="filter = option.value"
          >
            {{ option.label }}
          </button>
        </div>
        <UButton
          icon="i-lucide-refresh-cw"
          color="neutral"
          variant="ghost"
          size="sm"
          :loading="status === 'pending'"
          aria-label="Read your games again"
          @click="refresh(true)"
        />
      </div>
    </div>

    <template v-if="state.connected && state.riotId">
      <UAlert
        v-if="status === 'error' && !record"
        color="neutral"
        variant="subtle"
        icon="i-lucide-circle-alert"
        title="Your client did not share your match history"
        description="It sometimes needs a moment after logging in."
        :actions="[{ label: 'Try again', color: 'neutral', variant: 'outline', onClick: () => refresh(true) }]"
      />

      <div class="grid grid-cols-[minmax(0,1fr)_18rem] items-start gap-4">
        <div class="flex min-w-0 flex-col gap-4">
          <DashboardHero :record="record" :readings="readings" :sample="sample" :pending="pending" />

          <DashboardMatchHistory
            :games="historyGames"
            :all-games="loadedGames"
            :rank-history="rankHistory"
            :queue="filterLabel"
            :pending="pending"
            :has-older="hasOlder"
            :loading-older="loadingOlder"
            :champions="champions"
            :items="items"
            :summoner-spells="summoners"
            :rune-tree="runeTree"
            :name-tag="nameTag"
            @load-older="loadOlder"
          />
        </div>

        <aside class="flex min-w-0 flex-col gap-4">
          <ProfileRankedCardSkeleton v-if="pending" />
          <ProfileRankedCard v-else :ranked="ranked" :history="rankHistory" />
          <template v-if="pending">
            <USkeleton class="h-56 rounded-lg" />
            <USkeleton class="h-40 rounded-lg" />
          </template>
          <template v-else>
            <DashboardChampionsCard :champions="championLines(games)" />
            <DashboardRolesCard :lanes="laneLines(games)" />
          </template>
        </aside>
      </div>
    </template>

    <template v-else>
      <section class="relative flex min-h-48 items-end overflow-hidden rounded-lg border border-default p-6">
        <ChampionArt :alias="backdropAlias()" fade="x" position="70% 22%" />
        <div class="relative flex items-center gap-4">
          <div class="flex size-16 items-center justify-center rounded-lg bg-ink-900 ring-1 ring-default">
            <UIcon :name="state.connected ? 'i-lucide-loader-circle' : 'i-lucide-user-round'" class="size-7 text-dimmed" />
          </div>
          <div>
            <h2 class="text-3xl font-semibold tracking-tight text-highlighted">
              {{ state.connected ? 'Logging in…' : 'Waiting for the client' }}
            </h2>
            <p class="mt-1 max-w-md text-sm text-muted">
              Start League of Legends: your profile opens here, and the app follows you into champion select.
            </p>
          </div>
        </div>
      </section>

      <section class="flex flex-col gap-3">
        <div class="flex items-center justify-between">
          <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">Best by lane</h2>
          <UButton to="/tierlist" label="Tier list" trailing-icon="i-lucide-chevron-right" color="neutral" variant="link" size="xs" />
        </div>
        <DashboardMeta />
      </section>
    </template>
  </div>
</template>
