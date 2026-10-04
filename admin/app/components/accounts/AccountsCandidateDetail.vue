<script setup lang="ts">
// Candidate detail slide-over of the Accounts hub's Pipeline tab: the exact
// pipeline stage, timestamps, ingested match count and the linked manual seed
// request. Split out of `AccountsPipeline.vue` (#1436); the tab owns the
// deep-linked fetch (`?candidate=`).
import type { CandidateDetail, MainCandidateStatus } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

const props = defineProps<{
  detail: CandidateDetail | null
  pending: boolean
  error: string | null
  errorTraceId: string | undefined
}>()

const open = defineModel<boolean>('open', { required: true })

const { nameFor, iconFor } = useChampionStatic()

const title = computed(() =>
  props.detail
    ? candidateRiotIdLabel(props.detail.gameName, props.detail.tagLine)
    : 'Candidate detail',
)

// Ordered pipeline stages for the detail stepper, with the timestamp that marks
// each one. Processing has no dedicated timestamp on the entity, so it inherits
// the discovered floor for ordering only.
const PIPELINE_ORDER: MainCandidateStatus[] = [
  'New',
  'Scored',
  'Queued',
  'Processing',
  'Validated',
]

// Index of the open candidate's stage in `PIPELINE_ORDER`, and the derived
// reached/not-reached flag per stage. Computed once per detail instead of
// re-scanning the array four times per rendered step.
const currentStageIndex = computed(() => {
  const status = props.detail?.status
  return status ? PIPELINE_ORDER.indexOf(status) : -1
})
const pipelineStages = computed(() =>
  PIPELINE_ORDER.map((stage, index) => ({ stage, reached: index <= currentStageIndex.value })),
)
</script>

<template>
  <USlideover
    v-model:open="open"
    :title="title"
    :ui="{ content: 'sm:max-w-xl' }"
  >
    <template #body>
      <div v-if="pending" class="space-y-4">
        <USkeleton class="h-16 w-full" />
        <USkeleton class="h-48 w-full" />
      </div>

      <FetchErrorAlert
        v-else-if="error"
        :message="error"
        :trace-id="errorTraceId"
        title="Could not load candidate"
      />

      <div v-else-if="detail" class="space-y-6">
        <!-- Identity header -->
        <div class="flex items-center gap-3">
          <NuxtImg
            v-if="iconFor(detail.championId)"
            :src="iconFor(detail.championId)!"
            :alt="nameFor(detail.championId)"
            width="40"
            height="40"
            loading="lazy"
            class="size-10 rounded-lg ring-1 ring-default"
          />
          <div v-else class="size-10 rounded-lg bg-elevated ring-1 ring-default" />
          <div class="min-w-0">
            <p class="text-sm font-medium text-highlighted truncate">
              {{ nameFor(detail.championId) }}
            </p>
            <p class="text-xs text-muted truncate">
              {{ candidateRiotIdLabel(detail.gameName, detail.tagLine) }}
            </p>
          </div>
          <UBadge
            :color="candidateStatusColor(detail.status)"
            variant="subtle"
            size="sm"
            :icon="candidateStatusIcon(detail.status)"
            :label="detail.status"
            class="ml-auto shrink-0"
          />
        </div>

        <!-- Pipeline stage stepper -->
        <div>
          <p class="text-muted text-xs uppercase mb-2">Pipeline stage</p>
          <UAlert
            v-if="detail.status === 'Rejected'"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-x"
            title="Rejected"
            description="This candidate was ruled out of the pipeline (not a main)."
          />
          <ol v-else class="flex flex-wrap items-center gap-1.5">
            <li
              v-for="{ stage, reached } in pipelineStages"
              :key="stage"
              class="flex items-center gap-1.5"
            >
              <UBadge
                :color="reached ? candidateStatusColor(detail.status) : 'neutral'"
                :variant="reached ? 'subtle' : 'soft'"
                size="sm"
                :label="stage"
              />
              <UIcon
                v-if="stage !== 'Validated'"
                name="i-lucide-chevron-right"
                class="size-3 text-dimmed"
              />
            </li>
          </ol>
        </div>

        <!-- Facts -->
        <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Region</dt>
            <dd class="font-mono text-xs">{{ detail.platformId }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Score</dt>
            <dd class="tabular-nums">{{ detail.score.toFixed(3) }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Mastery points</dt>
            <dd class="tabular-nums">{{ formatNumber(detail.championPoints) }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Mastery rank</dt>
            <dd class="tabular-nums">#{{ detail.championRankInMasteryTop }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Ingested matches</dt>
            <dd class="tabular-nums">{{ formatNumber(detail.ingestedMatchCount) }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Last played</dt>
            <dd class="tabular-nums text-xs">{{ formatDateTime(detail.lastPlayTimeUtc) }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Discovered</dt>
            <dd class="tabular-nums text-xs">{{ formatDateTime(detail.discoveredAtUtc) }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Scored</dt>
            <dd class="tabular-nums text-xs">{{ formatDateTime(detail.scoredAtUtc) }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">Validated</dt>
            <dd class="tabular-nums text-xs">{{ formatDateTime(detail.validatedAtUtc) }}</dd>
          </div>
          <div class="col-span-2">
            <dt class="text-muted text-xs uppercase mb-0.5">PUUID</dt>
            <dd class="font-mono text-xs break-all text-muted">{{ detail.puuid }}</dd>
          </div>
        </dl>

        <!-- Linked manual seed request -->
        <div>
          <p class="text-muted text-xs uppercase mb-2">Manual add request</p>
          <div
            v-if="detail.seedRequest"
            class="rounded-lg border border-default p-3 space-y-2"
          >
            <div class="flex items-center justify-between gap-2">
              <span class="text-sm text-default">
                {{ candidateRiotIdLabel(detail.seedRequest.gameName, detail.seedRequest.tagLine) }}
              </span>
              <UBadge
                :color="seedStatusColor(detail.seedRequest.status)"
                variant="subtle"
                size="sm"
                :icon="seedStatusIcon(detail.seedRequest.status)"
                :label="detail.seedRequest.status"
              />
            </div>
            <dl class="grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs">
              <div>
                <dt class="text-muted uppercase">Requested</dt>
                <dd class="tabular-nums">{{ formatDateTime(detail.seedRequest.requestedAtUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted uppercase">Processed</dt>
                <dd class="tabular-nums">{{ formatDateTime(detail.seedRequest.processedAtUtc) }}</dd>
              </div>
            </dl>
            <UAlert
              v-if="detail.seedRequest.error"
              color="error"
              variant="subtle"
              size="sm"
              icon="i-lucide-triangle-alert"
              :description="detail.seedRequest.error"
            />
          </div>
          <p v-else class="text-sm text-muted">
            Discovered organically by the ladder — no manual request.
          </p>
        </div>
      </div>
    </template>
  </USlideover>
</template>
