<script setup lang="ts">
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const { filters, setFilter } = useChampionFilters()

const {
  data: champion,
  error: championError,
  status: championStatus,
} = await useChampion(championId, filters)

const summary = computed(() => champion.value?.summary ?? null)
const core = computed(() => champion.value?.core ?? null)
const advanced = computed(() => champion.value?.advanced ?? null)
const buildTree = computed(() => champion.value?.buildTree ?? null)

const activePatch = computed(() =>
  buildTree.value?.patch || summary.value?.latestPatchVersion || filters.value.patch || null)

const { data: staticData } = await useChampionStatic(championId, activePatch)
const { data: versions } = useDDragonVersions()
const { data: runeTree } = await useFetch('/api/static/rune-tree', { key: 'rune-tree' })

useSeoMeta({
  title: () => staticData.value?.championName ?? 'TrueMain',
  description: () => `Champion ${championId.value} build, runes and skill order.`,
})

const patchOptions = computed(() => {
  const seen = new Set<string>(
    (versions.value ?? [])
      .map(p => p.split('.').slice(0, 2).join('.'))
      .filter(Boolean)
      .slice(0, 12),
  )
  if (summary.value?.latestPatchVersion) seen.add(summary.value.latestPatchVersion)
  if (filters.value.patch) seen.add(filters.value.patch)
  return [...seen]
    .map(p => ({ label: p, value: p }))
    .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
})

const selectedPatch = computed(() => filters.value.patch || summary.value?.latestPatchVersion || '')
const selectedPosition = computed<ChampionPosition | ''>(() => {
  const value = filters.value.position || summary.value?.position || ''
  return POSITION_OPTIONS.some(o => o.value === value) ? value as ChampionPosition : ''
})

const topRunePages = computed(() => {
  const options = advanced.value?.runePageOptions ?? []
  return [...options].sort((a, b) => b.playRate - a.playRate).slice(0, 3)
})

const isLoading = computed(() => championStatus.value === 'pending' && !champion.value)
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
    <p
      v-if="isLoading"
      class="text-sm"
    >
      Loading…
    </p>

    <UAlert
      v-else-if="championError"
      color="error"
      variant="soft"
      title="Failed to load champion"
      :description="championError.message"
    />

    <template v-else-if="summary && staticData">
      <header class="flex flex-wrap items-center gap-4">
        <ChampionHeader
          :summary="summary"
          :champion-static="staticData"
          :champion-id="championId"
        />
        <ChampionFilters
          :selected-patch="selectedPatch"
          :selected-position="selectedPosition"
          :patch-options="patchOptions"
          :position-options="POSITION_OPTIONS"
          @update:patch="value => setFilter(value, null)"
          @update:position="value => setFilter(null, value)"
        />
      </header>

      <section class="flex flex-wrap gap-8">
        <ChampionSummonerSpells
          :summoners="core?.summonerSpells ?? null"
          :champion-static="staticData"
        />
        <ChampionSkillOrder
          :skill-order="core?.skillOrder ?? null"
          :champion-static="staticData"
        />
      </section>

      <section class="space-y-4">
        <h2 class="text-base font-semibold">
          Runes
        </h2>
        <ChampionRunesFull
          v-if="topRunePages[0] && runeTree"
          :page="topRunePages[0]"
          :tree="runeTree"
        />
      </section>

      <section class="space-y-3">
        <h3 class="text-sm font-medium text-muted">
          Alternative pages
        </h3>
        <ChampionRunes
          :pages="topRunePages"
          :champion-static="staticData"
        />
      </section>

      <section class="space-y-6">
        <h2 class="text-base font-semibold">
          Build
        </h2>
        <ChampionBuild
          :core="core"
          :champion-static="staticData"
        />
        <ChampionBuildPaths
          :build-tree="buildTree"
          :champion-static="staticData"
        />
      </section>
    </template>
  </main>
</template>
