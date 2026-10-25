<script setup lang="ts">
// The conflict queue's rows (#2253): the disputed account, the known player whose
// source claims it, the one holding it, what their links have in common — the open
// queue is ordered by it, most certain first (#2277) — how often the claim came back.
// Presentational — the container fetches, pages and opens the detail.
import type { PersonaConflict } from '~~/shared/types/ops'
import { formatNumber, formatTimeAgo } from '~~/shared/utils/format'
import { accountCount, renamedTo, resolutionColor, resolutionLabel, riotId } from '~~/shared/utils/persona-conflicts'
import { evidenceBadges } from '~~/shared/utils/persona-evidence'

defineProps<{
  conflicts: PersonaConflict[]
  pending: boolean
  emptyText: string
}>()

const emit = defineEmits<{ open: [conflict: PersonaConflict] }>()
</script>

<template>
  <div class="overflow-x-auto">
    <table class="w-full text-sm">
      <thead>
        <tr class="text-left text-xs uppercase text-muted border-b border-default">
          <th class="px-4 py-2 font-medium">
            Account
          </th>
          <th class="px-4 py-2 font-medium">
            Claimed by
          </th>
          <th class="px-4 py-2 font-medium">
            Held by
          </th>
          <th class="px-4 py-2 font-medium">
            In common
          </th>
          <th class="px-4 py-2 font-medium">
            Seen
          </th>
          <th class="px-4 py-2 font-medium">
            Status
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="conflict in conflicts"
          :key="conflict.id"
          data-row
          tabindex="0"
          :aria-label="`Open the conflict over ${riotId(conflict.account)}`"
          class="border-b border-default last:border-0 cursor-pointer hover:bg-elevated/40 focus-visible:outline-2 focus-visible:outline-primary"
          @click="emit('open', conflict)"
          @keydown.enter.prevent="emit('open', conflict)"
          @keydown.space.prevent="emit('open', conflict)"
        >
          <td class="px-4 py-2">
            <p class="font-medium text-highlighted">
              {{ riotId(conflict.account) }}
              <span class="text-muted font-mono text-xs ml-1">{{ conflict.account.platformId }}</span>
            </p>
            <p v-if="renamedTo(conflict.account)" class="text-xs text-dimmed">
              now {{ renamedTo(conflict.account) }}
            </p>
            <p v-else-if="!conflict.account.riotAccountId" class="text-xs text-dimmed">
              not tracked
            </p>
          </td>
          <td class="px-4 py-2">
            <p class="text-highlighted">
              {{ conflict.claimant.displayName }} <span class="font-mono text-xs text-dimmed">{{ conflict.claimant.slug }}</span>
            </p>
            <p class="text-xs text-muted">
              {{ conflict.source }} · {{ accountCount(conflict.claimant.accountCount) }}
            </p>
          </td>
          <td class="px-4 py-2">
            <p class="text-highlighted">
              {{ conflict.owner.displayName }} <span class="font-mono text-xs text-dimmed">{{ conflict.owner.slug }}</span>
            </p>
            <p class="text-xs text-muted">
              {{ conflict.owner.source }} · {{ accountCount(conflict.owner.accountCount) }}
            </p>
          </td>
          <td class="px-4 py-2">
            <div data-evidence class="flex flex-wrap gap-1">
              <UBadge
                v-for="badge in evidenceBadges(conflict.evidence)"
                :key="badge.label"
                :color="badge.color"
                variant="subtle"
                size="sm"
                :label="badge.label"
              />
            </div>
          </td>
          <td class="px-4 py-2 whitespace-nowrap">
            <p class="tabular-nums">
              {{ formatNumber(conflict.timesSeen) }}×
            </p>
            <p class="text-xs text-muted">
              {{ formatTimeAgo(conflict.lastSeenAtUtc) }}
            </p>
          </td>
          <td class="px-4 py-2">
            <UBadge
              :color="resolutionColor(conflict.resolution)"
              variant="subtle"
              size="sm"
              :label="resolutionLabel(conflict.resolution)"
            />
          </td>
        </tr>
      </tbody>
    </table>
    <p v-if="!pending && conflicts.length === 0" data-empty class="py-10 text-center text-sm text-muted">
      {{ emptyText }}
    </p>
  </div>
</template>
