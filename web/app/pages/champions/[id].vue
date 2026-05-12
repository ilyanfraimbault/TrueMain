<script setup lang="ts">
import type {
  ChampionBuildTreeNodeResponse,
  ItemSetOptionResponse,
  RunePageOptionResponse,
  SkillOrderOptionResponse,
  SummonerSpellOptionResponse
} from '~/types/champions'
import type { StaticItemData } from '~/types/static-data'
import { formatPercentage } from '~/utils/items'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const {
  advanced,
  buildTree,
  championState,
  championStatic,
  core,
  isLoading,
  patchOptions,
  positionOptions,
  selectedPatch,
  selectedPosition,
  setFilter,
  summary
} = useChampionPageStore(championId)

useSeoMeta({
  title: () => championStatic.value.championName ?? 'TrueMain',
  description: () => `Champion ${championId.value} build, runes and skill order.`
})

function itemsFromIds(ids: number[] | undefined | null): StaticItemData[] {
  if (!ids) return []
  return ids
    .map(id => championStatic.value.items[id])
    .filter((item): item is StaticItemData => Boolean(item))
}

function itemsFromSet(set: ItemSetOptionResponse | null): StaticItemData[] {
  return itemsFromIds(set?.itemIds ?? null)
}

function summonerName(id: number): string {
  return championStatic.value.summonerSpells[id]?.name ?? `Spell ${id}`
}

function summonerIcon(id: number): string {
  return championStatic.value.summonerSpells[id]?.iconUrl ?? ''
}

function perkIcon(id: number): string {
  return id > 0 ? (championStatic.value.perks[id]?.iconUrl ?? '') : ''
}

function perkStyleIcon(id: number): string {
  return id > 0 ? (championStatic.value.perkStyles[id]?.iconUrl ?? '') : ''
}

function spellByKey(key: string) {
  return championStatic.value.championSpells[key] ?? null
}

// Top-3 root build paths from the tree, each followed greedily through its most-played children
type BuildPath = { items: StaticItemData[], pickRate: number, games: number, winRate: number }

function followBranch(node: ChampionBuildTreeNodeResponse, depth = 0): ChampionBuildTreeNodeResponse[] {
  if (depth > 6 || !node.children.length) return [node]
  const next = [...node.children].sort((a, b) => b.pickRate - a.pickRate)[0]
  return next ? [node, ...followBranch(next, depth + 1)] : [node]
}

const topBuildPaths = computed<BuildPath[]>(() => {
  const roots = buildTree.value?.build ?? []
  return [...roots]
    .sort((a, b) => b.pickRate - a.pickRate)
    .slice(0, 3)
    .map((root): BuildPath => {
      const chain = followBranch(root)
      const last = chain[chain.length - 1] ?? root
      return {
        items: chain
          .map(n => championStatic.value.items[n.itemId])
          .filter((item): item is StaticItemData => Boolean(item)),
        pickRate: root.pickRate,
        games: last.games,
        winRate: last.wins / Math.max(last.games, 1)
      }
    })
})

// Top-3 rune pages by playRate
const topRunePages = computed<RunePageOptionResponse[]>(() => {
  const options = advanced.value?.runePageOptions ?? []
  return [...options].sort((a, b) => b.playRate - a.playRate).slice(0, 3)
})

const topSummoners = computed<SummonerSpellOptionResponse | null>(() => core.value?.summonerSpells ?? null)
const topSkillOrder = computed<SkillOrderOptionResponse | null>(() => core.value?.skillOrder ?? null)

const buildPathItems = computed(() => itemsFromIds(core.value?.buildPath?.itemIds ?? null))
const starterItems = computed(() => itemsFromSet(core.value?.starterItems ?? null))
const bootsItems = computed(() => itemsFromSet(core.value?.boots ?? null))

function onPatchChange(value: unknown) {
  void setFilter(String(value), null)
}

