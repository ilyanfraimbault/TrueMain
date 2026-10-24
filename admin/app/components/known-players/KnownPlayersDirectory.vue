<script setup lang="ts">
// The known players (#2019): every person the scrapers — or an operator — group
// Riot accounts under, with what still needs a decision (an account in conflict,
// one TrueMain does not track yet). A row opens the edit panel; "Add" creates one
// by hand. `?player=<id>` deep-links the panel.
import type { PersonaEdited, PersonaListRow, PersonaListView } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'
import { formatNumber } from '~~/shared/utils/format'
import { accountOutcomeMessage, PERSONA_SOURCES, VIEW_ITEMS } from '~~/shared/utils/known-players'

const route = useRoute()
const router = useRouter()

const sourceItems = [{ label: 'All sources', value: ALL }, ...PERSONA_SOURCES.map(s => ({ label: s, value: s }))]
const view = ref<PersonaListView>('All')
const sourceFilter = ref<string>(ALL)
const searchFilter = ref('')
const searchDebounced = refDebounced(searchFilter, 300)
const page = ref(1)
const pageSize = 25

watch([view, sourceFilter, searchDebounced], () => {
  page.value = 1
})

const filters = computed(() => ({
  view: view.value === 'All' ? undefined : view.value,
  source: sourceFilter.value === ALL ? undefined : sourceFilter.value,
  search: searchDebounced.value.trim() || undefined,
  page: page.value,
  pageSize,
}))

const { data, pending, error, refresh } = usePersonas(filters)
const personas = computed(() => data.value?.personas ?? [])
const total = computed(() => data.value?.total ?? 0)
const hasFilters = computed(() => view.value !== 'All' || sourceFilter.value !== ALL || Boolean(searchFilter.value.trim()))
const emptyText = computed(() => hasFilters.value ? 'No known player matches these filters.' : 'No known player yet.')

// The edit panel, mirrored in `?player=` so it survives a reload and can be shared.
const openId = ref<string | null>(typeof route.query.player === 'string' ? route.query.player : null)
const detailOpen = computed({
  get: () => openId.value !== null,
  set: (open) => {
    if (!open) {
      openId.value = null
    }
  },
})
watch(openId, (id) => {
  router.replace({ query: { ...route.query, player: id ?? undefined } })
})

function open(persona: Pick<PersonaListRow, 'id'>) {
  openId.value = persona.id
}

const createOpen = ref(false)
const toast = useActionToast()

function created(edited: PersonaEdited) {
  createOpen.value = false
  const outcome = edited.accounts[0] ? accountOutcomeMessage(edited.accounts[0].status) : null
  toast.success(`${edited.slug} added`, outcome ? `${outcome.title}. ${outcome.description ?? ''}`.trim() : undefined)
  refresh()
  open(edited)
}

defineExpose({ refresh, pending })
</script>

<template>
  <section>
    <div class="flex flex-wrap items-center gap-2 mb-3">
      <UInput v-model="searchFilter" icon="i-lucide-search" placeholder="Name, slug or Riot ID" class="w-full sm:w-64" :loading="pending" />
      <USelect v-model="view" :items="VIEW_ITEMS" icon="i-lucide-list-filter" class="w-52" aria-label="Which known players" />
      <USelect v-model="sourceFilter" :items="sourceItems" icon="i-lucide-database" class="w-48" aria-label="Source" />
      <UButton v-if="hasFilters" icon="i-lucide-x" color="neutral" variant="ghost" label="Clear" @click="view = 'All'; sourceFilter = ALL; searchFilter = ''" />
      <UButton data-add class="ml-auto" icon="i-lucide-user-plus" label="Add known player" @click="createOpen = true" />
    </div>

    <FetchErrorAlert v-if="error" :error="error" title="Failed to load the known players" class="mb-4" />

    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium text-highlighted">
            Known players
          </p>
          <UBadge v-if="!pending" color="neutral" variant="subtle" :label="`${formatNumber(total)} ${total === 1 ? 'player' : 'players'}`" />
        </div>
      </template>

      <KnownPlayersList :personas="personas" :pending="pending" :empty-text="emptyText" @open="open" />

      <div v-if="total > pageSize" class="flex justify-end border-t border-default px-4 py-3">
        <UPagination v-model:page="page" :total="total" :items-per-page="pageSize" :sibling-count="1" active-color="primary" variant="subtle" :disabled="pending" show-edges />
      </div>
    </UCard>

    <USlideover v-model:open="detailOpen" title="Known player" :ui="{ content: 'sm:max-w-2xl' }">
      <template #body>
        <KnownPlayersEditor v-if="openId" :persona-id="openId" @changed="refresh()" />
      </template>
    </USlideover>

    <UModal v-model:open="createOpen" title="Add a known player" description="Everything added here carries the source manual.">
      <template #body>
        <KnownPlayersCreate @created="created" @cancel="createOpen = false" />
      </template>
    </UModal>
  </section>
</template>
