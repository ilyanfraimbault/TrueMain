<script setup lang="ts">
import type { CompositionSlotInput } from '~~/shared/types/composition'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { describeFetchError } from '~/utils/errors'
import { formatPercentage } from '~~/shared/utils/ddragon'

useSeoMeta({
  title: 'Composition Builder',
  description:
    'Pick your champion, your role and both team comps to get a build recommendation drawn from the most similar real games.',
})

const route = useRoute()
const router = useRouter()

// ─── Draft state ─────────────────────────────────────────────────────────────

const initialChampion = Number(route.query.champion)
const playedChampionId = ref<number | null>(
  Number.isInteger(initialChampion) && initialChampion > 0 ? initialChampion : null,
)
const initialPosition = typeof route.query.position === 'string' ? route.query.position : ''
const playedPosition = ref<ChampionPosition | null>(
  isChampionPosition(initialPosition) ? initialPosition : null,
)
const selectedPatch = ref(typeof route.query.patch === 'string' ? route.query.patch : '')

function emptySlots(): Record<ChampionPosition, number | null> {
  return { TOP: null, JUNGLE: null, MIDDLE: null, BOTTOM: null, UTILITY: null }
}

const allySlots = ref(emptySlots())
const enemySlots = ref(emptySlots())

// The player's own slot is the picked champion — an ally there would be
// rejected by the API, so the row disappears and any leftover pick is cleared.
watch(playedPosition, (position) => {
  if (position) {
    allySlots.value[position] = null
  }
})

// Deep-link the player pick (champion, position, patch) — the nine team slots
// stay ephemeral, a full draft in the URL would be noise.
watch([playedChampionId, playedPosition, selectedPatch], ([champion, position, patch]) => {
  void router.replace({
    query: {
      ...(champion ? { champion: String(champion) } : {}),
      ...(position ? { position } : {}),
      ...(patch ? { patch } : {}),
    },
  })
})

// ─── Reference data ──────────────────────────────────────────────────────────

const { data: staticList, error: staticError } = useChampionStaticList()
const champions = computed(() => staticList.value ?? [])
const championsById = useChampionsById(champions)

const { data: versions } = useDDragonVersions()
// Reka UI selects reject an empty-string item value, so "All patches" rides a
// sentinel and the page state keeps '' for "no filter".
const ALL_PATCHES = 'all'
const recentPatchOptions = usePatchOptions(() => versions.value, () => selectedPatch.value)
const patchOptions = computed(() => [
  { label: 'All patches', value: ALL_PATCHES },
  ...recentPatchOptions.value,
])

// ─── Submit ──────────────────────────────────────────────────────────────────

const { data: recommendation, isLoading, error, submit, clear } = useCompositionBuild()

const canSubmit = computed(() => playedChampionId.value !== null && playedPosition.value !== null)

function toSlots(slots: Record<ChampionPosition, number | null>): CompositionSlotInput[] {
  return POSITION_OPTIONS
    .map(option => ({ position: option.value, championId: slots[option.value] }))
    .filter((slot): slot is CompositionSlotInput => slot.championId !== null)
}

async function recommend() {
  if (playedChampionId.value === null || playedPosition.value === null) {
    return
  }

  await submit(playedChampionId.value, {
    position: playedPosition.value,
    ...(selectedPatch.value ? { patch: selectedPatch.value } : {}),
    allies: toSlots(allySlots.value),
    enemies: toSlots(enemySlots.value),
  })
}

function resetDraft() {
  allySlots.value = emptySlots()
  enemySlots.value = emptySlots()
  clear()
}

const playedChampionName = computed(() =>
  playedChampionId.value === null ? null : championsById.value.get(playedChampionId.value)?.name ?? null,
)

const winRate = computed(() => {
  const build = recommendation.value?.build
  return build && build.gamesConsidered > 0 ? build.wins / build.gamesConsidered : null
})

