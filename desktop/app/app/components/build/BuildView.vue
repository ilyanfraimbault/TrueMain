<script setup lang="ts">
import type { ProfileIdentity } from '#shared/types/profile'
import { getProfileIconUrl } from '#shared/utils/ddragon'
import type { BuildOption } from '~/types/build'
import type { Lane } from '~/types/draft'
import type { DraftBuild } from '~/composables/useDraftBuild'
import { LANE_LABELS } from '~/types/draft'

/**
 * A champion's build, laid out the way the reference client does after a pick
 * — the builds to choose from and the champion's true mains down the left, the
 * build itself on the right — the core (`BuildCore`, the site's icons and
 * rune block laid out for the pane) over the site's build tree.
 *
 * With a draft, its own build (computed against the draft as it stands) is the
 * first row and the default; the champion's lane builds follow, so the player
 * can compare the draft's answer with what the lane usually runs. A true main
 * clicked in the list shows that player's own build on the champion instead.
 */
const props = defineProps<{
  championId: number
  position: string
  /** The build computed for the draft, when there is one. */
  draft?: { build: DraftBuild | null, pending: boolean, error: string | null, opponentId?: number | null, label?: string } | null
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

/** The row on screen: a build's key, or `main` for the true main picked in the list. */
const selected = ref<string | null>(null)
const main = ref<{ identity: ProfileIdentity } | null>(null)

// A new champion puts the view back on its first row; a draft build arriving
// takes the first row too, unless a true main's build is being read.
watch(() => `${props.championId}:${props.position}`, () => {
  main.value = null
  selected.value = options.value[0]?.key ?? null
})
watch(() => options.value[0]?.key, (first) => {
  if (selected.value !== 'main') selected.value = first ?? null
})

// ─── A true main's own build ────────────────────────────────────────────────

const truemainBuilds = useTruemainBuild()
const mainNameTag = computed(() => (main.value ? favoriteNameTag(main.value.identity.gameName, main.value.identity.tagLine) : null))
const onMain = computed(() => selected.value === 'main' && mainNameTag.value !== null)

function selectMain(row: { identity: ProfileIdentity }) {
  main.value = row
  selected.value = 'main'
  truemainBuilds.load(mainNameTag.value!, props.championId, props.position)
}

/** Their builds on the champion: `undefined` while asked, `null` when they have none. */
const mainAnswer = computed(() => (onMain.value ? truemainBuilds.answerOf(mainNameTag.value!, props.championId, props.position) : undefined))
const mainPending = computed(() => onMain.value && truemainBuilds.isPending(mainNameTag.value!, props.championId, props.position))
const mainFailed = computed(() => onMain.value && truemainBuilds.hasFailed(mainNameTag.value!, props.championId, props.position))

/** Their most played build, the one their page opens on. */
const mainOption = computed<BuildOption | null>(() => {
  const build = mainAnswer.value?.builds[0]
  if (!build) return null
  return {
    key: 'main',
    core: build.core,
    firstItemId: build.firstItemId,
    keystoneId: build.primaryKeystoneId,
    buildTree: build.buildTree,
    games: build.games,
    winRate: build.winRate,
  }
})

/** Where their build was read: a patch, and a lane when it is not the one asked for. */
const mainScope = computed(() => {
  const answer = mainAnswer.value
  if (!answer) return null
  const lane = answer.position && answer.position !== props.position ? LANE_LABELS[answer.position as Lane] ?? answer.position : null
  return [answer.patch ? `Patch ${answer.patch}` : null, lane ? `on ${lane}` : null].filter(Boolean).join(' · ')
})

const shown = computed(() => {
  if (onMain.value) return mainOption.value
  return options.value.find(option => option.key === selected.value) ?? options.value[0] ?? null
})
const waiting = computed(() => !shown.value && (onMain.value ? mainPending.value : lanePending.value || props.draft?.pending))
const emptyMessage = computed(() => {
  if (onMain.value && main.value) {
    return mainFailed.value ? `${main.value.identity.gameName}'s build could not be loaded` : `No build of ${main.value.identity.gameName} on this champion yet`
  }
  return 'No build for this pick yet'
})
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
        <BuildList v-model="selected" :options="options" :pending="lanePending || !!draft?.pending" :draft-label="draft?.label" :draft-opponent="draft?.opponentId ?? null" />
        <BuildMains :champion-id="championId" :position="position" :selected-name-tag="onMain ? mainNameTag : null" @select="selectMain" />
      </div>
    </aside>

    <div v-if="shown" class="min-h-0 space-y-4 overflow-y-auto p-3">
      <!-- Whose build this is, when it is a true main's. -->
      <div v-if="onMain && main" class="flex items-center gap-2.5 rounded-lg bg-elevated/60 py-1.5 pl-1.5 pr-1">
        <SkeletonImage
          :src="getProfileIconUrl(main.identity.profileIconId, patch)"
          :alt="main.identity.gameName"
          width="28"
          height="28"
          class="size-7 shrink-0 rounded-md"
        />
        <p class="min-w-0 flex-1 truncate text-[13px] leading-tight">
          <span class="font-semibold text-highlighted">{{ main.identity.gameName }}</span>
          <span class="text-muted">'s build</span>
          <span class="ml-2 text-[11px] tabular-nums text-dimmed">{{ shown.games.toLocaleString('en-US') }} games{{ mainScope ? ` · ${mainScope}` : '' }}</span>
        </p>
        <UButton
          size="xs"
          color="neutral"
          variant="ghost"
          trailing-icon="i-lucide-external-link"
          label="Profile"
          @click="openOnSite(`/truemains/${encodeURIComponent(mainNameTag!)}`)"
        />
      </div>

      <BuildCore
        :summoner-spells="shown.core.summonerSpells"
        :starter-items="shown.core.starterItems"
        :skill-order="shown.core.skillOrder"
        :boots="shown.core.boots"
        :rune-page="shown.core.runePage"
        :champion-static="championStatic(championId)"
        :items-map="items"
        :summoners-map="summoners"
        :summoners-pending="staticPending"
        :rune-tree="runeTree"
        :no-runes-message="shown.key === 'draft' ? 'No rune data in the sampled games.' : null"
        :champion-name="championStatic(championId)?.championName ?? ''"
      />
      <!-- The site's tree, drawn smaller and tighter to fit the pane. It is the build path too,
           item by item; a build with no branch to draw (a true main's thin sample) states the path instead. -->
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
      <div v-else-if="shown.core.itemPath?.itemIds.length" class="flex justify-center">
        <ChampionCoreBuildPath :path="shown.core.itemPath" :items-map="items" />
      </div>
    </div>

    <div v-else class="flex flex-col items-center justify-center gap-2 p-8 text-center">
      <UIcon :name="waiting ? 'i-lucide-loader-circle' : 'i-lucide-scroll-text'" class="size-6 text-dimmed" />
      <p class="text-sm text-muted">{{ waiting ? 'Reading the build…' : emptyMessage }}</p>
      <p v-if="draft?.error && !waiting" class="max-w-xs text-xs text-dimmed">{{ draft.error }}</p>
    </div>
  </div>
</template>
