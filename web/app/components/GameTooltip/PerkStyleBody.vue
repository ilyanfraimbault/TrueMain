<script setup lang="ts">
import type { StaticPerkStyleData } from '~~/shared/types/static-data'

defineProps<{
  perkStyle: StaticPerkStyleData
}>()
</script>

<template>
  <div>
    <header
      class="flex items-center gap-3"
      :class="{ 'mb-2': perkStyle.tooltip }"
    >
      <SkeletonImage
        :src="perkStyle.iconUrl"
        :alt="perkStyle.name"
        :width="36"
        :height="36"
        class="size-9 shrink-0"
      />
      <div>
        <div class="font-semibold text-default">
          {{ perkStyle.name }}
        </div>
        <div class="text-xs text-muted">
          Rune path
        </div>
      </div>
    </header>
    <p
      v-if="perkStyle.tooltip"
      class="border-t border-default/40 pt-2 text-sm leading-relaxed"
    >
      {{ perkStyle.tooltip }}
    </p>
  </div>
</template>