function onPositionChange(value: unknown) {
  void setFilter(null, String(value) as never)
}
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
      v-else-if="championState.error.value"
      color="error"
      variant="soft"
      title="Failed to load champion"
      :description="championState.error.value.message"
    />

    <template v-else-if="summary">
      <!-- Header -->
      <header class="flex flex-wrap items-center gap-4">
        <NuxtImg
          v-if="championStatic.championIconUrl"
          :src="championStatic.championIconUrl"
          :alt="championStatic.championName ?? ''"
          width="80"
          height="80"
          class="size-20 rounded"
        />
        <div class="flex-1">
          <h1 class="text-2xl font-semibold">
            {{ championStatic.championName ?? `Champion ${championId}` }}
          </h1>
          <p class="text-sm text-muted">
            {{ summary.position || '—' }} · {{ summary.games }} games · {{ formatPercentage(summary.winRate) }} WR
          </p>
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <USelect
            :model-value="selectedPatch"
            :items="patchOptions"
            placeholder="Patch"
            class="w-28"
            @update:model-value="onPatchChange"
          />
          <USelect
            :model-value="selectedPosition || undefined"
            :items="positionOptions"
            placeholder="Position"
            class="w-32"
            @update:model-value="onPositionChange"
          />
        </div>
      </header>

      <!-- Summoner spells + skill order -->
      <section class="flex flex-wrap gap-8">
        <div>
          <h2 class="text-sm font-medium text-muted">
            Summoner spells
          </h2>
          <div
            v-if="topSummoners"
            class="mt-2 flex items-center gap-1"
          >
            <NuxtImg
              :src="summonerIcon(topSummoners.spell1Id)"
              :alt="summonerName(topSummoners.spell1Id)"
              :title="summonerName(topSummoners.spell1Id)"
              width="40"
              height="40"
              class="size-10 rounded"
            />
            <NuxtImg
              :src="summonerIcon(topSummoners.spell2Id)"
              :alt="summonerName(topSummoners.spell2Id)"
              :title="summonerName(topSummoners.spell2Id)"
              width="40"
              height="40"
              class="size-10 rounded"
            />
            <span class="ml-2 text-sm text-muted">
              {{ formatPercentage(topSummoners.winRate) }} WR · {{ topSummoners.games }} games
            </span>
          </div>
          <p
            v-else
            class="text-sm text-muted"
          >
            No data
          </p>
        </div>

        <div>
          <h2 class="text-sm font-medium text-muted">
            Skill order
          </h2>
          <div
            v-if="topSkillOrder"
            class="mt-2 flex flex-wrap items-center gap-1"
          >
            <template
              v-for="(key, index) in topSkillOrder.sequence"
              :key="`${key}-${index}`"
            >
              <NuxtImg
                v-if="spellByKey(key)"
                :src="spellByKey(key)!.iconUrl"
                :alt="spellByKey(key)!.name"
                :title="`${key} — ${spellByKey(key)!.name}`"
                width="32"
                height="32"
                class="size-8 rounded"
              />
              <span
                v-else
                class="inline-flex size-8 items-center justify-center rounded border border-default text-xs"
              >
                {{ key }}
              </span>
            </template>
            <span class="ml-2 text-sm text-muted">
              {{ formatPercentage(topSkillOrder.winRate) }} WR · {{ topSkillOrder.games }} games
            </span>
          </div>
          <p
            v-else
            class="text-sm text-muted"
          >
            No data
          </p>
        </div>
      </section>

      <!-- Runes (top 3) -->
      <section>
        <h2 class="text-base font-semibold">
          Runes
        </h2>
        <ul class="mt-3 space-y-3">
          <li
            v-for="(page, index) in topRunePages"
            :key="`rune-${index}`"
            class="flex flex-wrap items-center gap-2"
          >
            <NuxtImg
              v-if="perkStyleIcon(page.primaryStyleId)"
              :src="perkStyleIcon(page.primaryStyleId)"
              :alt="`Style ${page.primaryStyleId}`"
              width="24"
              height="24"
              class="size-6"
            />
            <NuxtImg
              v-if="perkIcon(page.primaryKeystoneId)"
              :src="perkIcon(page.primaryKeystoneId)"
              :alt="`Keystone ${page.primaryKeystoneId}`"
              width="40"
              height="40"
              class="size-10 rounded-full"
            />
            <template
              v-for="perkId in [page.primaryPerk1Id, page.primaryPerk2Id, page.primaryPerk3Id]"
              :key="`p-${index}-${perkId}`"
            >
              <NuxtImg
                v-if="perkIcon(perkId)"
                :src="perkIcon(perkId)"
                :alt="`Perk ${perkId}`"
                width="28"
                height="28"
                class="size-7 rounded-full"
              />
            </template>
            <span class="text-sm text-muted">|</span>
            <NuxtImg
              v-if="perkStyleIcon(page.secondaryStyleId)"
              :src="perkStyleIcon(page.secondaryStyleId)"
              :alt="`Style ${page.secondaryStyleId}`"
              width="24"
              height="24"
              class="size-6"
            />
            <template
              v-for="perkId in [page.secondaryPerk1Id, page.secondaryPerk2Id]"
              :key="`s-${index}-${perkId}`"
            >
              <NuxtImg
                v-if="perkIcon(perkId)"
                :src="perkIcon(perkId)"
                :alt="`Perk ${perkId}`"
                width="28"
                height="28"
                class="size-7 rounded-full"
              />
            </template>
            <span class="ml-auto text-sm text-muted">
              {{ formatPercentage(page.playRate) }} · {{ formatPercentage(page.winRate) }} WR · {{ page.games }}g
            </span>
          </li>
          <li
            v-if="!topRunePages.length"
            class="text-sm text-muted"
          >
            No data
          </li>
        </ul>
      </section>

      <!-- Build -->
      <section>
        <h2 class="text-base font-semibold">
          Build
        </h2>
        <div class="mt-3 grid gap-4 sm:grid-cols-3">
          <div>
            <h3 class="text-sm font-medium text-muted">
              Starter items
            </h3>
            <div class="mt-2 flex flex-wrap gap-1">
              <NuxtImg
                v-for="(item, index) in starterItems"
                :key="`starter-${item.id}-${index}`"
                :src="item.iconUrl"
                :alt="item.name"
                :title="item.name"
                width="36"
                height="36"
                class="size-9 rounded"
              />
              <span
                v-if="!starterItems.length"
                class="text-sm text-muted"
              >
                No data
              </span>
            </div>
          </div>

          <div>
            <h3 class="text-sm font-medium text-muted">
              Boots
            </h3>
            <div class="mt-2 flex flex-wrap gap-1">
              <NuxtImg
                v-for="(item, index) in bootsItems"
                :key="`boots-${item.id}-${index}`"
                :src="item.iconUrl"
                :alt="item.name"
                :title="item.name"
                width="36"
                height="36"
                class="size-9 rounded"
              />
              <span
                v-if="!bootsItems.length"
                class="text-sm text-muted"
              >
                No data
              </span>
            </div>
          </div>

          <div>
            <h3 class="text-sm font-medium text-muted">
              Dominant build path
            </h3>
            <div class="mt-2 flex flex-wrap gap-1">
              <NuxtImg
                v-for="(item, index) in buildPathItems"
                :key="`bp-${item.id}-${index}`"
                :src="item.iconUrl"
                :alt="item.name"
                :title="item.name"
                width="36"
                height="36"
                class="size-9 rounded"
              />
              <span
                v-if="!buildPathItems.length"
                class="text-sm text-muted"
              >
                No data
              </span>
            </div>
          </div>
        </div>

        <h3 class="mt-6 text-sm font-medium text-muted">
          Top build paths
        </h3>
        <ul class="mt-2 space-y-2">
          <li
            v-for="(path, pathIndex) in topBuildPaths"
            :key="`path-${pathIndex}`"
            class="flex flex-wrap items-center gap-2"
          >
            <span class="text-sm tabular-nums text-muted">
              {{ formatPercentage(path.pickRate) }}
            </span>
            <NuxtImg
              v-for="(item, index) in path.items"
              :key="`pathitem-${pathIndex}-${item.id}-${index}`"
              :src="item.iconUrl"
              :alt="item.name"
              :title="item.name"
              width="32"
              height="32"
              class="size-8 rounded"
            />
            <span class="ml-auto text-sm text-muted">
              {{ formatPercentage(path.winRate) }} WR · {{ path.games }} games
            </span>
          </li>
          <li
            v-if="!topBuildPaths.length"
            class="text-sm text-muted"
          >
            No data
          </li>
        </ul>
      </section>
    </template>
  </main>
</template>
