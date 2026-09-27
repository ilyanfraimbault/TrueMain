<script setup lang="ts">
import type { SelectMenuItem } from '@nuxt/ui'
import type { Lane } from '~/types/draft'
import type { BuildSubject } from '~/composables/useDraftBuild'
import { LANES } from '~/types/draft'

/**
 * The site's matchup page: one champion against another on a lane, answered by
 * the same endpoint the draft uses (a draft with a single enemy on our lane),
 * so the build and the lane verdict here are the ones the draft would show.
 */
const route = useRoute()
const router = useRouter()
const { champions, portraitOf, nameOf } = useChampionStatics()
const { entries } = useTierList()

const queryId = (key: string) => {
  const value = Number(route.query[key])
  return Number.isInteger(value) && value > 0 ? value : null
}

function setQuery(key: string, value: string | number | null) {
  void router.replace({ query: { ...route.query, [key]: value ?? undefined } })
}

const championId = computed<number | null>({ get: () => queryId('champion'), set: value => setQuery('champion', value) })
const opponentId = computed<number | null>({ get: () => queryId('vs'), set: value => setQuery('vs', value) })

/** The lane asked for, else the champion's most played one. */
const lane = computed<Lane | null>({
  get: () => {
    const asked = String(route.query.lane ?? '').toUpperCase() as Lane
    if (LANES.includes(asked)) return asked
    const main = entries.value.filter(entry => entry.championId === championId.value).sort((a, b) => b.games - a.games)[0]
    return (main?.position as Lane | undefined) ?? null
  },
  set: value => setQuery('lane', value),
})

const items = computed<SelectMenuItem[]>(() => [...champions.value.values()]
  .sort((a, b) => a.name.localeCompare(b.name))
  .map(champion => ({ label: champion.name, value: champion.id, avatar: { src: portraitOf(champion.id) ?? undefined, alt: champion.name } })))

const subject = computed<BuildSubject | null>(() => {
  if (!championId.value || !lane.value) return null
  return {
    championId: championId.value,
    request: {
      position: lane.value,
      allies: [],
      enemies: opponentId.value ? [{ championId: opponentId.value, position: lane.value }] : [],
    },
  }
})

const { build, pending, error } = useDraftBuild(subject)
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-4">
    <div class="flex items-center gap-3 px-2">
      <UIcon name="i-lucide-wand-sparkles" class="size-6 text-primary" />
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">Matchup</h1>

      <div class="ml-auto flex items-center gap-2">
        <USelectMenu v-model="championId" :items="items" value-key="value" placeholder="Your champion" size="sm" class="w-44" :avatar="championId ? { src: portraitOf(championId) ?? undefined } : undefined" />
        <span class="text-xs font-semibold uppercase tracking-widest text-dimmed">vs</span>
        <USelectMenu v-model="opponentId" :items="items" value-key="value" placeholder="Opponent" size="sm" class="w-44" :avatar="opponentId ? { src: portraitOf(opponentId) ?? undefined } : undefined" />
        <LaneTabs v-model="lane" />
      </div>
    </div>

    <div class="grid min-h-0 flex-1 grid-cols-[9rem_minmax(0,1fr)] gap-4">
      <DraftLaneDuel
        :champion-id="championId"
        :opponent-id="opponentId"
        :position="lane"
        :lane="build && build.championId === championId ? build.lane : null"
        :pending="pending"
        class="surface self-start rounded-xl px-2 py-5"
      />

      <BuildView
        v-if="subject"
        :champion-id="subject.championId"
        :position="subject.request.position"
        :draft="{ build, pending, error, label: opponentId ? `vs ${nameOf(opponentId)}` : 'Lane build' }"
      >
        <template #header>
          <p class="text-sm font-semibold text-highlighted">{{ championId ? nameOf(championId) : '' }}</p>
        </template>
      </BuildView>

      <div v-else class="surface flex flex-col items-center justify-center gap-2 rounded-xl text-center">
        <UIcon name="i-lucide-swords" class="size-6 text-dimmed" />
        <p class="text-sm text-muted">Pick a champion and the one it faces</p>
      </div>
    </div>
  </div>
</template>
