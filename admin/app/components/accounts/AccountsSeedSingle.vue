<script setup lang="ts">
// "Add a single main" section of the Accounts hub's "Add mains" tab: the 3-field
// form (gameName, tagLine, region) and the live status stepper of the request it
// queued (Pending -> Resolving -> Ingested/Failed). The submit + polling live in
// `useSeedTracking`; split out of `AccountsSeed.vue` (#1436).
import type { FormError, FormSubmitEvent } from '@nuxt/ui'
import { TRACKED_REGION_ITEMS, TRACKED_REGIONS_LABEL } from '~~/shared/utils/regions'
import { formatRiotId, parseRiotIdOrSlug } from '#site-shared/utils/riot-id'
import { SEED_POLL_TIMEOUT_MS } from '~/composables/useSeedTracking'
import { SEED_STEP_STYLE, SEED_STEPS, seedStepStates } from '~/utils/seed-tracking'

const emit = defineEmits<{
  /** A request was queued: the queue's first page now holds it. */
  submitted: []
  /** The tracked request reached a terminal status. */
  settled: []
}>()

// Region <select> options. `value` is widened to `string` so `state.region` (a
// free `string`) type-checks.
const regionItems = TRACKED_REGION_ITEMS

interface SeedFormState {
  gameName: string
  tagLine: string
  region: string
}

const state = reactive<SeedFormState>({
  gameName: '',
  tagLine: '',
  region: '',
})

// `?riotId=` prefills the two name fields (the ⌘K palette's "Add … as a main",
// #2290); the region stays the operator's pick, a Riot ID does not carry it.
useQueryValue('riotId', (riotId) => {
  const parsed = parseRiotIdOrSlug(riotId)
  if (parsed) {
    state.gameName = parsed.gameName.trim()
    state.tagLine = parsed.tagLine.trim()
  }
}, { immediate: true })

