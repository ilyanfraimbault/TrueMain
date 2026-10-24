<script setup lang="ts">
// What holds a known player against its sources (#2019): the open conflicts its
// accounts are in — each linking to the queue to settle it — the accounts it may
// no longer claim and the links an operator removed. Presentational.
import type { PersonaAdminConflict, PersonaAdminExclusion, PersonaAdminRemovedLink } from '~~/shared/types/ops'
import { formatTimeAgo } from '~~/shared/utils/format'
import { conflictHref, exclusionReasonLabel } from '~~/shared/utils/known-players'

defineProps<{
  conflicts: PersonaAdminConflict[]
  exclusions: PersonaAdminExclusion[]
  removedLinks: PersonaAdminRemovedLink[]
}>()
</script>

<template>
  <div class="space-y-4">
    <section v-if="conflicts.length > 0" data-conflicts>
      <p class="text-xs uppercase text-muted mb-1">
        Open conflicts · {{ conflicts.length }}
      </p>
      <ul class="space-y-1 text-sm">
        <li v-for="conflict in conflicts" :key="conflict.id" class="flex items-center gap-2 rounded bg-warning/10 px-2 py-1">
          <span class="text-highlighted truncate">{{ conflict.gameName }}#{{ conflict.tagLine }}</span>
          <span class="font-mono text-xs text-muted">{{ conflict.platformId }}</span>
          <span class="text-xs text-muted truncate">
            {{ conflict.role === 'claimant' ? 'held by' : 'claimed by' }} {{ conflict.otherDisplayName }}
            <span class="font-mono text-dimmed">{{ conflict.otherSlug }}</span>
          </span>
          <ULink :to="conflictHref(conflict.id)" class="ml-auto text-xs text-primary shrink-0">
            Settle
          </ULink>
        </li>
      </ul>
    </section>

    <section v-if="exclusions.length > 0" data-exclusions>
      <p class="text-xs uppercase text-muted mb-1">
        May no longer claim · {{ exclusions.length }}
      </p>
      <ul class="space-y-1 text-sm">
        <li v-for="exclusion in exclusions" :key="`${exclusion.platformId}:${exclusion.gameName}#${exclusion.tagLine}`" class="flex items-center gap-2 px-2">
          <span class="text-muted truncate">{{ exclusion.gameName }}#{{ exclusion.tagLine }}</span>
          <span class="font-mono text-xs text-dimmed">{{ exclusion.platformId }}</span>
          <span class="ml-auto text-xs text-dimmed shrink-0">{{ exclusionReasonLabel(exclusion.reason) }} · {{ formatTimeAgo(exclusion.createdAtUtc) }}</span>
        </li>
      </ul>
      <p v-if="exclusions.some(e => e.reason !== 'takedown')" class="text-xs text-dimmed px-2 mt-1">
        Adding one of these accounts by hand lifts it, unless it was taken down.
      </p>
    </section>

    <section v-if="removedLinks.length > 0" data-removed-links>
      <p class="text-xs uppercase text-muted mb-1">
        Removed links · {{ removedLinks.length }}
      </p>
      <ul class="space-y-1 text-sm">
        <li v-for="link in removedLinks" :key="link.url" class="flex items-center gap-2 px-2">
          <span class="text-muted truncate line-through">{{ link.url }}</span>
          <span class="ml-auto text-xs text-dimmed shrink-0">{{ formatTimeAgo(link.createdAtUtc) }}</span>
        </li>
      </ul>
    </section>
  </div>
</template>
