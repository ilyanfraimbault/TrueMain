<script setup lang="ts">
import type {
  ChampionAdvancedResponse,
  ChampionBuildTreeResponse,
  ChampionCoreResponse,
  ChampionSummaryResponse
} from '~/types/champions'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const {
  advanced,
  buildTree,
  champion,
  championState,
  championStatic,
  core,
  displayPosition,
  isPageLoading,
  isStaticPending,
  patchOptions,
  positionOptions,
  selectedPatch,
  setPatchFilter,
  setPositionFilter,
  summary
} = useChampionPageStore(championId)

const loadingSummary: ChampionSummaryResponse = {
  championId: 0,
  games: 0,
  winRate: 0,
  trueMainCount: 0,
  position: '',
  latestPatchVersion: '',
  lastUpdatedAtUtc: ''
}

const loadingCore: ChampionCoreResponse = {
  sampleSize: 0,
  starterItems: null,
  boots: null,
  buildPath: null,
  summonerSpells: null,
  skillOrder: null,
  runePage: null
}

const loadingAdvanced: ChampionAdvancedResponse = {
  starterItemOptions: [],
  summonerSpellOptions: [],
  skillOrderOptions: [],
  runePageOptions: []
}

const loadingBuildTree: ChampionBuildTreeResponse = {
  championId: 0,
  patch: null,
  position: null,
  riotAccountId: null,
  platformId: null,
  totalGames: 0,
  boots: null,
  runePage: null,
  build: []
}

const renderedSummary = computed(() => summary.value ?? loadingSummary)
const renderedCore = computed(() => core.value ?? loadingCore)
const renderedAdvanced = computed(() => advanced.value ?? loadingAdvanced)
const renderedBuildTree = computed(() => buildTree.value ?? loadingBuildTree)
const isSectionLoading = computed(() => isPageLoading.value || isStaticPending.value)

const pageTitle = computed(() => championStatic.value.championName || 'TrueMain')
const pageDescription = computed(() => `Vue champion et build tree pour le champion ${championId.value}.`)

useSeoMeta({
  title: () => pageTitle.value,
  description: () => pageDescription.value
})
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-8 p-4 md:p-6">
    <section v-if="championState.error.value && !isPageLoading">
      <UAlert
        color="error"
        variant="soft"
        title="Impossible de charger /champions/{id}"
        :description="championState.error.value.message"
      />
    </section>

    <section
      v-else
      class="space-y-8"
    >
      <ChampionsChampionHeader
        :champion-static="championStatic"
        :summary="renderedSummary"
        :patch-options="patchOptions"
        :position-options="positionOptions"
        :selected-patch="selectedPatch"
        :display-position="displayPosition"
        :is-static-pending="isSectionLoading"
        @update:patch="setPatchFilter"
        @update:position="setPositionFilter"
      />

      <ChampionsChampionCoreSection
        :core="renderedCore"
        :champion-static="championStatic"
        :is-static-pending="isSectionLoading"
      />

      <ChampionsChampionAdvancedSection
        :advanced="renderedAdvanced"
        :champion-static="championStatic"
        :is-static-pending="isSectionLoading"
      />

      <ChampionsChampionBuildTreeSection
        :build-tree="renderedBuildTree"
        :champion-static="championStatic"
        :fallback-boots="renderedCore.boots"
        :is-static-pending="isSectionLoading"
      />
    </section>
  </main>
</template>
