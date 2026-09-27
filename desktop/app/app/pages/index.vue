<script setup lang="ts">
import type { LeaderboardResponse, TruemainRow } from '~/types/truemains'

/**
 * Home: the player first — who the client is logged in as and where it is —
 * then the way into champion select, the patch at a glance and the best true
 * mains. The player's own numbers need the app to know them (#1682, #1683);
 * until then nothing here is about them that the client did not say.
 */
const { state, screen } = useLcuState()
const { profileIconOf } = useChampionStatics()

const name = computed(() => state.value.riotId?.split('#')[0] ?? null)
const tag = computed(() => state.value.riotId?.split('#')[1] ?? null)
const icon = computed(() => (state.value.profileIconId !== null ? profileIconOf(state.value.profileIconId) : null))

const mains = ref<TruemainRow[]>([])
const mainsFailed = ref(false)
onMounted(async () => {
  try {
    mains.value = (await apiGet<LeaderboardResponse>('/truemains', { pageSize: 5 })).rows
  }
  catch {
    mainsFailed.value = true
  }
})
</script>

<template>
  <div class="flex h-full flex-col gap-4 overflow-y-auto p-6">
    <div class="flex items-center gap-3">
      <UIcon name="i-lucide-house" class="size-6 text-primary" />
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">Dashboard</h1>
    </div>

    <div class="grid grid-cols-[minmax(0,1fr)_17rem] gap-4">
      <section class="surface relative flex min-h-44 items-end overflow-hidden rounded-xl p-5">
        <ChampionArt :alias="backdropAlias()" fade="x" position="70% 22%" />
        <div class="relative flex items-end gap-4">
          <div class="relative shrink-0">
            <div class="size-20 overflow-hidden rounded-2xl bg-ink-900 ring-2 ring-primary/60">
              <img v-if="icon && state.connected" :src="icon" alt="" class="size-full object-cover">
              <div v-else class="flex size-full items-center justify-center">
                <UIcon name="i-lucide-user-round" class="size-8 text-dimmed" />
              </div>
            </div>
            <span
              v-if="state.connected && state.summonerLevel !== null"
              class="absolute -bottom-2 left-1/2 -translate-x-1/2 rounded-full border border-default bg-ink-950 px-2 py-0.5 text-xs font-semibold tabular-nums text-highlighted"
            >{{ state.summonerLevel }}</span>
          </div>
          <div class="min-w-0 pb-1">
            <h2 class="truncate text-3xl font-semibold tracking-tight text-highlighted">
              <template v-if="state.connected">{{ name ?? 'Logging in…' }}<span v-if="tag" class="ml-1 text-xl font-medium text-dimmed">#{{ tag }}</span></template>
              <template v-else>Waiting for the client</template>
            </h2>
            <p class="mt-1 text-sm text-muted">
              {{ state.connected ? 'Connected to the League client' : 'Start League of Legends and the app follows you into champion select.' }}
            </p>
          </div>
        </div>
      </section>

      <section class="surface flex flex-col justify-between gap-3 rounded-xl p-4">
        <div class="flex items-center gap-2">
          <UIcon name="i-lucide-sparkles" class="size-5 text-primary" />
          <h2 class="text-sm font-semibold text-highlighted">Champ select</h2>
          <UBadge v-if="screen === 'draft'" color="primary" size="sm" class="ml-auto">Live</UBadge>
        </div>
        <p class="text-xs text-muted">
          {{ screen === 'draft'
            ? 'Your champion select is on: picks, lanes and builds as it stands.'
            : 'Opens on its own when your next champion select starts. Rehearse one meanwhile.' }}
        </p>
        <div class="flex flex-col gap-2">
          <UButton v-if="screen === 'draft'" to="/draft" label="Open the draft" icon="i-lucide-arrow-right" trailing block />
          <UButton to="/simulator" label="Draft simulator" icon="i-lucide-flask-conical" color="neutral" variant="subtle" block />
        </div>
      </section>
    </div>

    <section class="flex flex-col gap-3">
      <div class="flex items-center justify-between">
        <h2 class="text-sm font-semibold text-highlighted">Best by lane</h2>
        <UButton to="/tierlist" label="Tier list" trailing-icon="i-lucide-chevron-right" color="neutral" variant="link" size="xs" />
      </div>
      <DashboardMeta />
    </section>

    <section class="flex flex-col gap-3">
      <div class="flex items-center justify-between">
        <h2 class="text-sm font-semibold text-highlighted">Top true mains</h2>
        <UButton to="/truemains" label="Leaderboard" trailing-icon="i-lucide-chevron-right" color="neutral" variant="link" size="xs" />
      </div>
      <div class="surface overflow-hidden rounded-xl">
        <TruemainRow v-for="row in mains" :key="`${row.identity.platformId}-${row.identity.gameName}-${row.identity.tagLine}`" :row="row" />
        <p v-if="mainsFailed" class="p-4 text-center text-sm text-muted">The leaderboard could not be loaded</p>
        <div v-else-if="!mains.length" class="flex flex-col gap-2 p-3">
          <USkeleton v-for="index in 3" :key="index" class="h-10 w-full" />
        </div>
      </div>
    </section>
  </div>
</template>
