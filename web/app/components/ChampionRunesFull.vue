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

function styleIcon(id: number): string {
  return props.tree.perkStyles[id]?.iconUrl ?? ''
}
</script>

<template>
  <div class="flex flex-wrap items-start gap-x-6 gap-y-4">
    <!-- Primary tree (left) -->
    <section
      v-if="primary"
      class="flex flex-col items-center gap-2"
    >
      <!-- Style icon (silent tree identifier, no text) -->
      <NuxtImg
        :src="styleIcon(primary.styleId)"
        :alt="primary.name"
        :title="primary.name"
        width="16"
        height="16"
        class="size-4"
      />

      <!-- Keystone row (3-4 options, larger to read as "the keystone slot") -->
      <div class="flex items-center gap-2">
        <NuxtImg
          v-for="id in primary.keystones"
          :key="`pk-${id}`"
          :src="perkIcon(id)"
          :alt="perkName(id)"
          :title="perkName(id)"
          width="40"
          height="40"
          :class="[
            'size-10 rounded-full transition',
            id === page.primaryKeystoneId ? '' : 'opacity-40 grayscale',
          ]"
        />
      </div>

      <!-- 3 sub-rows of 3 -->
      <div
        v-for="(row, rowIndex) in primary.subRows"
        :key="`prow-${rowIndex}`"
        class="flex items-center gap-2"
      >
        <NuxtImg
          v-for="id in row"
          :key="`pp-${rowIndex}-${id}`"
          :src="perkIcon(id)"
          :alt="perkName(id)"
          :title="perkName(id)"
          width="28"
          height="28"
          :class="[
            'size-7 rounded-full transition',
            selectedPrimary.has(id) ? '' : 'opacity-40 grayscale',
          ]"
        />
      </div>
    </section>

    <!-- Right column: secondary on top + shards below -->
    <div class="flex flex-col gap-4">
      <!-- Secondary tree (3 rows of 3, no keystone) -->
      <section
        v-if="secondary"
        class="flex flex-col items-center gap-2"
      >
        <NuxtImg
          :src="styleIcon(secondary.styleId)"
          :alt="secondary.name"
          :title="secondary.name"
          width="16"
          height="16"
          class="size-4"
        />

        <div
          v-for="(row, rowIndex) in secondary.subRows"
          :key="`srow-${rowIndex}`"
          class="flex items-center gap-2"
        >
          <NuxtImg
            v-for="id in row"
            :key="`sp-${rowIndex}-${id}`"
            :src="perkIcon(id)"
            :alt="perkName(id)"
            :title="perkName(id)"
            width="24"
            height="24"
            :class="[
              'size-6 rounded-full transition',
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
          class="flex items-center gap-2"
        >
          <NuxtImg
            v-for="id in row"
            :key="`shard-${rowIndex}-${id}`"
            :src="perkIcon(id)"
            :alt="perkName(id)"
            :title="perkName(id)"
            width="20"
            height="20"
            :class="[
              'size-5 rounded-full transition',
              selectedShards[rowIndex] === id ? '' : 'opacity-40 grayscale',
            ]"
          />
        </div>
      </section>
    </div>
  </div>
</template>
