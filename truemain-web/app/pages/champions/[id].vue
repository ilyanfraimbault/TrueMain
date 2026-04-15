<script setup lang="ts">
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

const pageTitle = computed(() => championStatic.value.championName || 'TrueMain')
const pageDescription = computed(() => `Vue champion et build tree pour le champion ${championId.value}.`)

useSeoMeta({
  title: () => pageTitle.value,
  description: () => pageDescription.value
})
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-8 p-4 md:p-6">
    <ChampionsChampionPageSkeleton v-if="isPageLoading" />

    <section v-else-if="championState.error.value">
      <UAlert
        color="error"
        variant="soft"
        title="Impossible de charger /champions/{id}"
        :description="championState.error.value.message"
      />
    </section>

    <section
      v-else-if="champion && summary && core && advanced && buildTree"
      class="space-y-8"
    >
      <ChampionsChampionHeader
        :champion-static="championStatic"
        :summary="summary"
        :patch-options="patchOptions"
        :position-options="positionOptions"
        :selected-patch="selectedPatch"
        :display-position="displayPosition"
        @update:patch="setPatchFilter"
        @update:position="setPositionFilter"
      />

      <ChampionsChampionCoreSection
        :core="core"
        :champion-static="championStatic"
        :is-static-pending="isStaticPending"
      />

      <ChampionsChampionAdvancedSection
        :advanced="advanced"
        :champion-static="championStatic"
      />

      <ChampionsChampionBuildTreeSection
        :build-tree="buildTree"
        :champion-static="championStatic"
        :boots="core.boots"
        :is-static-pending="isStaticPending"
      />
    </section>
  </main>
</template>
