<script setup lang="ts">
import type { ChampionResponse } from '~~/shared/types/champions'
import { describeFetchError, fetchErrorStatus } from '~/utils/errors'

/**
 * Baseline build fallback of the composition builder: when the requested
 * matchup has never been recorded, this renders the same top build the
 * champion page shows (its default tab), fetched from the regular champion
 * endpoint. A 404 means we hold no aggregate at all for the champion — the
 * empty state says so instead of looking like a failure.
 */
const props = defineProps<{
  championId: number
  position: string
  championName: string | null
}>()

const { data: champion, status, error } = useLazyAsyncData<ChampionResponse | null>(
  () => `builder-fallback-${props.championId}-${props.position}`,
  async () => {
    try {
      return await $fetch<ChampionResponse>(`/api/champions/${props.championId}`, {
        query: { position: props.position },
      })
    }
    catch (err) {
      // 404 = no aggregate for this champion/position — an empty state,
      // not a failure.
      if (fetchErrorStatus(err) === 404) {
        return null
      }
      throw err
    }
  },
  { watch: [() => props.championId, () => props.position] },
)

const build = computed(() => champion.value?.builds[0] ?? null)

const assetsPatch = computed(() => champion.value?.patch ?? null)
const { runeTree, itemsMap } = useBuildAssets(assetsPatch)
const { data: summonersMap } = useStaticSummonerSpells(assetsPatch)
const { data: championStatic } = useChampionStatic(
  () => props.championId,
  () => assetsPatch.value,
)
</script>

<template>
  <SectionCard
    :title="championName ? `${championName}'s standard build` : 'Standard build'"
    subtitle="The champion page's top build, not specific to this draft."
  >
    <ChampionBuildTabsSkeleton v-if="status === 'pending'" />
    <UAlert
      v-else-if="error"
      color="error"
      variant="soft"
      title="Standard build unavailable"
      :description="describeFetchError(error)"
    />
    <ChampionBuildPanel
      v-else-if="build && championStatic"
      :build="build"
      :champion-static="championStatic"
      :items-map="itemsMap"
      :summoners-map="summonersMap ?? {}"
      :rune-tree="runeTree"
    />
    <div
      v-else
      class="glass rounded-lg px-6 py-10 text-center"
    >
      <p class="font-medium">
        No build data yet
      </p>
      <p class="mt-1 text-sm text-muted">
        We hold no recorded games for this champion at this position.
      </p>
    </div>
  </SectionCard>
</template>
