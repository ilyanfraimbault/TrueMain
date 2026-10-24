<script setup lang="ts">
// The known-players list's rows (#2019): who, from which source, how many accounts
// are tracked or not yet, links, open conflicts, last change. Presentational — the
// container fetches, pages and opens the edit panel.
import type { PersonaListRow } from '~~/shared/types/ops'
import { formatNumber, formatTimeAgo } from '~~/shared/utils/format'
import { accountsSummary } from '~~/shared/utils/known-players'

defineProps<{
  personas: PersonaListRow[]
  pending: boolean
  emptyText: string
}>()

const emit = defineEmits<{ open: [persona: PersonaListRow] }>()
</script>

<template>
  <div class="overflow-x-auto">
    <table class="w-full text-sm">
      <thead>
        <tr class="text-left text-xs uppercase text-muted border-b border-default">
          <th class="px-4 py-2 font-medium">
            Known player
          </th>
          <th class="px-4 py-2 font-medium">
            Accounts
          </th>
          <th class="px-4 py-2 font-medium">
            Links
          </th>
          <th class="px-4 py-2 font-medium">
            Conflicts
          </th>
          <th class="px-4 py-2 font-medium">
            Updated
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="persona in personas"
          :key="persona.id"
          data-row
          tabindex="0"
          :aria-label="`Open ${persona.displayName}`"
          class="border-b border-default last:border-0 cursor-pointer hover:bg-elevated/40 focus-visible:outline-2 focus-visible:outline-primary"
          @click="emit('open', persona)"
          @keydown.enter.prevent="emit('open', persona)"
          @keydown.space.prevent="emit('open', persona)"
        >
          <td class="px-4 py-2">
            <p class="font-medium text-highlighted">
              {{ persona.displayName }}
              <UIcon v-if="persona.displayNameLocked" name="i-lucide-lock" class="size-3 text-dimmed align-middle" aria-label="Name set by an operator" />
              <span class="font-mono text-xs text-dimmed ml-1">{{ persona.slug }}</span>
            </p>
            <p class="text-xs text-muted">
              from {{ persona.source }}
              <UBadge v-if="persona.removedAtUtc" data-removed color="error" variant="subtle" size="sm" label="Removed" class="ml-1" />
            </p>
          </td>
          <td class="px-4 py-2 text-muted" :class="persona.unresolvedAccounts > 0 ? 'text-warning' : ''">
            {{ accountsSummary(persona) }}
          </td>
          <td class="px-4 py-2 tabular-nums text-muted">
            {{ formatNumber(persona.links) }}
          </td>
          <td class="px-4 py-2">
            <UBadge v-if="persona.openConflicts > 0" data-conflicts color="warning" variant="subtle" size="sm" :label="`${formatNumber(persona.openConflicts)} open`" />
            <span v-else class="text-dimmed">—</span>
          </td>
          <td class="px-4 py-2 whitespace-nowrap text-xs text-muted">
            {{ formatTimeAgo(persona.updatedAtUtc) }}
          </td>
        </tr>
      </tbody>
    </table>
    <p v-if="!pending && personas.length === 0" data-empty class="py-10 text-center text-sm text-muted">
      {{ emptyText }}
    </p>
  </div>
</template>
