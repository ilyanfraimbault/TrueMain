<script setup lang="ts">
// The body of the per-match slide-over: header facts, the checks this match
// trips, and both teams laid out by position with the gaps highlighted.
//
// Extracted from pages/data-quality.vue (#992) so the page reads as the layout
// it now is — a verdict, a list of checks and a list of flagged matches —
// instead of burying two hundred lines of slide-over markup between them.
import type {
  DataQualityIssueType,
  IssueMeta,
  MatchDataQualityDetail,
  MatchTeam,
} from '~~/shared/types/ops'
import { formatDateTime, formatDuration } from '~~/shared/utils/format'

defineProps<{
  detail: MatchDataQualityDetail | null
  pending: boolean
  error: string | null
  errorTraceId?: string | null
  meta: Record<DataQualityIssueType, IssueMeta>
  queueLabel: (queueId: number) => string
}>()

const { nameFor, iconFor } = useChampionStatic()

// Identity tint for a team header: blue side stays blue, red side stays red,
// whatever the result — win/loss is conveyed by the Victory/Defeat badge so
// "Blue team" can never render red. Unknown team ids stay neutral.
function teamAccent(teamId: number): string {
  if (teamId === 100) {
    return 'text-info'
  }
  if (teamId === 200) {
    return 'text-error'
  }
  return 'text-muted'
}

function teamLabel(teamId: number, index: number): string {
  if (teamId === 100) {
    return 'Blue team'
  }
  if (teamId === 200) {
    return 'Red team'
  }
  return `Team ${index + 1}`
}

// Header count: actual vs expected ("4/5 players") so a short roster reads as
// short, with members that exist but didn't map onto a lane called out as
// unplaced instead of being passed off as missing players.
function teamPlayersLabel(team: MatchTeam): string {
  const count = team.expectedPlayerCount !== null
    ? `${team.playerCount}/${team.expectedPlayerCount} players`
    : `${team.playerCount} ${team.playerCount === 1 ? 'player' : 'players'}`
  return team.unplacedCount > 0 ? `${count} · ${team.unplacedCount} unplaced` : count
}
</script>

