<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionSynergyEntry } from '~~/shared/types/champions'
import type { ChampionPosition } from '#common/utils/positions'
import { isLoadingStatus } from '#common/utils/async-data'

const props = defineProps<{
  championId: number
  position: ChampionPosition | null
  champions: ChampionStaticListItem[]
  /** Elo filter (exact tier or "X+" threshold), forwarded to both endpoints. */
  eloBracket?: string
  /** Patch (`major.minor`); omitted spans every patch with data. */
  patch?: string | null
}>()

const TOP_N = 8

// Which partner lane the list is narrowed to (null = every lane), and which
// partner the user picked to extend into a trio.
const partnerPosition = ref<ChampionPosition | null>(null)
const selectedPartner = ref<ChampionSynergyEntry | null>(null)

const championById = useChampionsById(() => props.champions)
const championName = computed(() =>
  championById.value.get(props.championId)?.name ?? 'this champion',
)

const { data, status, error } = useChampionSynergies(
  () => props.championId,
  () => props.position,
  {
    partnerPosition: () => partnerPosition.value,
    eloBracket: () => props.eloBracket,
    patch: () => props.patch,
  },
)

const {
  data: trioData,
  status: trioStatus,
  error: trioError,
} = useChampionTrioSynergies(
  () => props.championId,
  () => props.position,
  () => selectedPartner.value?.partnerChampionId ?? null,
  () => selectedPartner.value?.partnerPosition ?? null,
  {
    eloBracket: () => props.eloBracket,
    patch: () => props.patch,
  },
)

// Skeleton only on the very first load — keep the rows on screen while a lane
// filter refetches so the list doesn't flash out from under the cursor. `idle`
// counts as loading for the same reason as the Matchups panel (#1954): the server
// renders this before the client-only fetch ever ran, and must not claim the
// champion has no games.
const isLoading = computed(() => isLoadingStatus(status.value) && !data.value)
const isTrioLoading = computed(() => trioStatus.value === 'pending' && !trioData.value)

// Defensive re-sort: the API already orders by synergy, but the "top N" slice
// below must not depend on response order.
const partners = computed(() =>
  [...(data.value?.partners ?? [])].sort((a, b) => b.synergy - a.synergy).slice(0, TOP_N),
)

const completions = computed(() =>
  [...(trioData.value?.completions ?? [])].sort((a, b) => b.synergy - a.synergy).slice(0, TOP_N),
)

// The champion's own sample is what every expected win rate is built from, so
// when it is too thin the backend returns no partners at all rather than
// numbers it cannot stand behind. Distinguish that from "no partner reached the
// shared-games floor" — they need different sentences.
const hasChampionSample = computed(() => (data.value?.championGames ?? 0) > 0)
const noPartners = computed(() => !isLoading.value && !error.value && partners.value.length === 0)

const selectedPartnerName = computed(() =>
  selectedPartner.value
    ? championById.value.get(selectedPartner.value.partnerChampionId)?.name ?? 'this partner'
    : null,
)

function togglePartner(entry: ChampionSynergyEntry) {
  selectedPartner.value
    = selectedPartner.value?.partnerChampionId === entry.partnerChampionId
      && selectedPartner.value?.partnerPosition === entry.partnerPosition
      ? null
      : entry
}

// A lane filter can hide the currently selected partner; drop the trio panel
// rather than leaving it describing a duo no longer in the list.
watch(partnerPosition, () => {
  selectedPartner.value = null
})
watch(() => [props.championId, props.position], () => {
  selectedPartner.value = null
})
</script>

<template>
  <SectionCard
    :level="2"
    title="Synergies"
  >
    <template #actions>
      <RolePicker
        :position="partnerPosition"
        :exclude="position"
        @update:position="value => (partnerPosition = value)"
      />
    </template>

    <div class="flex flex-col gap-2">
      <template v-if="isLoading">
        <USkeleton v-for="i in 6" :key="`syn-skel-${i}`" class="h-8 w-full rounded-md" />
      </template>

      <!-- A failure, not an empty state: it had been hand-written as the same
           muted line as the two below, with copy of its own that never saw the
           real status — the #1661 sweep missed it because it was neither a
           `UAlert` nor a `describeFetchError` call. -->
      <FetchErrorAlert
        v-else-if="error"
        :error="error"
        title="Failed to load the synergies"
        class="my-2"
      />

      <!-- No usable baseline for the champion itself: say so with the real
           count instead of showing a ranking built on nothing. -->
      <UEmpty
        v-else-if="!hasChampionSample"
        size="sm"
        icon="i-lucide-users-round"
        :description="`Not enough recorded games on ${championName} in this lane yet to measure synergies.`"
      />

      <UEmpty
        v-else-if="noPartners"
        size="sm"
        icon="i-lucide-users-round"
        :description="`No teammate has reached ${data?.minGames ?? 0} shared games with ${championName} yet.`"
      />

      <template v-else>
        <ChampionSynergyColumns :champion-name="championName" />

        <div class="flex flex-col gap-1">
          <ChampionSynergyRow
            v-for="partner in partners"
            :key="`${partner.partnerChampionId}-${partner.partnerPosition}`"
            selectable
            :selected="selectedPartner?.partnerChampionId === partner.partnerChampionId
              && selectedPartner?.partnerPosition === partner.partnerPosition"
            :champion="championById.get(partner.partnerChampionId) ?? null"
            :champion-id="partner.partnerChampionId"
            :position="partner.partnerPosition"
            :games="partner.games"
            :play-rate="partner.playRate"
            :win-rate="partner.winRate"
            :synergy="partner.synergy"
            @select="togglePartner(partner)"
          />
        </div>
      </template>

      <!-- Trio completions for the chosen duo. -->
      <ChampionSynergyTrios
        v-if="selectedPartner"
        :champion-name="championName"
        :partner-name="selectedPartnerName"
        :loading="isTrioLoading"
        :error="trioError"
        :completions="completions"
        :pair-games="trioData?.pairGames ?? 0"
        :min-games="trioData?.minGames ?? 0"
        :champion-by-id="championById"
      />
    </div>
  </SectionCard>
</template>
