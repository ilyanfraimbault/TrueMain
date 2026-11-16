<script setup lang="ts">
// "Resolve the certain ones" (#2295): pick a score floor, read every conflict it
// would touch and the merge it would get — into the older known player — then
// confirm twice, like every merge. What is sent is the ids on screen, never the
// floor alone; the API re-checks each and answers what it did, shown in place.
import type { PersonaConflictBulkPreviewReadModel, PersonaConflictBulkResolveResponse } from '#site-shared/contract/api'
import { computed, ref, watch } from 'vue'
import { formatNumber } from '~~/shared/utils/format'
import { BULK_FLOORS, DEFAULT_BULK_FLOOR, bulkMergeLine, bulkResolvedMessage, skipReasonLabel } from '~~/shared/utils/persona-conflict-bulk'
import { resolveErrorMessage, riotId } from '~~/shared/utils/persona-conflicts'
import { evidenceBadges } from '~~/shared/utils/persona-evidence'
import { useArmedConfirm } from '~/composables/useArmedConfirm'
import { useKeyedFetch } from '~/composables/useKeyedFetch'

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ resolved: [] }>()

const floorItems = [...BULK_FLOORS]
const floor = ref<number>(DEFAULT_BULK_FLOOR)
// The preview is keyed by the floor it was asked for (#2471): only the answer for
// the floor on screen is ever drawn — or sent. A superseded request, failed or not,
// writes nothing.
const { state: previewState, load, reset } = useKeyedFetch<number, PersonaConflictBulkPreviewReadModel>(asked => previewBulkResolve(asked))
const preview = computed(() => {
  const current = previewState.value
  return current.s === 'ready' && current.id === floor.value ? current : null
})
const previewError = computed(() => previewState.value.s === 'error' ? previewState.value.error : null)
const loading = computed(() => previewState.value.s === 'loading')
const confirmation = useArmedConfirm<'bulk'>({ scope: preview })
const armed = computed(() => confirmation.armed.value === 'bulk')
const busy = computed(() => confirmation.sending.value === 'bulk')
const result = ref<PersonaConflictBulkResolveResponse | null>(null)
const toast = useActionToast()

const candidates = computed(() => preview.value?.value.conflicts ?? [])
const count = computed(() => candidates.value.length)
const byId = computed(() => new Map(candidates.value.map(c => [c.conflict.id, c])))

watch([open, floor], ([isOpen]) => {
  result.value = null
  if (isOpen) {
    load(floor.value)
  }
  else {
    reset()
  }
}, { immediate: true })

function submit() {
  // What is sent is the preview on screen and the floor IT was asked for, never the
  // select's current value: they are the same object, so they cannot disagree.
  const shown = preview.value
  if (!shown || shown.value.conflicts.length === 0) {
    return
  }
  return confirmation.run('bulk', async () => {
    try {
      result.value = await bulkResolvePersonaConflicts({ ids: shown.value.conflicts.map(c => c.conflict.id), minScore: shown.id })
      const message = bulkResolvedMessage(result.value)
      toast.success(message.title, message.description)
      emit('resolved')
    }
    catch (e) {
      toast.failure('Conflicts not resolved', resolveErrorMessage(e))
    }
  })
}

const confirmLabel = computed(() => {
  const what = `${formatNumber(count.value)} conflict${count.value === 1 ? '' : 's'}`
  return armed.value ? `Confirm: merge ${what}` : `Merge ${what}`
})
</script>

<template>
  <UModal
    v-model:open="open"
    title="Resolve the certain ones"
    description="Conflicts whose two known players list the same social channel and no differing one, merged into the older of the two."
    :ui="{ content: 'sm:max-w-3xl' }"
  >
    <template #body>
      <div class="space-y-4">
        <div class="flex flex-wrap items-center gap-3">
          <USelect v-model="floor" data-floor :items="floorItems" icon="i-lucide-gauge" class="w-40" :disabled="busy || Boolean(result)" />
          <p v-if="preview && !result" data-summary class="text-sm text-muted">
            {{ formatNumber(preview.value.eligible) }} {{ preview.value.eligible === 1 ? 'conflict qualifies' : 'conflicts qualify' }}<template v-if="preview.value.eligible > count">
              — this batch takes the first {{ formatNumber(count) }}, the rest come with the next
            </template>.
          </p>
        </div>

        <p class="text-xs text-dimmed">
          10 per shared social channel, 3 per shared reference page. A conflict listing a differing channel is never part of a bulk action.
        </p>

        <FetchErrorAlert v-if="previewError" :error="previewError" title="Failed to load the preview" />

        <ul v-else-if="result" data-outcomes class="divide-y divide-default text-sm">
          <li v-for="outcome in result.outcomes" :key="outcome.id" data-outcome :data-status="outcome.status" class="flex items-center justify-between gap-3 py-2">
            <span class="text-highlighted">{{ byId.get(outcome.id) ? riotId(byId.get(outcome.id)!.conflict.account) : outcome.id }}</span>
            <UBadge
              :color="outcome.status === 'resolved' ? 'success' : 'warning'"
              variant="subtle"
              size="sm"
              :label="outcome.status === 'resolved' ? `Merged into ${outcome.resolution?.holderSlug}` : skipReasonLabel(outcome.reason)"
            />
          </li>
        </ul>

        <ul v-else-if="count > 0" data-preview class="divide-y divide-default text-sm max-h-96 overflow-y-auto">
          <li v-for="candidate in candidates" :key="candidate.conflict.id" data-candidate class="py-2">
            <div class="flex items-center justify-between gap-3">
              <p class="font-medium text-highlighted">
                {{ riotId(candidate.conflict.account) }}
                <span class="text-muted font-mono text-xs ml-1">{{ candidate.conflict.account.platformId }}</span>
              </p>
              <UBadge color="neutral" variant="subtle" size="sm" :label="`score ${candidate.conflict.evidence.score}`" />
            </div>
            <p class="text-xs text-muted">
              Merge {{ bulkMergeLine(candidate).merged }} into {{ bulkMergeLine(candidate).kept }}
              <span class="font-mono text-dimmed">/players/{{ candidate.mergedSlug }} → /players/{{ candidate.keptSlug }}</span>
            </p>
            <div class="mt-1 flex flex-wrap gap-1">
              <UBadge v-for="badge in evidenceBadges(candidate.conflict.evidence)" :key="badge.label" :color="badge.color" variant="subtle" size="sm" :label="badge.label" />
            </div>
          </li>
        </ul>

        <p v-else-if="!loading" data-empty class="py-6 text-center text-sm text-muted">
          No open conflict reaches this floor without a differing channel.
        </p>
      </div>
    </template>

    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton color="neutral" variant="ghost" :label="result ? 'Close' : 'Cancel'" @click="open = false" />
        <UButton
          v-if="!result"
          data-action="bulk-confirm"
          icon="i-lucide-merge"
          color="primary"
          :variant="armed ? 'solid' : 'subtle'"
          :label="confirmLabel"
          :loading="busy || loading"
          :disabled="busy || loading || count === 0"
          @click="submit()"
        />
      </div>
    </template>
  </UModal>
</template>
