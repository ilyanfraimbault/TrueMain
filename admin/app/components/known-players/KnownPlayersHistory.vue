<script setup lang="ts">
// A known player's own trail in the edit panel (#2288): the operator actions that
// targeted it, newest first, from the same endpoint as the Audit page. `version`
// moves after each edit the panel makes, so the new action shows up at once.
import type { AuditEventsReadModel } from '#site-shared/contract/api'
import { computed, watch } from 'vue'
import { useKeyedFetch } from '~/composables/useKeyedFetch'
import { panelPhase } from '~/composables/useLastGoodPayload'

const props = defineProps<{ personaId: string, version: number }>()

const PAGE_SIZE = 20

// Keyed by the player (#2471): a ⌘K jump drops the previous player's trail at once,
// and its late answer never lands here; a reload after an edit keeps the trail drawn.
const { state, value: page, load } = useKeyedFetch<string, AuditEventsReadModel>(
  personaId => getAuditEvents({ targetType: 'Persona', targetId: personaId, pageSize: PAGE_SIZE }),
)
watch(() => [props.personaId, props.version] as const, ([personaId]) => load(personaId), { immediate: true })

const events = computed(() => page.value?.entries ?? [])
const total = computed(() => page.value?.total ?? 0)
const error = computed(() => state.value.s === 'error' ? state.value.error : null)
const phase = computed(() => panelPhase({ hasData: page.value !== null, pending: state.value.s === 'loading', error: error.value }))
</script>

<template>
  <section data-history>
    <div class="flex items-center gap-2 mb-1">
      <p class="text-xs uppercase text-muted">
        History<template v-if="total > 0">
          · {{ total }}
        </template>
      </p>
      <ULink
        v-if="total > events.length"
        :to="`/audit?targetType=Persona&targetId=${encodeURIComponent(personaId)}`"
        class="ml-auto text-xs text-primary"
        data-history-all
      >
        All {{ total }} on the Audit page
      </ULink>
    </div>
    <FetchErrorAlert v-if="phase === 'failed'" :error="error" title="Failed to load this player's history" />
    <template v-else>
      <p v-if="phase === 'stale'" class="text-xs text-muted mb-1">
        The last reload failed; showing the trail as it was.
      </p>
      <AuditEventList :events="events" :pending="phase === 'loading'" :show-target="false" empty-text="No operator action on this known player yet." />
    </template>
  </section>
</template>
