<script setup lang="ts">
// The operator-action trail as a timeline (#2288): when, who, what in plain words,
// the target as a link to where it lives, and the context as chips. Shared by the
// Audit page and the known-player History, which hides the target (it is the player).
import type { AuditEvent } from '~~/shared/types/ops'
import { computed } from 'vue'
import { auditChips, auditTarget } from '~~/shared/utils/audit'
import { formatDateTime, formatTimeAgo } from '~~/shared/utils/format'

const props = withDefaults(defineProps<{
  events: AuditEvent[]
  pending: boolean
  emptyText: string
  showTarget?: boolean
}>(), { showTarget: true })

const rows = computed(() => props.events.map(event => ({
  event,
  target: auditTarget(event),
  chips: auditChips(event),
})))
</script>

<template>
  <div>
    <ul v-if="rows.length > 0" class="divide-y divide-default">
      <li v-for="{ event, target, chips } in rows" :key="event.id" data-event class="flex flex-wrap items-baseline gap-x-3 gap-y-1 py-2 text-sm">
        <span class="w-36 shrink-0 text-xs text-muted tabular-nums" :title="formatTimeAgo(event.timestampUtc)">
          {{ formatDateTime(event.timestampUtc) }}
        </span>
        <UBadge :label="event.actor" color="neutral" variant="subtle" size="sm" data-actor />
        <span class="text-highlighted" data-label>{{ event.label }}</span>
        <template v-if="showTarget && target.label">
          <ULink v-if="target.to" :to="target.to" class="font-mono text-xs text-primary" data-target>
            {{ target.label }}
          </ULink>
          <span v-else class="font-mono text-xs text-muted" data-target>{{ target.label }}</span>
        </template>
        <span v-if="chips.length > 0" class="flex flex-wrap gap-1 basis-full sm:basis-auto">
          <span v-for="chip in chips" :key="chip.key" data-chip class="rounded bg-elevated px-1.5 py-0.5 text-xs">
            <span class="text-dimmed">{{ chip.key }}</span>
            <span class="ml-1 text-default break-all">{{ chip.value }}</span>
          </span>
        </span>
      </li>
    </ul>
    <p v-else-if="!pending" data-empty class="py-6 text-center text-sm text-muted">
      {{ emptyText }}
    </p>
  </div>
</template>
