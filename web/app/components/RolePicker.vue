<script setup lang="ts">
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'

// Segmented picker: an "All positions" button followed by the five Riot
// positions. Same look-and-feel as the /champions filter strip so the
// leaderboard and the champion list feel like one app. Selected state
// uses `color="neutral" variant="soft"` — keeps the emerald accent out of
// the segmented control where it would compete with the rest of the UI.
//
// `hideAll` drops the leading "All positions" button — used on the
// champion detail page where the API always returns data for a specific
// position, so "no filter" has no meaningful UI state.
const props = withDefaults(defineProps<{
  position: ChampionPosition | null
  hideAll?: boolean
}>(), { hideAll: false })

const emit = defineEmits<{
  'update:position': [value: ChampionPosition | null]
}>()

const FILL_ICON_URL = getPositionIconUrl('fill')

function select(value: ChampionPosition | null) {
  emit('update:position', value)
}
</script>

<template>
  <UFieldGroup size="md">
    <UButton
      v-if="!hideAll"
      :variant="position === null ? 'soft' : 'ghost'"
      color="neutral"
      square
      aria-label="All positions"
      @click="select(null)"
    >
      <SkeletonImage
        :src="FILL_ICON_URL"
        alt="All positions"
        :width="18"
        :height="18"
        class="size-[18px]"
      />
    </UButton>
    <UButton
      v-for="option in POSITION_OPTIONS"
      :key="option.value"
      :variant="position === option.value ? 'soft' : 'ghost'"
      color="neutral"
      square
      :aria-label="option.label"
      @click="select(option.value)"
    >
      <SkeletonImage
        :src="option.iconUrl"
        :alt="option.label"
        :width="18"
        :height="18"
        class="size-[18px]"
      />
    </UButton>
  </UFieldGroup>
</template>
