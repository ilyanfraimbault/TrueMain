<script setup lang="ts">
// Channel conflicts (#2286): two known players listing the same social channel —
// nobody else streams on it or posts from it — with no account in common, so the
// account queue above cannot see them. Same-name ones are merged by the intake; these
// are the rest. The operator compares both and merges them (into either, asked
// twice: a page retires) or marks them as different people. Either way they leave:
// the list is derived from the links, not stored.
import type { PersonaChannelConflict } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'
import { resolveErrorMessage } from '~~/shared/utils/persona-conflicts'
import { linkHandle, markedMessage, mergedMessage } from '~~/shared/utils/persona-homonyms'

const props = defineProps<{
  /** The conflict queue's search, applied to either known player's name or slug. */
  search?: string
  /** Moved by the queue when it reloads: this list reloads with it. */
  stamp?: number
}>()

const emit = defineEmits<{ total: [count: number] }>()

const page = ref(1)
const pageSize = 25
watch(() => props.search, () => {
  page.value = 1
})

const filters = computed(() => ({ search: props.search || undefined, page: page.value, pageSize }))
const { data, pending, error, refresh } = usePersonaChannelConflicts(filters)
const conflicts = computed(() => data.value?.conflicts ?? [])
const total = computed(() => data.value?.total ?? 0)
watch(total, count => emit('total', count), { immediate: true })
watch(() => props.stamp, () => refresh())

const selected = ref<PersonaChannelConflict | null>(null)
const detailOpen = ref(false)
const armed = ref<'older' | 'newer' | null>(null)
const busy = ref(false)
const toast = useActionToast()

function open(conflict: PersonaChannelConflict) {
  selected.value = conflict
  armed.value = null
  detailOpen.value = true
}

async function act(run: () => Promise<{ title: string, description?: string }>, failure: string) {
  busy.value = true
  try {
    const message = await run()
    toast.success(message.title, message.description)
    detailOpen.value = false
  }
  catch (e) {
    toast.failure(failure, resolveErrorMessage(e))
  }
  finally {
    busy.value = false
    armed.value = null
    refresh()
  }
}

function merge(keep: 'older' | 'newer') {
  const conflict = selected.value
  if (!conflict || busy.value) {
    return
  }

  if (armed.value !== keep) {
    armed.value = keep
    return
  }

  const [kept, merged] = keep === 'older' ? [conflict.older, conflict.newer] : [conflict.newer, conflict.older]
  return act(async () => mergedMessage(await mergePersonaHomonyms({ keepId: kept.id, mergeIds: [merged.id] })), 'Not merged')
}

function distinct() {
  const conflict = selected.value
  if (conflict && !busy.value) {
    return act(async () => markedMessage(await markPersonaHomonymsDistinct({ personaIds: [conflict.older.id, conflict.newer.id] })), 'Not marked')
  }
}

</script>

<template>
  <section>
    <FetchErrorAlert v-if="error" :error="error" title="Failed to load the channel conflicts" class="mb-4" />

    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium text-highlighted">
            Shared channels
          </p>
          <UBadge v-if="!pending" color="neutral" variant="subtle" :label="`${formatNumber(total)} ${total === 1 ? 'conflict' : 'conflicts'}`" />
        </div>
      </template>

      <div class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-left text-xs uppercase text-muted border-b border-default">
              <th class="px-4 py-2 font-medium">
                Known player
              </th>
              <th class="px-4 py-2 font-medium">
                Also listing it
              </th>
              <th class="px-4 py-2 font-medium">
                Channels in common
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="conflict in conflicts"
              :key="`${conflict.older.id}:${conflict.newer.id}`"
              data-channel-row
              tabindex="0"
              :aria-label="`Open the channel conflict between ${conflict.older.slug} and ${conflict.newer.slug}`"
              class="border-b border-default last:border-0 cursor-pointer hover:bg-elevated/40 focus-visible:outline-2 focus-visible:outline-primary"
              @click="open(conflict)"
              @keydown.enter.prevent="open(conflict)"
              @keydown.space.prevent="open(conflict)"
            >
              <td v-for="side in [conflict.older, conflict.newer]" :key="side.id" class="px-4 py-2">
                <p class="text-highlighted">
                  {{ side.displayName }} <span class="font-mono text-xs text-dimmed">{{ side.slug }}</span>
                </p>
                <p class="text-xs text-muted">
                  {{ side.source }} · {{ side.accounts.length }} {{ side.accounts.length === 1 ? 'account' : 'accounts' }}
                </p>
              </td>
              <td class="px-4 py-2">
                <div class="flex flex-wrap gap-1">
                  <UBadge
                    v-for="channel in conflict.sharedChannels"
                    :key="channel.url"
                    color="success"
                    variant="subtle"
                    size="sm"
                    :label="`${channel.kind} · ${linkHandle(channel.url)}`"
                  />
                </div>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-if="!pending && conflicts.length === 0" data-empty class="py-10 text-center text-sm text-muted">
          {{ search ? 'No channel conflict matches this search.' : 'No two known players list the same channel.' }}
        </p>
      </div>

      <div v-if="total > pageSize" class="flex justify-end border-t border-default px-4 py-3">
        <UPagination v-model:page="page" :total="total" :items-per-page="pageSize" :sibling-count="1" active-color="primary" variant="subtle" :disabled="pending" show-edges />
      </div>
    </UCard>

    <USlideover
      v-model:open="detailOpen"
      :title="selected ? `${selected.older.displayName} · ${selected.newer.displayName}` : 'Channel conflict'"
      :description="selected ? `${selected.sharedChannels.map(c => c.kind).join(', ')} in common` : ''"
      :ui="{ content: 'sm:max-w-3xl' }"
    >
      <template #body>
        <div v-if="selected" class="space-y-6">
          <KnownPlayersConflictCompare
            :claimant="selected.older"
            :owner="selected.newer"
            :labels="[`Created ${formatDateTime(selected.older.createdAtUtc)}`, `Created ${formatDateTime(selected.newer.createdAtUtc)}`]"
          />
          <USeparator />
          <div class="flex flex-wrap items-center gap-2">
            <UButton
              v-for="keep in (['older', 'newer'] as const)"
              :key="keep"
              :data-action="`merge-${keep}`"
              icon="i-lucide-merge"
              color="primary"
              :variant="armed === keep ? 'solid' : 'subtle'"
              :disabled="busy"
              :label="`${armed === keep ? 'Confirm: merge' : 'Merge'} into ${selected[keep].slug}`"
              @click="merge(keep)"
            />
            <UButton
              data-action="distinct"
              icon="i-lucide-users"
              color="neutral"
              variant="subtle"
              :disabled="busy"
              label="Different people"
              @click="distinct()"
            />
          </div>
          <p class="text-xs text-muted">
            {{ armed ? 'Accounts and links join the kept one; the other page redirects there.' : 'A merge keeps one page and redirects the other; different people stay apart for good.' }}
          </p>
        </div>
      </template>
    </USlideover>
  </section>
</template>
