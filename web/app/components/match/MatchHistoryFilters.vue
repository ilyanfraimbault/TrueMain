<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'

const props = defineProps<{
  champions: ChampionStaticListItem[]
  position: ChampionPosition | null
  championId: number | null
}>()

const emit = defineEmits<{
  'update:position': [value: ChampionPosition | null]
  'update:championId': [value: number | null]
}>()

function clearAll() {
  emit('update:position', null)
  emit('update:championId', null)
}

const hasAnyFilter = computed(() => props.position !== null || props.championId !== null)
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <RolePicker
      :position="position"
      @update:position="value => emit('update:position', value)"
    />

    <ChampionPicker
      :champions="champions"
      :champion-id="championId"
      @update:champion-id="value => emit('update:championId', value)"
    />

    <UButton
      v-if="hasAnyFilter"
      variant="ghost"
      color="neutral"
      size="xs"
      icon="i-lucide-x"
      @click="clearAll"
    >
      Clear
    </UButton>
  </div>
</template>
