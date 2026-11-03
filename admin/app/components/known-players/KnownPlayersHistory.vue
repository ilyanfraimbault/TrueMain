<script setup lang="ts">
// A known player's own trail in the edit panel (#2288): the operator actions that
// targeted it, newest first, from the same endpoint as the Audit page. `version`
// moves after each edit the panel makes, so the new action shows up at once.
import type { AuditEvent } from '~~/shared/types/ops'
import { ref, watch } from 'vue'

const props = defineProps<{ personaId: string, version: number }>()

const PAGE_SIZE = 20
const events = ref<AuditEvent[]>([])
const total = ref(0)
const pending = ref(false)
const error = ref<unknown>(null)

async function load() {
  pending.value = true
  error.value = null
  try {
    const page = await getAuditEvents({ targetType: 'Persona', targetId: props.personaId, pageSize: PAGE_SIZE })
    events.value = page.entries
    total.value = page.total
  }
  catch (e) {
    error.value = e
  }
  finally {
    pending.value = false
  }
}

watch(() => [props.personaId, props.version], load, { immediate: true })
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
    <FetchErrorAlert v-if="error" :error="error" title="Failed to load this player's history" />
    <AuditEventList v-else :events="events" :pending="pending" :show-target="false" empty-text="No operator action on this known player yet." />
  </section>
</template>