const allyRows = computed(() =>
  POSITION_OPTIONS.filter(option => option.value !== playedPosition.value))
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Draft tools"
      title="Composition builder"
      description="Pick your champion and both comps — even partially — and get the build that won the most similar real games."
    >
      <div class="flex flex-wrap items-center gap-3">
        <ChampionPicker
          :champions="champions"
          :champion-id="playedChampionId"
          placeholder="Your champion"
          @update:champion-id="playedChampionId = $event"
        />
        <RolePicker
          :position="playedPosition"
          hide-all
          @update:position="playedPosition = $event"
        />
        <USelect
          :items="patchOptions"
          :model-value="selectedPatch || ALL_PATCHES"
          class="w-36"
          aria-label="Patch"
          @update:model-value="selectedPatch = $event === ALL_PATCHES ? '' : String($event ?? '')"
        />
      </div>
    </PageHeader>

    <UAlert
      v-if="staticError"
      color="error"
      variant="soft"
      title="Champion list unavailable"
      :description="describeFetchError(staticError)"
    />

    <div class="grid gap-6 lg:grid-cols-2">
      <SectionCard title="Your team" subtitle="The four picks around you — leave unknown lanes empty.">
        <ul class="space-y-2">
          <li
            v-for="option in allyRows"
            :key="option.value"
            class="glass-hover flex items-center gap-3 rounded-lg px-3 py-2"
          >
            <SkeletonImage
              :src="option.iconUrl"
              :alt="option.label"
              :width="20"
              :height="20"
              class="size-5 opacity-80"
            />
            <span class="w-20 text-sm text-muted">{{ option.label }}</span>
            <ChampionPicker
              :champions="champions"
              :champion-id="allySlots[option.value]"
              placeholder="Any champion"
              trigger-class="w-full"
              class="flex-1"
              @update:champion-id="allySlots[option.value] = $event"
            />
          </li>
        </ul>
      </SectionCard>

      <SectionCard title="Enemy team" subtitle="The lane opponent weighs the most — fill it first if you know it.">
        <ul class="space-y-2">
          <li
            v-for="option in POSITION_OPTIONS"
            :key="option.value"
            class="glass-hover flex items-center gap-3 rounded-lg px-3 py-2"
          >
            <SkeletonImage
              :src="option.iconUrl"
              :alt="option.label"
              :width="20"
              :height="20"
              class="size-5 opacity-80"
            />
            <span class="w-20 text-sm text-muted">
              {{ option.label }}
              <span v-if="option.value === playedPosition" class="block text-xs text-primary">vs you</span>
            </span>
            <ChampionPicker
              :champions="champions"
              :champion-id="enemySlots[option.value]"
              placeholder="Any champion"
              trigger-class="w-full"
              class="flex-1"
              @update:champion-id="enemySlots[option.value] = $event"
            />
          </li>
        </ul>
      </SectionCard>
    </div>

    <div class="flex flex-wrap items-center gap-3">
      <UButton
        size="lg"
        icon="i-lucide-wand-sparkles"
        :loading="isLoading"
        :disabled="!canSubmit"
        @click="recommend"
      >
        Recommend a build
      </UButton>
      <UButton
        variant="ghost"
        color="neutral"
        :disabled="isLoading"
        @click="resetDraft"
      >
        Reset draft
      </UButton>
      <p v-if="!canSubmit" class="text-sm text-muted">
        Pick your champion and role to get a recommendation.
      </p>
    </div>

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      title="Recommendation unavailable"
      :description="describeFetchError(error)"
    />

    <SectionCard
      v-if="recommendation"
      :title="playedChampionName ? `Recommended for ${playedChampionName}` : 'Recommendation'"
    >
      <div v-if="recommendation.build.gamesConsidered === 0" class="glass rounded-lg px-6 py-12 text-center">
        <p class="font-medium">No similar games found</p>
        <p class="mt-1 text-sm text-muted">
          Nothing recorded for this champion at this position yet — try clearing the patch filter.
        </p>
      </div>
      <dl v-else class="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <div>
          <dt class="text-sm text-muted">Games sampled</dt>
          <dd class="text-lg font-semibold">{{ recommendation.build.gamesConsidered }}</dd>
        </div>
        <div>
          <dt class="text-sm text-muted">Win rate</dt>
          <dd class="text-lg font-semibold">{{ winRate === null ? '—' : formatPercentage(winRate) }}</dd>
        </div>
        <div>
          <dt class="text-sm text-muted">Draft similarity</dt>
          <dd class="text-lg font-semibold">
            {{ recommendation.confidence.maxPossibleScore === 0 ? '—' : formatPercentage(recommendation.confidence.meanSimilarity) }}
          </dd>
        </div>
        <div>
          <dt class="text-sm text-muted">Pool scanned</dt>
          <dd class="text-lg font-semibold">{{ recommendation.confidence.candidatePoolSize }}</dd>
        </div>
      </dl>
    </SectionCard>
  </main>
</template>
