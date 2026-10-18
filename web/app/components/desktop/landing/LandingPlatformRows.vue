<script setup lang="ts">
import type { DesktopPlatform, DesktopRelease } from '~~/shared/types/desktop'

/**
 * Both installers as rows at the foot of `/download` (#2072): what each runs
 * on and its download, the visitor's own first and marked as this computer.
 */
const props = defineProps<{ release: DesktopRelease | null | undefined, platform: DesktopPlatform | null }>()

const order = computed<DesktopPlatform[]>(() => (props.platform === 'windows' ? ['windows', 'mac'] : ['mac', 'windows']))
const available = (target: DesktopPlatform) => Boolean(props.release?.installers[target])
</script>

<template>
  <ul class="divide-y divide-default border-y border-default">
    <li v-for="target in order" :key="target" class="flex flex-wrap items-center gap-x-4 gap-y-2 py-4">
      <UIcon :name="DESKTOP_PLATFORMS[target].icon" class="size-5 shrink-0 text-highlighted" />
      <div class="min-w-0 flex-1">
        <p class="flex items-center gap-2 font-medium text-highlighted">
          {{ DESKTOP_PLATFORMS[target].label }}
          <UBadge v-if="target === platform" color="primary" variant="subtle" size="sm" label="This computer" />
        </p>
        <p class="text-sm text-muted">
          {{ DESKTOP_PLATFORMS[target].requirement }}
        </p>
      </div>
      <UButton
        :to="`/api/desktop/download/${target}`"
        external
        :color="target === platform ? 'primary' : 'neutral'"
        :variant="target === platform ? 'solid' : 'subtle'"
        icon="i-lucide-download"
        label="Download"
        :disabled="!available(target)"
      />
    </li>
  </ul>
</template>
