<script setup lang="ts">
import type { RunePageOptionResponse } from '~~/shared/types/champions'
import type { RuneTreeResponse, RuneTreeStyle } from '~~/shared/types/static-data'

const props = defineProps<{
  page: RunePageOptionResponse
  tree: RuneTreeResponse
}>()

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

function perkIcon(id: number): string {
  return props.tree.perks[id]?.iconUrl ?? ''
}

function perkName(id: number): string {
  return props.tree.perks[id]?.name ?? `Perk ${id}`
}
</script>

<template>
  <div class="flex flex-wrap gap-x-16 gap-y-8">
    <!-- Primary tree (left) -->
    <section
      v-if="primary"
      class="flex flex-col items-center gap-5"
    >
      <!-- Keystone row (3-4 options) -->
      <div class="flex gap-3">
        <NuxtImg
          v-for="id in primary.keystones"
          :key="`pk-${id}`"
          :src="perkIcon(id)"
          :alt="perkName(id)"
          :title="perkName(id)"
          width="56"
          height="56"
          :class="[
            'size-14 rounded-full transition',
            id === page.primaryKeystoneId
              ? 'ring-2 ring-primary ring-offset-2 ring-offset-default'
              : 'opacity-25 grayscale',
          ]"
        />
      </div>

      <!-- 3 sub-rows of 3 -->
      <div
        v-for="(row, rowIndex) in primary.subRows"
        :key="`prow-${rowIndex}`"
        class="flex gap-4"
      >
        <NuxtImg
          v-for="id in row"
          :key="`pp-${rowIndex}-${id}`"
          :src="perkIcon(id)"
          :alt="perkName(id)"
          :title="perkName(id)"
          width="40"
          height="40"
          :class="[
            'size-10 rounded-full transition',
            selectedPrimary.has(id) ? '' : 'opacity-25 grayscale',
          ]"
        />
      </div>
    </section>

    <!-- Right column: secondary on top + shards below -->
    <div class="flex flex-col gap-8">
      <!-- Secondary tree (3 rows of 3, no keystone) -->
      <section
        v-if="secondary"
        class="flex flex-col items-center gap-4"
      >
        <div
          v-for="(row, rowIndex) in secondary.subRows"
          :key="`srow-${rowIndex}`"
          class="flex gap-3"
        >
          <NuxtImg
            v-for="id in row"
            :key="`sp-${rowIndex}-${id}`"
            :src="perkIcon(id)"
            :alt="perkName(id)"
            :title="perkName(id)"
            width="36"
            height="36"
            :class="[
              'size-9 rounded-full transition',
              selectedSecondary.has(id) ? '' : 'opacity-25 grayscale',
            ]"
          />
        </div>
      </section>

      <!-- Stat shards -->
      <section class="flex flex-col items-center gap-2">
        <div
          v-for="(row, rowIndex) in tree.shardSlots"
          :key="`shard-row-${rowIndex}`"
          class="flex gap-3"
        >
          <NuxtImg
            v-for="id in row"
            :key="`shard-${rowIndex}-${id}`"
            :src="perkIcon(id)"
            :alt="perkName(id)"
            :title="perkName(id)"
            width="28"
            height="28"
            :class="[
              'size-7 rounded-full transition',
              selectedShards[rowIndex] === id
                ? 'ring-2 ring-primary'
                : 'opacity-25 grayscale',
            ]"
          />
        </div>
      </section>
    </div>
  </div>
</template>
