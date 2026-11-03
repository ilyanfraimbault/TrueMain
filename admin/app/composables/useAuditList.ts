import type { AuditEventsFilters } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'

/**
 * The Audit page's filters and the paged `GET /api/ops/audit-events` they drive
 * (#2288). `?targetType=&targetId=` narrows it to one entity — a link from
 * elsewhere lands on that entity's trail — and stays until cleared.
 */
export function useAuditList() {
  const route = useRoute()
  const router = useRouter()
  const queryText = (raw: unknown) => (typeof raw === 'string' && raw.trim() ? raw.trim() : undefined)

  const action = ref<string>(ALL)
  const actor = ref<string>(ALL)
  const targetType = ref<string>(queryText(route.query.targetType) ?? ALL)
  const targetId = ref<string | undefined>(queryText(route.query.targetId))
  const sinceWindow = ref<SinceWindow>(ALL)
  const searchInput = ref('')
  const search = refDebounced(searchInput, 300)
  const page = ref(1)
  const pageSize = 50

  const filters = computed<AuditEventsFilters>(() => ({
    action: action.value === ALL ? undefined : action.value,
    actor: actor.value === ALL ? undefined : actor.value,
    targetType: targetType.value === ALL ? undefined : targetType.value,
    targetId: targetId.value,
    since: sinceWindow.value === ALL ? undefined : sinceToIso(sinceWindow.value),
    search: search.value.trim() || undefined,
    page: page.value,
    pageSize,
  }))

  watch([action, actor, targetType, targetId, sinceWindow, search], () => {
    page.value = 1
  })

  const { data, pending, refreshing, error, refresh } = useAuditEvents(filters)

  const all = (label: string) => ({ label, value: ALL })
  const actionItems = computed(() => [
    all('All actions'),
    ...(data.value?.actions ?? []).map(a => ({ label: a.label, value: a.name })),
  ])
  const actorItems = computed(() => [all('Anyone'), ...(data.value?.actors ?? []).map(a => ({ label: a, value: a }))])
  const targetTypeItems = computed(() => [
    all('Any target'),
    ...(data.value?.targetTypes ?? []).map(t => ({ label: t, value: t })),
  ])

  const hasActiveFilters = computed(() => Boolean(
    action.value !== ALL || actor.value !== ALL || targetType.value !== ALL || targetId.value
    || sinceWindow.value !== ALL || searchInput.value.trim(),
  ))

  function clearTarget() {
    targetId.value = undefined
    const { targetId: _id, targetType: _type, ...rest } = route.query
    router.replace({ query: rest })
  }

  function resetFilters() {
    action.value = ALL
    actor.value = ALL
    targetType.value = ALL
    sinceWindow.value = ALL
    searchInput.value = ''
    clearTarget()
  }

  return {
    action,
    actor,
    targetType,
    targetId,
    sinceWindow,
    searchInput,
    page,
    actionItems,
    actorItems,
    targetTypeItems,
    hasActiveFilters,
    clearTarget,
    resetFilters,
    pending,
    refreshing,
    error,
    refresh,
    entries: computed(() => data.value?.entries ?? []),
    total: computed(() => data.value?.total ?? 0),
    serverPageSize: computed(() => data.value?.pageSize ?? pageSize),
  }
}