<template>
  <div v-if="pending" class="space-y-4">
    <USkeleton class="h-16 w-full" />
    <USkeleton class="h-64 w-full" />
  </div>

  <FetchErrorAlert
    v-else-if="error"
    :message="error"
    :trace-id="errorTraceId ?? undefined"
    title="Could not load match"
  />

  <div v-else-if="detail" class="space-y-5">
    <!-- Header facts -->
    <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">Region</dt>
        <dd class="font-mono text-xs">{{ detail.platformId }}</dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">Queue</dt>
        <dd>
          {{ queueLabel(detail.queueId) }}
          <span v-if="detail.queueId !== 0" class="text-dimmed">({{ detail.queueId }})</span>
          <span v-else class="block text-xs text-dimmed mt-0.5">
            Riot sent no queue id — the game likely aborted before stats were recorded.
          </span>
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">Played</dt>
        <dd class="tabular-nums text-xs">{{ formatDateTime(detail.gameStartTimeUtc) }}</dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">Duration</dt>
        <dd
          class="tabular-nums text-xs"
          :class="detail.gameDurationSeconds <= 0 ? 'text-error font-medium' : ''"
        >
          {{ detail.gameDurationSeconds <= 0
            ? '0 (no length recorded)'
            : formatDuration(detail.gameDurationSeconds * 1000) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">Players</dt>
        <dd
          class="tabular-nums text-xs"
          :class="detail.expectedParticipantCount !== null
            && detail.participantCount !== detail.expectedParticipantCount
            ? 'text-error font-medium'
            : ''"
        >
          {{ detail.participantCount }}<template
            v-if="detail.expectedParticipantCount !== null"
          >&nbsp;/&nbsp;{{ detail.expectedParticipantCount }} expected</template>
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">Timeline</dt>
        <dd>
          <UBadge
            :color="detail.timelineIngested ? 'success' : 'warning'"
            variant="subtle"
            size="sm"
            :icon="detail.timelineIngested ? 'i-lucide-check' : 'i-lucide-clock-alert'"
            :label="detail.timelineIngested ? 'Ingested' : 'Missing'"
          />
        </dd>
      </div>
    </dl>

    <!-- Flagged issues for this match -->
    <div v-if="detail.issues.length > 0">
      <p class="text-muted text-xs uppercase mb-1.5">Flagged issues</p>
      <div class="flex flex-wrap gap-1.5">
        <UBadge
          v-for="type in detail.issues"
          :key="type"
          :color="meta[type].color"
          variant="subtle"
          size="sm"
          :icon="meta[type].icon"
          :label="meta[type].label"
        />
      </div>
    </div>
    <UAlert
      v-else
      color="success"
      variant="subtle"
      icon="i-lucide-circle-check"
      title="No issues"
      description="This match passes every applicable check."
    />

    <!-- Teams laid out by position -->
    <div v-if="detail.teams.length > 0" class="space-y-4">
      <p class="text-muted text-xs uppercase">
        {{ detail.hasLanes ? 'Teams by position' : 'Teams' }}
      </p>
      <div
        v-for="(team, teamIndex) in detail.teams"
        :key="team.teamId"
        class="rounded-lg border border-default overflow-hidden"
      >
        <div class="flex items-center justify-between gap-3 px-3 py-2 bg-elevated/30">
          <div class="flex items-center gap-2 min-w-0">
            <p class="text-xs font-medium truncate" :class="teamAccent(team.teamId)">
              {{ teamLabel(team.teamId, teamIndex) }}
              <span class="text-dimmed font-normal">· team {{ team.teamId }}</span>
            </p>
            <UBadge
              v-if="team.win !== null"
              :color="team.win ? 'success' : 'error'"
              variant="subtle"
              size="sm"
              :label="team.win ? 'Victory' : 'Defeat'"
            />
          </div>
          <span
            class="text-xs whitespace-nowrap"
            :class="team.expectedPlayerCount !== null
              && team.playerCount !== team.expectedPlayerCount
              ? 'text-error font-medium'
              : 'text-muted'"
          >
            {{ teamPlayersLabel(team) }}
          </span>
        </div>
        <ul class="divide-y divide-default">
          <li
            v-for="(slot, slotIndex) in team.slots"
            :key="`${slot.position}-${slot.participantId ?? slotIndex}`"
            class="flex items-center gap-3 px-3 py-2"
            :class="!slot.filled ? 'bg-error/5' : slot.duplicateChampion ? 'bg-warning/5' : ''"
          >
            <!-- Position label (lane queues) -->
            <div
              v-if="detail.hasLanes"
              class="w-20 shrink-0 text-xs font-medium uppercase"
              :class="slot.filled ? 'text-muted' : 'text-error'"
            >
              {{ slot.position || 'UNKNOWN' }}
            </div>

            <!-- Filled slot: champion + summoner -->
            <template v-if="slot.filled">
              <NuxtImg
                v-if="slot.championId !== null && iconFor(slot.championId)"
                :src="iconFor(slot.championId)!"
                :alt="nameFor(slot.championId)"
                width="24"
                height="24"
                loading="lazy"
                class="size-6 rounded ring-1 ring-default shrink-0"
              />
              <div
                v-else
                class="size-6 rounded bg-elevated ring-1 ring-default shrink-0"
              />
              <div class="min-w-0 flex-1">
                <p class="text-xs text-highlighted truncate">
                  {{ slot.championId !== null ? nameFor(slot.championId) : '—' }}
                </p>
                <p class="text-xs text-muted truncate">
                  {{ slot.summonerName || '—' }}
                </p>
              </div>
              <UBadge
                v-if="slot.duplicateChampion"
                color="warning"
                variant="subtle"
                size="sm"
                icon="i-lucide-copy"
                label="Duplicate"
              />
            </template>

            <!-- Empty slot: a highlighted gap -->
            <template v-else>
              <UIcon name="i-lucide-circle-slash" class="size-6 text-error/60 shrink-0" />
              <span class="text-xs text-error italic">Missing</span>
            </template>
          </li>
        </ul>
      </div>
    </div>
    <p v-else class="text-sm text-muted">
      No participant rows recorded for this match.
    </p>
  </div>
</template>
