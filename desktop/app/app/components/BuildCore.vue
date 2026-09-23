<script setup lang="ts">
import type { BuildCoreView, BuildTreeNode } from '~/types/build'

/**
 * The build — summoners, skill order, starter, boots, runes, build path and
 * the tree behind it — the same blocks as the site's champion and matchup
 * pages (`web/app/components/Champion/BuildPanel/Core.vue`), laid out for a
 * window rather than a page.
 */
const props = defineProps<{
  core: BuildCoreView
  championId: number
  firstItemId: number
  buildTree: BuildTreeNode[]
  /**
   * Lay the item path beside the runes rather than under them — for a panel
   * wider than it is tall, where stacking would leave the sides empty.
   */
  wide?: boolean
}>()

const { aliasOf } = useChampionStatics()
const { spell, item, loadChampionSpells, championSpell } = useBuildStatics()

const alias = computed(() => aliasOf(props.championId))
watch(alias, value => value && loadChampionSpells(value), { immediate: true })

const percent = (ratio: number | undefined) => (ratio === undefined ? null : `${Math.round(ratio * 100)}%`)
</script>

<template>
  <!--
    Two arrangements of the same blocks. Stacked: the runes beside the four
    small choices, then the path and its tree full width. Wide: the runes over
    the four choices in one column, the path and its tree in the other. Nothing
    stretches to fill — the panel picks the arrangement that matches its shape
    and scales the whole block to the room it has (`BuildPanel`).
  -->
  <div :class="wide ? 'grid grid-cols-[auto_minmax(0,1fr)] gap-3' : 'flex flex-col gap-3'">
    <div :class="wide ? 'flex flex-col gap-3' : 'grid grid-cols-[auto_minmax(0,1fr)] gap-3'">
      <BuildWell title="Runes" :rate="percent(core.runePage?.winRate)" vertical>
        <RuneTree v-if="core.runePage" :page="core.runePage" class="px-1" />
      </BuildWell>

      <div class="grid grid-cols-2 gap-3">
        <BuildWell title="Summoners" :rate="percent(core.summonerSpells?.winRate)">
          <template v-if="core.summonerSpells">
            <GameIcon :source="spell(core.summonerSpells.spell1Id)" size="size-9" />
            <GameIcon :source="spell(core.summonerSpells.spell2Id)" size="size-9" />
          </template>
        </BuildWell>

        <BuildWell title="Boots" :rate="percent(core.boots?.winRate)">
          <GameIcon
            v-for="(id, index) in core.boots?.itemIds ?? []"
            :key="`boots-${index}`"
            :source="item(id)"
            size="size-9"
          />
        </BuildWell>

        <BuildWell title="Skill order" :rate="percent(core.skillOrder?.winRate)" class="col-span-2">
          <template v-for="(key, index) in core.skillOrder?.sequence ?? []" :key="key">
            <div class="relative size-9 shrink-0">
              <GameIcon v-if="alias && championSpell(alias, key)" :source="championSpell(alias, key)" size="size-9" />
              <span v-else class="flex size-9 items-center justify-center rounded bg-accented text-sm font-bold">{{ key }}</span>
              <ItemRankBadge :value="key" />
            </div>
            <UIcon
              v-if="index < (core.skillOrder?.sequence.length ?? 0) - 1"
              name="i-lucide-chevron-right"
              class="size-4 shrink-0 text-dimmed"
            />
          </template>
        </BuildWell>

        <BuildWell title="Starter" :rate="percent(core.starterItems?.winRate)" class="col-span-2">
          <GameIcon
            v-for="(id, index) in core.starterItems?.itemIds ?? []"
            :key="`starter-${index}`"
            :source="item(id)"
            size="size-9"
          />
        </BuildWell>
      </div>
    </div>

    <!-- The path in one line, and under it the tree it was drawn from. -->
    <BuildWell title="Build path" :rate="percent(core.itemPath?.winRate)" vertical>
      <div class="flex items-center gap-1">
        <template v-for="(id, index) in core.itemPath?.itemIds ?? []" :key="`path-${index}`">
          <GameIcon :source="item(id)" size="size-9" />
          <UIcon
            v-if="index < (core.itemPath?.itemIds.length ?? 0) - 1"
            name="i-lucide-chevron-right"
            class="size-4 shrink-0 text-dimmed"
          />
        </template>
      </div>
      <BuildTree
        v-if="buildTree.length"
        :tree="buildTree"
        :first-item-id="firstItemId"
        :item-path="core.itemPath?.itemIds ?? []"
        class="mt-4 max-w-full"
      />
    </BuildWell>
  </div>
</template>
