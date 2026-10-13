<script setup lang="ts">
import { championDisplayName } from '~~/shared/utils/champion-name'
import { ELO_BRACKET_ALL, eloBracketLabel } from '#common/utils/elo-brackets'

// The champion page's no-data-for-this-rank state: the picked rank has no games
// (the champion may well have builds in other ranks). The rank select stays
// visible so the reader can switch rank — a dead end otherwise — rather than
// silently showing all-ranks data under the selected rank.
defineProps<{
  championId: number
  displayName: string | null | undefined
  displayIconUrl: string | null | undefined
  eloBracket: string
}>()

const emit = defineEmits<{ 'update:eloBracket': [value: string] }>()
</script>

<template>
  <div class="space-y-6">
    <header class="flex flex-wrap items-center gap-3">
      <SkeletonImage
        v-if="displayIconUrl"
        :src="displayIconUrl"
        :alt="displayName ?? ''"
        width="48"
        height="48"
        class="size-12 rounded"
      />
      <h1 class="text-lg font-semibold text-default">
        {{ championDisplayName(championId, displayName) }}
      </h1>
    </header>

    <ChampionEloFilter
      :model-value="eloBracket"
      @update:model-value="value => emit('update:eloBracket', value)"
    />

    <UEmpty
      icon="i-lucide-medal"
      :title="`No ${displayName ?? 'champion'} games in ${eloBracketLabel(eloBracket)} yet`"
      description="Pick another rank above, or widen the slice to every rank."
      :actions="[{
        color: 'neutral',
        variant: 'subtle',
        size: 'sm',
        icon: 'i-lucide-layers',
        label: 'See all ranks',
        onClick: () => emit('update:eloBracket', ELO_BRACKET_ALL),
      }]"
    />
  </div>
</template>
