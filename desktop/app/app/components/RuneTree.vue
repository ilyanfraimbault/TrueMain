<script setup lang="ts">
import type { BuildRunePage } from '~/types/build'
import { SHARD_SLOTS } from '~/composables/useBuildStatics'

/**
 * The rune page as the site's core view draws it: both trees in full, the
 * picked runes lit and the rest stepped back, so the page reads like the
 * client's own editor.
 */
const props = defineProps<{ page: BuildRunePage }>()

const { perk, runeStyle } = useBuildStatics()

const primary = computed(() => runeStyle(props.page.primaryStyleId))
const secondary = computed(() => runeStyle(props.page.secondaryStyleId))

const picked = computed(() => new Set([
  props.page.primaryKeystoneId,
  props.page.primaryPerk1Id,
  props.page.primaryPerk2Id,
  props.page.primaryPerk3Id,
  props.page.secondaryPerk1Id,
  props.page.secondaryPerk2Id,
]))
const shards = computed(() => [props.page.statOffense, props.page.statFlex, props.page.statDefense])

const state = (on: boolean) => (on ? 'selected-perk' : 'deselected')
</script>

<template>
  <div class="flex items-stretch gap-6">
    <section v-if="primary" class="flex flex-col items-center gap-1">
      <div class="flex items-center gap-0.5">
        <GameIcon
          v-for="id in primary.keystones"
          :key="id"
          :source="perk(id)"
          size="size-[39px]"
          round
          class="transition"
          :class="state(id === page.primaryKeystoneId)"
        />
      </div>
      <div v-for="(row, index) in primary.subRows" :key="index" class="flex items-center gap-1">
        <GameIcon
          v-for="id in row"
          :key="id"
          :source="perk(id)"
          size="size-7"
          round
          class="transition"
          :class="state(picked.has(id))"
        />
      </div>
    </section>

    <div class="flex flex-col justify-between">
      <section v-if="secondary" class="flex flex-col items-center gap-1">
        <div v-for="(row, index) in secondary.subRows" :key="index" class="flex items-center gap-1">
          <GameIcon
            v-for="id in row"
            :key="id"
            :source="perk(id)"
            size="size-6"
            round
            class="transition"
            :class="state(picked.has(id))"
          />
        </div>
      </section>
      <section class="flex flex-col items-center gap-1">
        <div v-for="(row, index) in SHARD_SLOTS" :key="index" class="flex items-center gap-1">
          <GameIcon
            v-for="id in row"
            :key="id"
            :source="perk(id)"
            size="size-5"
            round
            class="bg-ink-950 transition"
            :class="state(shards[index] === id)"
          />
        </div>
      </section>
    </div>
  </div>
</template>
