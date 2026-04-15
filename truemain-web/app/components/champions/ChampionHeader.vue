<script setup lang="ts">
import type { ChampionDisplayPosition, ChampionPosition } from '~/composables/useChampionPageStore'
import type { ChampionSummaryResponse } from '~/types/champions'
import type { ChampionStaticData } from '~/types/static-data'
import { formatPercentage } from '~/utils/items'

defineProps<{
  championStatic: ChampionStaticData
  summary: ChampionSummaryResponse
  patchOptions: Array<{ label: string, value: string }>
  positionOptions: Array<{ label: string, value: ChampionPosition, iconUrl: string }>
  selectedPatch: string
  displayPosition: ChampionDisplayPosition
}>()

const emit = defineEmits<{
  'update:patch': [patch: string]
  'update:position': [position: ChampionPosition]
}>()
</script>

<template>
  <header class="flex flex-col gap-6 md:flex-row md:items-start md:justify-between">
    <div class="flex items-center gap-5">
      <ChampionsChampionAsyncImage
        v-if="championStatic.championIconUrl"
        :src="championStatic.championIconUrl"
        :alt="championStatic.championName ?? ''"
        size-class="size-28 shrink-0"
        image-class="rounded-3xl border border-default shadow-sm"
        wrapper-class="rounded-3xl"
        width="112"
        height="112"
      />
      <div
        v-else
        class="size-28 shrink-0 rounded-3xl border border-default bg-elevated"
      />

      <div class="space-y-3">
        <h1
          v-if="championStatic.championName"
          class="text-3xl font-semibold tracking-tight"
        >
          {{ championStatic.championName }}
        </h1>

        <dl class="flex flex-wrap items-end gap-x-10 gap-y-3 text-sm">
          <div>
            <dt class="text-muted">
              Games
            </dt>
            <dd class="mt-1 text-2xl font-semibold tracking-tight">
              {{ summary.games }}
            </dd>
          </div>
          <div>
            <dt class="text-muted">
              Win rate
            </dt>
            <dd class="mt-1 text-2xl font-semibold tracking-tight">
              {{ formatPercentage(summary.winRate) }}
            </dd>
          </div>
        </dl>
      </div>
    </div>

    <div class="ml-auto flex w-full max-w-md flex-col items-end gap-4">
      <UFormField
        label="Patch"
        class="w-36"
      >
        <USelect
          :model-value="selectedPatch"
          :items="patchOptions"
          color="neutral"
          variant="subtle"
          class="w-36"
          @update:model-value="emit('update:patch', String($event))"
        />
      </UFormField>

      <div class="flex w-full justify-end">
        <UFieldGroup
          size="md"
          class="rounded-xl bg-elevated/40 p-1"
        >
          <UButton
            v-for="option in positionOptions"
            :key="option.value"
            type="button"
            color="neutral"
            square
            :variant="displayPosition === option.value ? 'soft' : 'ghost'"
            :title="option.label"
            :aria-label="option.label"
            @click="emit('update:position', option.value)"
          >
            <ChampionsChampionAsyncImage
              :src="option.iconUrl"
              :alt="option.label"
              size-class="size-5"
              image-class="object-contain"
              wrapper-class="rounded-sm"
              width="20"
              height="20"
            />
          </UButton>
        </UFieldGroup>
      </div>
    </div>
  </header>
</template>
