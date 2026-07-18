<script setup lang="ts">
import type { CompositionBuildResponse } from '~~/shared/types/composition'
import { formatPercentage } from '~~/shared/utils/ddragon'

/**
 * Full composition recommendation (#563): confidence strip + the same core
 * panels the champion page renders (spells, starter, skill order, boots, core
 * path, runes), the pruned build tree, plus the situational-items row specific
 * to this feature. Self-contained: mounts only when a recommendation exists,
 * so the static asset fetches (items, rune tree, summoners, champion spells)
 * fire lazily with the right patch instead of on page load.
 */
const props = defineProps<{
  recommendation: CompositionBuildResponse
  championName: string | null
  championIconUrl: string | null
}>()

/**
 * Below this many sampled games the aggregation is thin enough that single
 * games swing every dimension — the panel stays visible but carries an
 * explicit warning instead of fabricated certainty.
 */
const LOW_SAMPLE_FLOOR = 20

/** Mean similarity under this reads as "barely draft-specific". */
const LOW_SIMILARITY_FLOOR = 0.2

const build = computed(() => props.recommendation.build)
const confidence = computed(() => props.recommendation.confidence)

const assetsPatch = computed(() => props.recommendation.patch ?? null)
const { runeTree, itemsMap } = useBuildAssets(assetsPatch)
const { data: summonersMap } = useStaticSummonerSpells(assetsPatch)
const { data: championStatic } = useChampionStatic(
  () => props.recommendation.championId,
  () => assetsPatch.value,
)

const winRate = computed(() =>
  build.value.gamesConsidered > 0 ? build.value.wins / build.value.gamesConsidered : null)

const draftRequested = computed(() => confidence.value.maxPossibleScore > 0)
const lowSample = computed(() => confidence.value.sampleSize < LOW_SAMPLE_FLOOR)
const lowSimilarity = computed(() =>
  draftRequested.value && confidence.value.meanSimilarity < LOW_SIMILARITY_FLOOR)

const lowDataMessage = computed(() => {
  if (lowSample.value) {
    return `Only ${confidence.value.sampleSize} similar game${confidence.value.sampleSize === 1 ? '' : 's'} `
      + 'back this recommendation — treat it as a hint, not a consensus.'
  }
  if (lowSimilarity.value) {
    return 'Few recorded games actually resemble this draft — the build below leans '
      + 'on the champion\'s general games more than on your specific composition.'
  }
  return null
})

const stats = computed(() => [
  {
    label: 'Games sampled',
    value: String(build.value.gamesConsidered),
    hint: 'Most similar games the build is computed from',
  },
  {
    label: 'Win rate',
    value: winRate.value === null ? '—' : formatPercentage(winRate.value),
    hint: 'Across the sampled games',
  },
  {
    label: 'Draft similarity',
    value: draftRequested.value ? formatPercentage(confidence.value.meanSimilarity) : '—',
    hint: 'How closely the sample reproduces your draft',
  },
  {
    label: 'Pool scanned',
    value: String(confidence.value.candidatePoolSize),
    hint: 'Recent games considered for the sample',
  },
])
</script>

<template>
  <SectionCard>
    <template #title>
      <div class="flex items-center gap-2.5">
        <SkeletonImage
          v-if="championIconUrl"
          :src="championIconUrl"
          :alt="championName ?? ''"
          :width="28"
          :height="28"
          class="size-7 rounded-lg ring-1 ring-primary/40"
        />
        <h2 class="text-sm font-medium text-default">
          {{ championName ? `Recommended build for ${championName}` : 'Recommended build' }}
        </h2>
      </div>
    </template>
    <div class="space-y-6">
      <!-- Confidence strip — always first: the numbers qualify everything below. -->
      <dl class="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <div
          v-for="stat in stats"
          :key="stat.label"
          :title="stat.hint"
        >
          <dt class="text-sm text-muted">
            {{ stat.label }}
          </dt>
          <dd class="text-lg font-semibold">
            {{ stat.value }}
          </dd>
        </div>
      </dl>

      <UAlert
        v-if="lowDataMessage"
        color="warning"
        variant="soft"
        icon="i-lucide-triangle-alert"
        title="Thin data for this draft"
        :description="lowDataMessage"
      />

      <!-- Same layout skeleton as the champion page's build panel: flexible
           left column, fixed 240px runes column at lg+ (see BuildPanel.vue for
           the sizing rationale). -->
      <div class="grid gap-x-6 gap-y-5 lg:grid-cols-[minmax(0,1fr)_240px]">
        <div class="flex flex-col gap-5 sm:flex-row sm:items-start">
          <div class="flex flex-col gap-5">
            <ChampionCoreSpells
              :summoners="build.summonerSpells"
              :summoners-map="summonersMap ?? {}"
            />
            <ChampionCoreStarterItems
              :starter="build.starterItems"
              :items-map="itemsMap"
            />
          </div>
          <div class="flex flex-1 flex-col gap-5">
            <div class="flex flex-wrap items-start justify-around gap-6">
              <ChampionCoreSkillOrder
                v-if="championStatic"
                :skill-order="build.skillOrder"
                :champion-static="championStatic"
              />
              <ChampionCoreBoots
                :boots="build.boots"
                :items-map="itemsMap"
              />
            </div>
            <div class="flex justify-center">
              <ChampionCoreBuildPath
                :path="build.corePath"
                :items-map="itemsMap"
              />
            </div>
          </div>
        </div>
        <div class="w-full shrink-0 overflow-hidden lg:w-[240px]">
          <ChampionCoreRunes
            v-if="build.runePage && runeTree"
            :page="build.runePage"
            :tree="runeTree"
          />
          <p
            v-else
            class="text-sm text-muted"
          >
            No rune data in the sampled games.
          </p>
        </div>
      </div>

      <!-- Build tree — same component as the champion page, recomputed from the
           sampled games only. -->
      <ChampionBuildPanelBuildTree
        v-if="build.buildTree.length > 0"
        :tree="build.buildTree"
        :first-item-id="build.firstItemId"
        :item-path="build.corePath?.itemIds ?? []"
        :items-map="itemsMap"
      />

      <!-- Situational items — specific to the composition recommender. -->
      <div v-if="build.situationalItems.length > 0">
        <h3 class="text-sm font-medium text-muted">
          Situational items
        </h3>
        <ul class="mt-2 flex flex-wrap gap-4">
          <li
            v-for="slot in build.situationalItems"
            :key="slot.itemIds[0]"
            class="glass-hover flex items-center gap-3 rounded-lg px-3 py-2"
          >
            <GameTooltipItemIcon
              :item="itemsMap[slot.itemIds[0]!] ?? null"
              :width="32"
              :height="32"
              class="size-8"
            />
            <div class="text-xs leading-tight">
              <p class="font-medium">
                {{ formatPercentage(slot.winRate) }} WR
              </p>
              <p class="text-muted">
                {{ formatPercentage(slot.pickRate) }} of games
              </p>
            </div>
          </li>
        </ul>
      </div>
    </div>
  </SectionCard>
</template>
