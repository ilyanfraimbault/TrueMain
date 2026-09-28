<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionSynergyEntry } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'

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
// filter refetches so the list doesn't flash out from under the cursor.
const isLoading = computed(() => status.value === 'pending' && !data.value)
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
        <!-- Column key. "Synergy" is points of win rate above expectation, so it
             needs naming — a bare signed number would read as a win-rate delta.
             The definition sits in the header's tooltip, not in a footnote. -->
        <div class="flex items-center gap-3 px-1.5 text-[0.65rem] font-semibold uppercase tracking-wide text-dimmed">
          <span class="size-8 shrink-0" aria-hidden="true" />
          <span class="size-4 shrink-0" aria-hidden="true" />
          <span class="min-w-0 flex-1">Partner</span>
          <span class="shrink-0">Sample</span>
          <span class="w-12 shrink-0 text-right">WR</span>
          <span
            class="w-14 shrink-0 text-right"
            :title="`Win rate minus what ${championName}'s and the partner's own win rates predicted, in points.`"
          >Synergy</span>
        </div>

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
      <div v-if="selectedPartner" class="flex flex-col gap-3 border-t border-default pt-4">
        <p class="px-2 text-xs font-semibold uppercase tracking-wide text-primary">
          Best third pick with {{ selectedPartnerName }}
        </p>

        <template v-if="isTrioLoading">
          <USkeleton v-for="i in 3" :key="`trio-skel-${i}`" class="h-8 w-full rounded-md" />
        </template>

        <FetchErrorAlert
          v-else-if="trioError"
          :error="trioError"
          title="Failed to load the trio suggestions"
          class="my-2"
        />

        <!-- A duo's games are the ceiling for every trio drawn from it, so a thin
             duo cannot support a third dimension. Show the real count. -->
        <UEmpty
          v-else-if="completions.length === 0"
          size="xs"
          icon="i-lucide-users-round"
        >
          <template #description>
            {{ championName }} and {{ selectedPartnerName }} share
            {{ (trioData?.pairGames ?? 0).toLocaleString('en-US') }} games — not enough to suggest a third pick
            (minimum {{ trioData?.minGames ?? 0 }} games together with the same teammate).
          </template>
        </UEmpty>

        <template v-else>
          <div class="flex flex-col gap-1">
            <ChampionSynergyRow
              v-for="completion in completions"
              :key="`${completion.championId}-${completion.position}`"
              :champion="championById.get(completion.championId) ?? null"
              :champion-id="completion.championId"
              :position="completion.position"
              :games="completion.games"
              :win-rate="completion.winRate"
              :synergy="completion.synergy"
            />
          </div>
        </template>
      </div>
    </div>
  </SectionCard>
</template>
