<script setup lang="ts">
import type { ChampionResponse } from '~~/shared/types/champions'
import { indexItemContext } from '~~/shared/utils/item-context'
import { isLoadingStatus } from '~/utils/async-data'
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
  /**
   * Why the reader is looking at the standard build instead of a matchup one.
   * Rendered as a warning icon beside the title with this as its tooltip —
   * never as prose, so the caveat never pushes the build down the page.
   */
  notice?: string | null
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

const cardTitle = computed(() =>
  props.championName ? `${props.championName}'s standard build` : 'Standard build')

const build = computed(() => champion.value?.builds[0] ?? null)

const assetsPatch = computed(() => champion.value?.patch ?? null)
const { runeTree, itemsMap } = useBuildAssets(assetsPatch)
const { data: summonersMap, status: summonersStatus } = useStaticSummonerSpells(assetsPatch)
const { data: championStatic } = useChampionStatic(
  () => props.championId,
  () => assetsPatch.value,
)

// Same situational context as the recommendation panel (#1451). This fallback renders the
// champion's standard build, so the cards describe exactly the scope it came from — which
// is also why they are safe here: the fallback is shown precisely when the matchup has no
// games, and a verdict measured on the champion at large is then the honest answer.
const { data: itemContext } = useChampionItemContext(
  () => props.championId,
  () => props.position,
  () => assetsPatch.value,
)
const itemContextIndex = computed(() => indexItemContext(itemContext.value?.items))
</script>

<template>
  <!-- `#title` opts the card out of its automatic `aria-labelledby`, so the
       region is named here instead — otherwise the whole section goes unnamed
       for anyone navigating by landmark. -->
  <SectionCard :aria-label="cardTitle">
    <template #title>
      <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
        <h3 class="text-sm font-medium text-default">
          {{ cardTitle }}
        </h3>
        <!-- Icon only, message in the tooltip — the same qualifier pattern the
             recommendation card uses for a thin sample. -->
        <UTooltip
          v-if="notice"
          :text="notice"
          :delay-duration="150"
        >
          <UIcon
            name="i-lucide-triangle-alert"
            class="size-4 text-warning"
          />
        </UTooltip>
      </div>
    </template>
    <ChampionBuildCoreSkeleton v-if="status === 'pending'" />
    <UAlert
      v-else-if="error"
      color="error"
      variant="soft"
      title="Standard build unavailable"
      :description="describeFetchError(error)"
    />
    <!-- The core block and the build tree, not the champion page's whole build
         panel. This page is read to answer "what do I build into this
         opponent": the champion's global variations, alternative rune pages and
         power spikes answer a different question, and reading them here as if
         they were the matchup's is worse than not showing them. -->
    <div
      v-else-if="build && championStatic"
      class="space-y-6"
    >
      <ChampionBuildPanelCore
        :item-context="itemContextIndex"
        :summoner-spells="build.core.summonerSpells"
        :starter-items="build.core.starterItems"
        :skill-order="build.core.skillOrder"
        :boots="build.core.boots"
        :item-path="build.core.itemPath"
        :rune-page="build.core.runePage"
        :champion-static="championStatic"
        :items-map="itemsMap"
        :summoners-map="summonersMap ?? {}"
        :summoners-pending="isLoadingStatus(summonersStatus)"
        :rune-tree="runeTree"
      />
      <ChampionBuildPanelBuildTree
        :item-context="itemContextIndex"
        v-if="build.buildTree.length > 0"
        :tree="build.buildTree"
        :first-item-id="build.firstItemId"
        :item-path="build.core.itemPath?.itemIds ?? []"
        :items-map="itemsMap"
      />
    </div>
    <div
      v-else
      class="surface rounded-lg px-6 py-10 text-center"
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
