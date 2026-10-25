<script setup lang="ts">
// The homonyms queue (#2271): known players sharing a name with no account in
// common, which the conflict queue cannot see. Often one person two sources
// published under two slugs, sometimes two pros with one handle — only the
// operator can tell, and the call holds: a merged page redirects, a group marked
// as different people only comes back with a new known player of that name.
import type { PersonaHomonymDistinctBody, PersonaHomonymMergeBody } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'
import { formatNumber } from '~~/shared/utils/format'
import { resolveErrorMessage } from '~~/shared/utils/persona-conflicts'
import { markedMessage, mergedMessage } from '~~/shared/utils/persona-homonyms'

const searchFilter = ref('')
const searchDebounced = refDebounced(searchFilter, 300)
const page = ref(1)
const pageSize = 10

watch(searchDebounced, () => {
  page.value = 1
})

const filters = computed(() => ({
  search: searchDebounced.value.trim() || undefined,
  page: page.value,
  pageSize,
}))

const { data, pending, error, refresh } = usePersonaHomonyms(filters)
const groups = computed(() => data.value?.groups ?? [])
const total = computed(() => data.value?.total ?? 0)
const busyKey = ref<string | null>(null)
const toast = useActionToast()

async function act(key: string, run: () => Promise<{ title: string, description?: string }>, failure: string) {
  busyKey.value = key
  try {
    const message = await run()
    toast.success(message.title, message.description)
  }
  catch (e) {
    toast.failure(failure, resolveErrorMessage(e))
  }
  finally {
    busyKey.value = null
    refresh()
  }
}

function merge(key: string, body: PersonaHomonymMergeBody) {
  return act(key, async () => mergedMessage(await mergePersonaHomonyms(body)), 'Not merged')
}

function distinct(key: string, body: PersonaHomonymDistinctBody) {
  return act(key, async () => markedMessage(await markPersonaHomonymsDistinct(body)), 'Not marked')
}

defineExpose({ refresh, pending, total })
</script>

<template>
  <section>
    <div class="flex flex-wrap items-center gap-2 mb-3">
      <UInput v-model="searchFilter" icon="i-lucide-search" placeholder="Name or slug" class="w-full sm:w-64" :loading="pending" />
      <p class="text-xs text-muted">
        Same name, no account in common. Merge the ones that are one person; mark the others as different people.
      </p>
    </div>

    <FetchErrorAlert v-if="error" :error="error" title="Failed to load the homonyms" class="mb-4" />

    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium text-highlighted">
            To review
          </p>
          <UBadge v-if="!pending" color="neutral" variant="subtle" :label="`${formatNumber(total)} ${total === 1 ? 'name' : 'names'}`" />
        </div>
      </template>

      <div v-for="group in groups" :key="group.key" class="border-b border-default px-4 py-4 last:border-b-0">
        <p class="text-sm font-semibold text-highlighted mb-3">
          {{ group.personas[0]?.displayName ?? group.key }}
          <span class="text-xs font-normal text-muted">· {{ group.personas.length }} known players</span>
        </p>
        <KnownPlayersHomonymGroup
          :group="group"
          :busy="busyKey !== null"
          @merge="body => merge(group.key, body)"
          @distinct="body => distinct(group.key, body)"
        />
      </div>
      <p v-if="!pending && groups.length === 0" class="px-4 py-10 text-center text-sm text-muted">
        {{ searchFilter.trim() ? 'No homonym matches this search.' : 'No homonym left: every name is one known player.' }}
      </p>

      <div v-if="total > pageSize" class="flex justify-end border-t border-default px-4 py-3">
        <UPagination v-model:page="page" :total="total" :items-per-page="pageSize" :sibling-count="1" active-color="primary" variant="subtle" :disabled="pending" show-edges />
      </div>
    </UCard>
  </section>
</template>
