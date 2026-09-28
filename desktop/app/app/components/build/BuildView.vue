<script setup lang="ts">
import type { BuildOption } from '~/types/build'
import type { DraftBuild } from '~/composables/useDraftBuild'

/**
 * A champion's build, laid out the way the reference client does after a pick
 * — the builds to choose from and the champion's true mains down the left, the
 * build itself on the right — the core (`BuildCore`, the site's icons and
 * rune block laid out for the pane) over the site's build tree.
 *
 * With a draft, its own build (computed against the draft as it stands) is the
 * first row and the default; the champion's lane builds follow, so the player
 * can compare the draft's answer with what the lane usually runs.
 */
const props = defineProps<{
  championId: number
  position: string
  /** The build computed for the draft, when there is one. */
  draft?: { build: DraftBuild | null, pending: boolean, error: string | null, label?: string } | null
}>()

const { response, pending: lanePending } = useChampionBuilds(toRef(props, 'championId'), toRef(props, 'position'))
const { items, summoners, runeTree, pending: staticPending, loadChampion, championStatic } = useStaticData()
const { patch } = useChampionStatics()

// The champion's spells need the patch, which may land after the view does.
watch(() => `${props.championId}:${patch.value}`, () => loadChampion(props.championId), { immediate: true })

const options = computed<BuildOption[]>(() => {
  const list: BuildOption[] = []
  const draftBuild = props.draft?.build
  if (draftBuild && draftBuild.championId === props.championId) {
    list.push({
      key: 'draft',
      core: draftBuild.core,
      firstItemId: draftBuild.firstItemId,
      keystoneId: draftBuild.core.runePage?.primaryKeystoneId ?? null,
      buildTree: draftBuild.buildTree,
      games: draftBuild.games,
      winRate: draftBuild.games > 0 ? draftBuild.wins / draftBuild.games : null,
      standard: draftBuild.source === 'standard',
    })
  }
  for (const build of response.value?.builds ?? []) {
    list.push({
      key: `lane-${build.firstItemId}-${build.primaryKeystoneId}`,
      core: build.core,
      firstItemId: build.firstItemId,
      keystoneId: build.primaryKeystoneId,
      buildTree: build.buildTree,
      games: build.games,
      winRate: build.winRate,
    })
  }
  return list
})

const selected = ref<string | null>(null)
// A new champion, or a draft build arriving, puts the view back on the first row.
watch(
  () => `${props.championId}:${props.position}:${options.value[0]?.key}`,
  () => (selected.value = options.value[0]?.key ?? null),
)

const shown = computed(() => options.value.find(option => option.key === selected.value) ?? options.value[0] ?? null)
const waiting = computed(() => !shown.value && (lanePending.value || props.draft?.pending))
</script>

<template>
  <div class="surface relative grid h-full min-h-0 grid-cols-[18rem_minmax(0,1fr)] overflow-hidden rounded-xl">
    <!-- The thin bar that says a newer draft is being asked for, without hiding the answer on screen. -->
    <div v-if="draft?.pending && shown" class="absolute inset-x-0 top-0 z-10 h-0.5 overflow-hidden">
      <div class="h-full w-1/3 animate-[tm-scan_1.1s_ease-in-out_infinite] bg-gradient-to-r from-transparent via-primary to-transparent" />
    </div>

    <aside class="flex min-h-0 flex-col border-r border-default bg-muted/50">
      <div class="border-b border-default px-3 py-2.5">
        <slot name="header" />
      </div>
      <div class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-2">
        <BuildList v-model="selected" :options="options" :pending="lanePending || !!draft?.pending" :draft-label="draft?.label" />
        <BuildMains :champion-id="championId" />
      </div>
    </aside>

    <div v-if="shown" class="min-h-0 space-y-4 overflow-y-auto p-3">
      <BuildCore
        :summoner-spells="shown.core.summonerSpells"
        :starter-items="shown.core.starterItems"
        :skill-order="shown.core.skillOrder"
        :boots="shown.core.boots"
        :item-path="shown.core.itemPath"
        :rune-page="shown.core.runePage"
        :champion-static="championStatic(championId)"
        :items-map="items"
        :summoners-map="summoners"
        :summoners-pending="staticPending"
        :rune-tree="runeTree"
        :no-runes-message="shown.key === 'draft' ? 'No rune data in the sampled games.' : null"
      />
      <!-- The site's tree, drawn smaller and tighter to fit the pane. -->
      <ChampionBuildPanelBuildTree
        v-if="shown.buildTree.length > 0"
        :tree="shown.buildTree"
        :first-item-id="shown.firstItemId"
        :item-path="shown.core.itemPath?.itemIds ?? []"
        :items-map="items"
        :item-size="28"
        :h-gap="10"
        :v-gap="20"
      />
    </div>

    <div v-else class="flex flex-col items-center justify-center gap-2 p-8 text-center">
      <UIcon :name="waiting ? 'i-lucide-loader-circle' : 'i-lucide-scroll-text'" class="size-6 text-dimmed" />
      <p class="text-sm text-muted">{{ waiting ? 'Reading the build…' : 'No build for this pick yet' }}</p>
      <p v-if="draft?.error && !waiting" class="max-w-xs text-xs text-dimmed">{{ draft.error }}</p>
    </div>
  </div>
</template>
