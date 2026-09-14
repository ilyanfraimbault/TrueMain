<script setup lang="ts">
// Body of the /logs detail slide-over: one entry's fields, the request it was
// written during (#1555) — method and path, status, duration and the traceId the
// client received in its ProblemDetails — then its message and exception stack.
import type { LogEntry } from '~~/shared/types/logs'
import { formatDateTime } from '~~/shared/utils/format'

const props = defineProps<{ entry: LogEntry }>()

// Rows written outside an HTTP request (every Ingestor row) carry none of these.
const hasRequest = computed(() =>
  Boolean(props.entry.requestPath || props.entry.traceId || props.entry.statusCode != null))

const statusColor = computed(() => {
  const status = props.entry.statusCode
  if (status == null) return 'neutral'
  if (status >= 500) return 'error'
  if (status >= 400) return 'warning'
  return 'success'
})
</script>

<template>
  <div class="space-y-5">
    <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Level
        </dt>
        <dd>
          <UBadge
            :color="levelColor(entry.level)"
            :icon="levelIcon(entry.level)"
            variant="subtle"
            size="sm"
            :label="entry.level"
          />
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Timestamp
        </dt>
        <dd class="tabular-nums">
          {{ formatDateTime(entry.timestampUtc) }}
        </dd>
      </div>
      <div class="col-span-2">
        <dt class="text-muted text-xs uppercase mb-0.5">
          Category
        </dt>
        <dd class="font-mono text-xs break-all">
          {{ entry.category }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Process
        </dt>
        <dd class="font-mono text-xs">
          {{ entry.processName ?? '—' }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Host
        </dt>
        <dd class="font-mono text-xs">
          {{ entry.host ?? '—' }}
        </dd>
      </div>
      <div v-if="entry.eventType" class="col-span-2">
        <dt class="text-muted text-xs uppercase mb-0.5">
          Event
        </dt>
        <dd>
          <UBadge
            color="primary"
            variant="subtle"
            size="sm"
            :label="entry.eventType"
          />
        </dd>
      </div>
    </dl>

    <div v-if="hasRequest">
      <p class="text-muted text-xs uppercase mb-1.5">
        Request
      </p>
      <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
        <div v-if="entry.requestPath" class="col-span-2">
          <dt class="text-muted text-xs uppercase mb-0.5">
            Path
          </dt>
          <dd class="font-mono text-xs break-all">
            <span v-if="entry.requestMethod" class="text-highlighted">{{ entry.requestMethod }}</span>
            {{ entry.requestPath }}
          </dd>
        </div>
        <div v-if="entry.statusCode != null">
          <dt class="text-muted text-xs uppercase mb-0.5">
            Status
          </dt>
          <dd>
            <UBadge
              :color="statusColor"
              variant="subtle"
              size="sm"
              :label="String(entry.statusCode)"
            />
          </dd>
        </div>
        <div v-if="entry.durationMs != null">
          <dt class="text-muted text-xs uppercase mb-0.5">
            Duration
          </dt>
          <dd class="tabular-nums">
            {{ entry.durationMs.toLocaleString('en-US') }} ms
          </dd>
        </div>
        <div v-if="entry.traceId" class="col-span-2">
          <dt class="text-muted text-xs uppercase mb-0.5">
            Trace ID
          </dt>
          <dd class="flex items-center justify-between gap-2">
            <span class="font-mono text-xs break-all">{{ entry.traceId }}</span>
            <CopyButton :text="entry.traceId" label="Copy" />
          </dd>
        </div>
      </dl>
    </div>

    <div>
      <div class="flex items-center justify-between mb-1.5">
        <p class="text-muted text-xs uppercase">
          Message
        </p>
        <CopyButton :text="entry.message" label="Copy" />
      </div>
      <pre class="text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ entry.message }}</pre>
    </div>

    <div v-if="entry.exception">
      <div class="flex items-center justify-between mb-1.5">
        <p class="text-muted text-xs uppercase">
          Exception
        </p>
        <CopyButton :text="entry.exception" label="Copy" />
      </div>
      <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ entry.exception }}</pre>
    </div>
  </div>
</template>
