<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'

defineProps<{
  champions: ChampionStaticListItem[]
  position: ChampionPosition | null
  championId: number | null
}>()

const emit = defineEmits<{
  'update:position': [value: ChampionPosition | null]
  'update:championId': [value: number | null]
}>()
</script>

<template>
  <!--
    Two sibling roots (no wrapper div) so they become direct flex children
    of the header's justify-between row: the champion search sits in the
    middle and the position picker pins to the right. ChampionPicker uses
    the same placeholder + width as the /champions and leaderboard filter
    strips so the bars feel identical. Each picker self-resets (the
    champion picker's inline ✕, the position picker's "All positions"), so
    no extra Clear control is needed.
  -->
  <ChampionPicker
    :champions="champions"
    :champion-id="championId"
    placeholder="Search for a champion"
    trigger-class="w-64"
    @update:champion-id="value => emit('update:championId', value)"
  />

  <RolePicker
    :position="position"
    @update:position="value => emit('update:position', value)"
  />
</template>
