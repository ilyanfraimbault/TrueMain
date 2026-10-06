<script setup lang="ts">
// Health verdict strip (#1031). The Overview stays the landing page, so the cockpit's
// one-line answer is surfaced there rather than making an operator navigate to find out
// whether anything is on fire. Deliberately the verdict and nothing else: the tiles and the
// signals live on /health.
//
// It is handed the last good verdict, not the raw fetch (#1427): Nuxt resets `data` on
// error, and a strip that blanked on every slow evaluation read as "nothing to report". A
// kept verdict states its age and that the latest refresh failed, so it is never read as live.
import type { PipelineHealth } from '~~/shared/types/ops'
import { detectorStatusMeta } from '~~/shared/utils/detector-status'
import { formatTimeAgo } from '~~/shared/utils/format'

const props = defineProps<{
  health: PipelineHealth | null
  pending: boolean
  failed: boolean
}>()

const verdict = computed(() => detectorStatusMeta(props.health?.status))
</script>

<template>
  <!-- Its own fetch, so a broken /ops/pipeline-health costs this strip and not the panel
       below it — and says so in place rather than rendering a healthy-looking blank. -->
  <NuxtLink
    v-if="pending || health || failed"
    to="/health"
    class="group block rounded-lg focus-visible:outline-2 focus-visible:outline-primary"
  >
    <UCard class="transition-colors group-hover:bg-elevated/50">
      <USkeleton v-if="pending && !health" class="h-6 w-72" />
      <div v-else class="flex items-center gap-3">
        <UIcon
          :name="verdict.icon"
          class="size-5 shrink-0"
          :class="health ? verdict.text : 'text-dimmed'"
        />
        <p class="text-sm grow min-w-0 truncate" :class="health ? 'text-highlighted' : 'text-dimmed italic'">
          {{ health ? health.headline : 'Pipeline health could not be loaded.' }}
        </p>
        <span v-if="health" class="text-xs shrink-0" :class="failed ? 'text-warning' : 'text-muted'">
          {{ failed ? 'refresh failed · ' : '' }}{{ formatTimeAgo(health.evaluatedAtUtc) }}
        </span>
        <UIcon
          name="i-lucide-arrow-up-right"
          class="size-4 shrink-0 text-dimmed group-hover:text-muted"
        />
      </div>
    </UCard>
  </NuxtLink>
</template>
