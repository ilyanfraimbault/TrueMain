<script setup lang="ts">
import type { BuildRunePage } from '~~/shared/types/champions'
import type { RuneTreeResponse, RuneTreeStyle } from '~~/shared/types/static-data'

const props = defineProps<{
  page: BuildRunePage
  tree: RuneTreeResponse
  /** Base size (px) for the regular perks. Other rows scale from this. */
  size?: number
  /** Override the keystone row size independently of `size`. Useful when the
   *  surrounding layout wants emphasised keystones but smaller perk rows
   *  (e.g. the rune-variations panel). Defaults to `size`. */
  keystoneSize?: number
}>()

const baseSize = computed(() => props.size ?? 32)
const keystoneSize = computed(() => props.keystoneSize ?? baseSize.value)
const perkSize = computed(() => Math.max(20, baseSize.value - 4))
const secondarySize = computed(() => Math.max(16, baseSize.value - 12))
const shardSize = computed(() => Math.max(12, baseSize.value - 16))

const primary = computed<RuneTreeStyle | null>(() =>
  props.tree.styles.find(s => s.styleId === props.page.primaryStyleId) ?? null,
)

const secondary = computed<RuneTreeStyle | null>(() =>
  props.tree.styles.find(s => s.styleId === props.page.secondaryStyleId) ?? null,
)

const selectedPrimary = computed(() => new Set([
  props.page.primaryKeystoneId,
  props.page.primaryPerk1Id,
  props.page.primaryPerk2Id,
  props.page.primaryPerk3Id,
]))

const selectedSecondary = computed(() => new Set([
  props.page.secondaryPerk1Id,
  props.page.secondaryPerk2Id,
]))

const selectedShards = computed(() => [
  props.page.statOffense,
  props.page.statFlex,
  props.page.statDefense,
])
</script>

<template>
  <div class="flex flex-wrap items-stretch gap-x-6 gap-y-4">
    <!-- Primary tree (left) -->
    <section
      v-if="primary"
      class="flex flex-col items-center gap-1"
    >
      <!-- Keystone row -->
      <div class="flex items-center gap-0.5">
        <GameTooltipPerkIcon
          v-for="id in primary.keystones"
          :key="`pk-${id}`"
          :perk="tree.perks[id] ?? null"
          :width="keystoneSize"
          :height="keystoneSize"
          :style="{ width: `${keystoneSize}px`, height: `${keystoneSize}px` }"
          :class="[
            'rounded-full transition',
            id === page.primaryKeystoneId ? '' : 'opacity-40 grayscale',
          ]"
        />
      </div>

      <!-- 3 sub-rows of 3 perks -->
      <div
        v-for="(row, rowIndex) in primary.subRows"
        :key="`prow-${rowIndex}`"
        class="flex items-center gap-1"
      >
        <GameTooltipPerkIcon
          v-for="id in row"
          :key="`pp-${rowIndex}-${id}`"
          :perk="tree.perks[id] ?? null"
          :width="perkSize"
          :height="perkSize"
          :style="{ width: `${perkSize}px`, height: `${perkSize}px` }"
          :class="[
            'rounded-full transition',
            selectedPrimary.has(id) ? '' : 'opacity-40 grayscale',
          ]"
        />
      </div>
    </section>

    <!-- Right column: secondary on top + shards on bottom -->
    <div class="flex flex-col justify-between">
      <!-- Secondary tree (3 rows of 3, no keystone) -->
      <section
        v-if="secondary"
        class="flex flex-col items-center gap-1"
      >
        <div
          v-for="(row, rowIndex) in secondary.subRows"
          :key="`srow-${rowIndex}`"
          class="flex items-center gap-1"
        >
          <GameTooltipPerkIcon
            v-for="id in row"
            :key="`sp-${rowIndex}-${id}`"
            :perk="tree.perks[id] ?? null"
            :width="secondarySize"
            :height="secondarySize"
            :style="{ width: `${secondarySize}px`, height: `${secondarySize}px` }"
            :class="[
              'rounded-full transition',
              selectedSecondary.has(id) ? '' : 'opacity-40 grayscale',
            ]"
          />
        </div>
      </section>

      <!-- Stat shards -->
      <section class="flex flex-col items-center gap-1">
        <div
          v-for="(row, rowIndex) in tree.shardSlots"
          :key="`shard-row-${rowIndex}`"
          class="flex items-center gap-1"
        >
          <GameTooltipPerkIcon
            v-for="id in row"
            :key="`shard-${rowIndex}-${id}`"
            :perk="tree.perks[id] ?? null"
            :width="shardSize"
            :height="shardSize"
            :style="{ width: `${shardSize}px`, height: `${shardSize}px` }"
            :class="[
              'rounded-full transition',
              selectedShards[rowIndex] === id ? '' : 'opacity-40 grayscale',
            ]"
          />
        </div>
      </section>
    </div>
  </div>
</template>
