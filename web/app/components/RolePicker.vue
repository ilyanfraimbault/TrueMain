<script setup lang="ts">
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'

// Segmented picker: an "All positions" button followed by the five Riot
// positions. Same look-and-feel as the /champions filter strip so the
// leaderboard and the champion list feel like one app. Selected state
// uses `color="neutral" variant="soft"`: the brand accent stays on the page's
// primary controls, and a strip of six buttons is not one of them.
//
// `hideAll` drops the leading "All positions" button — used on the
// champion detail page where the API always returns data for a specific
// position, so "no filter" has no meaningful UI state.
//
// `exclude` removes one position from the strip. The synergies panel uses it
// for the champion's own lane: a team fields one player per lane, so offering
// it would be a button that can only ever return an empty list.
const props = withDefaults(defineProps<{
  position: ChampionPosition | null
  hideAll?: boolean
  exclude?: ChampionPosition | null
}>(), { hideAll: false, exclude: null })

const emit = defineEmits<{
  'update:position': [value: ChampionPosition | null]
}>()

const FILL_ICON_URL = getPositionIconUrl('fill')

const options = computed(() =>
  POSITION_OPTIONS.filter(option => option.value !== props.exclude),
)

function select(value: ChampionPosition | null) {
  emit('update:position', value)
}
</script>

<template>
  <!-- Same surface as the USelect `outline` trigger (bg-default + accented
       inset ring) so the segmented control reads as one control among the
       filter selects instead of a bare transparent button strip. -->
  <UFieldGroup size="md" class="rounded-md bg-default ring ring-inset ring-accented">
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
      v-for="option in options"
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