// Normalize a tag line: drop a leading '#' (people paste `Name#TAG`) and trim.
function normalizeTag(raw: string): string {
  return raw.replace(/^#/, '').trim()
}

function validate(s: SeedFormState): FormError[] {
  const errors: FormError[] = []
  if (!s.gameName.trim()) {
    errors.push({ name: 'gameName', message: 'Required' })
  }
  if (!normalizeTag(s.tagLine)) {
    errors.push({ name: 'tagLine', message: 'Required' })
  }
  if (!s.region) {
    errors.push({ name: 'region', message: 'Pick a region' })
  }
  return errors
}

const { state: seed, submitting, tracked, submit } = useSeedTracking({
  onSubmitted: () => emit('submitted'),
  onSettled: () => emit('settled'),
})

function onSubmit(event: FormSubmitEvent<SeedFormState>) {
  return submit({
    gameName: event.data.gameName.trim(),
    tagLine: normalizeTag(event.data.tagLine),
    platformId: event.data.region,
  })
}

const trackedRiotId = computed(() =>
  tracked.value === null ? '' : (formatRiotId(tracked.value.gameName, tracked.value.tagLine) ?? ''),
)

// --- Status presentation -----------------------------------------------------
// Badge colours/icons live in `utils/pipeline-status.ts` (auto-imported), so this
// tab badges a status exactly like the account explorer and the candidate
// pipeline do.

// Stepper: Pending -> Resolving -> (Ingested | Failed), each step's look read from
// one table (`utils/seed-tracking.ts`); Failed marks the resolve step, where it broke.
const steps = computed(() => tracked.value ? seedStepStates(tracked.value.status) : [])
const polling = computed(() => seed.value.s === 'polling')
const isFailed = computed(() => tracked.value?.status === 'Failed')
</script>

<template>
  <section class="max-w-4xl">
    <div class="flex items-center gap-2 mb-1">
      <UIcon name="i-lucide-user-plus" class="size-4 text-primary" />
      <h2 class="text-sm font-medium text-highlighted">
        Add a single main
      </h2>
    </div>
    <p class="text-xs text-muted mb-4">
      Register one Riot ID and watch it resolve live.
    </p>

    <UForm
      :state="state"
      :validate="validate"
      class="space-y-4"
      @submit="onSubmit"
    >
      <div class="grid grid-cols-1 sm:grid-cols-[1fr_auto_10rem] gap-3 items-start">
        <UFormField label="Game name" name="gameName" required>
          <UInput
            v-model="state.gameName"
            placeholder="e.g. Faker"
            icon="i-lucide-user"
            class="w-full"
            autocomplete="off"
          />
        </UFormField>

        <UFormField label="Tag line" name="tagLine" required>
          <UInput
            v-model="state.tagLine"
            placeholder="KR1"
            class="w-full sm:w-28"
            autocomplete="off"
            :ui="{ leading: 'ps-2.5' }"
          >
            <template #leading>
              <span class="text-dimmed text-sm">#</span>
            </template>
          </UInput>
        </UFormField>

        <UFormField label="Region" name="region" required>
          <USelect
            v-model="state.region"
            :items="regionItems"
            placeholder="Region"
            icon="i-lucide-globe"
            class="w-full"
          />
        </UFormField>
      </div>

      <div class="flex items-center gap-3">
        <UButton
          type="submit"
          icon="i-lucide-user-plus"
          label="Seed main"
          :loading="submitting"
        />
        <p class="text-xs text-dimmed">
          Tracked regions: {{ TRACKED_REGIONS_LABEL }}
        </p>
      </div>
    </UForm>

    <!-- Submit-level error (network / 400 before an id was issued) -->
    <FetchErrorAlert
      v-if="seed.s === 'rejected'"
      :message="seed.error"
      title="Could not queue this Riot ID"
      class="mt-6"
    />

    <!-- Live status of the tracked request -->
    <div
      v-if="tracked"
      class="mt-6 rounded-lg border p-4 bg-elevated/25"
      :class="isFailed ? 'border-error/30' : 'border-default'"
    >
      <div class="flex items-center justify-between gap-2 mb-4">
        <p class="font-medium text-highlighted truncate">
          {{ trackedRiotId }}
          <span class="text-muted font-normal">· {{ tracked.platformId }}</span>
        </p>
        <UBadge
          :color="seedStatusColor(tracked.status)"
          :icon="seedStatusIcon(tracked.status)"
          variant="subtle"
          size="sm"
          :label="tracked.status"
          :ui="{ leadingIcon: polling && tracked.status === 'Resolving' ? 'animate-spin' : '' }"
        />
      </div>

      <!-- Stepper -->
      <ol class="flex items-center gap-2">
        <template v-for="(step, i) in SEED_STEPS" :key="step.key">
          <li class="flex items-center gap-2 min-w-0" :data-step-state="steps[i]">
            <span
              class="flex items-center justify-center size-6 rounded-full text-xs shrink-0 ring-1"
              :class="SEED_STEP_STYLE[steps[i]!].dot"
            >
              <UIcon v-if="steps[i] === 'done'" name="i-lucide-check" class="size-3.5" />
              <UIcon v-else-if="steps[i] === 'failed'" name="i-lucide-x" class="size-3.5" />
              <UIcon v-else-if="steps[i] === 'active' && polling" name="i-lucide-loader" class="size-3.5 animate-spin" />
              <span v-else>{{ i + 1 }}</span>
            </span>
            <span class="text-xs truncate" :class="SEED_STEP_STYLE[steps[i]!].label">
              {{ step.label }}
            </span>
          </li>
          <li
            v-if="i < SEED_STEPS.length - 1"
            class="flex-1 h-px min-w-4"
            :class="SEED_STEP_STYLE[steps[i]!].connector"
          />
        </template>
      </ol>

      <!-- Terminal / in-flight detail -->
      <div class="mt-4 text-sm">
        <UAlert
          v-if="tracked.status === 'Ingested'"
          color="success"
          variant="subtle"
          icon="i-lucide-circle-check"
          title="Account queued"
          description="Matches & mains will follow on the next ingestion run — only the account and its mastery-derived candidates have been created so far."
        />
        <UAlert
          v-else-if="tracked.status === 'Failed'"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-x"
          title="Seeding failed"
          :description="tracked.error ?? 'The Riot ID could not be resolved.'"
        />
        <p v-else-if="seed.s === 'timedOut'" class="text-muted">
          Still processing after {{ SEED_POLL_TIMEOUT_MS / 1000 }}s — it will
          continue in the background. Use refresh to check on it later.
        </p>
        <p v-else class="text-muted flex items-center gap-2">
          <UIcon name="i-lucide-loader" class="size-4 animate-spin" />
          {{ tracked.status === 'Resolving'
            ? 'Resolving the Riot ID with the Riot API…'
            : 'Waiting for the resolver to pick this up…' }}
        </p>

        <!-- Resolved PUUID, once known -->
        <div
          v-if="tracked.resolvedPuuid"
          class="mt-3 flex items-baseline gap-2"
        >
          <span class="text-muted text-xs uppercase">PUUID</span>
          <code class="font-mono text-xs text-default break-all">
            {{ tracked.resolvedPuuid }}
          </code>
        </div>
      </div>
    </div>
  </section>
</template>
