<script setup lang="ts">
import type { KitRunePage } from '#shared/types/kit'
import type { RuneTreeResponse, RuneTreeStyle } from '#shared/types/static-data'
import { withPrimaryPerk, withPrimaryStyle, withSecondaryPerk, withSecondaryStyle, withShard } from '~/utils/kit-runes'

/**
 * The kit editor's rune page: the rune block's layout (`ChampionCoreRunes`) —
 * the primary tree on the left, the secondary and the shards on the right —
 * with every rune a button, and each tree's row of styles above it. The
 * edits follow the client's rules (`utils/kit-runes.ts`).
 */
const props = defineProps<{ tree: RuneTreeResponse }>()
const page = defineModel<KitRunePage>({ required: true })

const primary = computed<RuneTreeStyle | null>(() => props.tree.styles.find(style => style.styleId === page.value.primaryStyleId) ?? null)
const secondary = computed<RuneTreeStyle | null>(() => props.tree.styles.find(style => style.styleId === page.value.secondaryStyleId) ?? null)
const secondaryStyles = computed(() => props.tree.styles.filter(style => style.styleId !== page.value.primaryStyleId))

const primaryChosen = computed(() => new Set([page.value.primaryKeystoneId, page.value.primaryPerk1Id, page.value.primaryPerk2Id, page.value.primaryPerk3Id]))
const secondaryChosen = computed(() => new Set([page.value.secondaryPerk1Id, page.value.secondaryPerk2Id]))
const shards = computed(() => [page.value.statOffense, page.value.statFlex, page.value.statDefense])

const perkName = (id: number) => props.tree.perks[id]?.name ?? `Rune ${id}`
const styleName = (id: number) => props.tree.perkStyles[id]?.name ?? `Tree ${id}`

const KEYSTONE = 34
const PERK = 26
const SMALL = 22
const SHARD = 18
</script>

<template>
  <div class="flex flex-wrap items-start gap-x-8 gap-y-4">
    <section class="flex flex-col items-center gap-2" aria-label="Primary tree">
      <div class="flex gap-1" role="group" aria-label="Primary tree">
        <button
          v-for="style in tree.styles"
          :key="style.styleId"
          type="button"
          :aria-pressed="style.styleId === page.primaryStyleId"
          :aria-label="styleName(style.styleId)"
          class="rounded-full p-0.5 transition"
          :class="style.styleId === page.primaryStyleId ? 'selected-perk' : 'deselected hover:opacity-100'"
          @click="page = withPrimaryStyle(page, tree, style.styleId)"
        >
          <GameTooltipPerkStyleIcon :style="tree.perkStyles[style.styleId] ?? null" :width="SMALL" :height="SMALL" class="size-[22px]" />
        </button>
      </div>
      <template v-if="primary">
        <div class="flex gap-1">
          <button
            v-for="id in primary.keystones"
            :key="id"
            type="button"
            class="rounded-full transition"
            :class="id === page.primaryKeystoneId ? 'selected-perk' : 'deselected hover:opacity-100'"
            :aria-label="perkName(id)"
            :aria-pressed="id === page.primaryKeystoneId"
            @click="page = withPrimaryPerk(page, tree, id)"
          >
            <GameTooltipPerkIcon :perk="tree.perks[id] ?? null" :width="KEYSTONE" :height="KEYSTONE" class="size-[34px] rounded-full" />
          </button>
        </div>
        <div v-for="(row, rowIndex) in primary.subRows" :key="`p-${rowIndex}`" class="flex gap-1.5">
          <button
            v-for="id in row"
            :key="id"
            type="button"
            class="rounded-full transition"
            :class="primaryChosen.has(id) ? 'selected-perk' : 'deselected hover:opacity-100'"
            :aria-label="perkName(id)"
            :aria-pressed="primaryChosen.has(id)"
            @click="page = withPrimaryPerk(page, tree, id)"
          >
            <GameTooltipPerkIcon :perk="tree.perks[id] ?? null" :width="PERK" :height="PERK" class="size-[26px] rounded-full" />
          </button>
        </div>
      </template>
    </section>

    <div class="flex flex-col gap-4">
      <section class="flex flex-col items-center gap-2" aria-label="Secondary tree">
        <div class="flex gap-1" role="group" aria-label="Secondary tree">
          <button
            v-for="style in secondaryStyles"
            :key="style.styleId"
            type="button"
            :aria-pressed="style.styleId === page.secondaryStyleId"
            :aria-label="styleName(style.styleId)"
            class="rounded-full p-0.5 transition"
            :class="style.styleId === page.secondaryStyleId ? 'selected-perk' : 'deselected hover:opacity-100'"
            @click="page = withSecondaryStyle(page, tree, style.styleId)"
          >
            <GameTooltipPerkStyleIcon :style="tree.perkStyles[style.styleId] ?? null" :width="SMALL" :height="SMALL" class="size-[22px]" />
          </button>
        </div>
        <template v-if="secondary">
          <div v-for="(row, rowIndex) in secondary.subRows" :key="`s-${rowIndex}`" class="flex gap-1.5">
            <button
              v-for="id in row"
              :key="id"
              type="button"
              class="rounded-full transition"
              :class="secondaryChosen.has(id) ? 'selected-perk' : 'deselected hover:opacity-100'"
              :aria-label="perkName(id)"
              :aria-pressed="secondaryChosen.has(id)"
              @click="page = withSecondaryPerk(page, tree, id)"
            >
              <GameTooltipPerkIcon :perk="tree.perks[id] ?? null" :width="SMALL" :height="SMALL" class="size-[22px] rounded-full" />
            </button>
          </div>
        </template>
      </section>

      <section class="flex flex-col items-center gap-1" aria-label="Shards">
        <div v-for="(row, rowIndex) in tree.shardSlots" :key="`shard-${rowIndex}`" class="flex gap-1.5">
          <button
            v-for="id in row"
            :key="id"
            type="button"
            class="rounded-full transition"
            :class="shards[rowIndex] === id ? 'selected-perk' : 'deselected hover:opacity-100'"
            :aria-label="perkName(id)"
            :aria-pressed="shards[rowIndex] === id"
            @click="page = withShard(page, tree, rowIndex, id)"
          >
            <GameTooltipPerkIcon :perk="tree.perks[id] ?? null" :width="SHARD" :height="SHARD" class="size-[18px] rounded-full" />
          </button>
        </div>
      </section>
    </div>
  </div>
</template>
