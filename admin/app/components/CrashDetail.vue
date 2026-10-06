<script setup lang="ts">
// Body of the Crashes tab's detail slide-over (`CrashesPanel.vue`): the whole
// report, copyable as text — the server's plain-language reading (#722), the
// environment and memory/GC snapshot, the exception chain and the log lines
// captured just before the crash.
import type { CrashReport } from '~~/shared/types/ops'
import { formatDateTime, formatElapsed, humanizeBytes } from '~~/shared/utils/format'

defineProps<{ entry: CrashReport }>()
</script>

<template>
  <div class="space-y-5">
    <div class="flex items-center gap-2">
      <CopyButton :text="crashToText(entry)" label="Copy report" />
      <UBadge
        :color="crashSourceColor(entry.source)"
        :icon="crashSourceIcon(entry.source)"
        variant="subtle"
        size="sm"
        :label="crashSourceLabel(entry.source)"
      />
    </div>

    <!-- Server-derived plain-language reading of the crash (#722). -->
    <UAlert
      v-if="entry.explanation"
      color="warning"
      variant="subtle"
      icon="i-lucide-lightbulb"
      title="What likely happened"
      :description="entry.explanation"
    />

    <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Process
        </dt>
        <dd class="font-mono text-xs">
          {{ entry.processName }}
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
      <div v-if="entry.exceptionType" class="col-span-2">
        <dt class="text-muted text-xs uppercase mb-0.5">
          Exception type
        </dt>
        <dd class="font-mono text-xs break-all">
          {{ entry.exceptionType }}
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
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Uptime
        </dt>
        <dd class="tabular-nums">
          {{ formatElapsed(entry.uptimeSeconds * 1000) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Working set
        </dt>
        <dd class="tabular-nums">
          {{ humanizeBytes(entry.workingSetBytes) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Managed heap
        </dt>
        <dd class="tabular-nums">
          {{ humanizeBytes(entry.totalManagedMemoryBytes) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          GC gen 0 / 1 / 2
        </dt>
        <dd class="tabular-nums">
          {{ entry.gen0Collections }} / {{ entry.gen1Collections }} / {{ entry.gen2Collections }}
        </dd>
      </div>
      <div v-if="entry.exitCode !== null">
        <dt class="text-muted text-xs uppercase mb-0.5">
          Exit code
        </dt>
        <dd class="tabular-nums">
          {{ entry.exitCode }}
        </dd>
      </div>
      <div class="col-span-2">
        <dt class="text-muted text-xs uppercase mb-0.5">
          Runtime / app version
        </dt>
        <dd class="font-mono text-xs break-all">
          {{ entry.runtimeVersion ?? '—' }} · {{ entry.appVersion ?? '—' }}
        </dd>
      </div>
      <div v-if="entry.osDescription" class="col-span-2">
        <dt class="text-muted text-xs uppercase mb-0.5">
          OS
        </dt>
        <dd class="font-mono text-xs break-all">
          {{ entry.osDescription }}
        </dd>
      </div>
    </dl>

    <div v-if="entry.message">
      <div class="flex items-center justify-between mb-1.5">
        <p class="text-muted text-xs uppercase">
          Message
        </p>
        <CopyButton :text="entry.message" label="Copy" />
      </div>
      <pre class="text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ entry.message }}</pre>
    </div>

    <div v-if="entry.stackTrace">
      <div class="flex items-center justify-between mb-1.5">
        <p class="text-muted text-xs uppercase">
          Stack trace
        </p>
        <CopyButton :text="entry.stackTrace" label="Copy" />
      </div>
      <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ entry.stackTrace }}</pre>
    </div>

    <div v-if="entry.innerExceptions.length">
      <p class="text-muted text-xs uppercase mb-1.5">
        Inner exceptions
      </p>
      <div class="space-y-2">
        <div
          v-for="(inner, i) in entry.innerExceptions"
          :key="i"
          class="border border-default rounded-md p-3"
        >
          <p class="font-mono text-xs text-highlighted break-all mb-1">
            {{ inner.type }}
          </p>
          <p class="text-xs text-muted mb-1">
            {{ inner.message }}
          </p>
          <pre
            v-if="inner.stackTrace"
            class="text-xs text-muted bg-elevated/50 rounded p-2 overflow-auto whitespace-pre-wrap"
          >{{ inner.stackTrace }}</pre>
        </div>
      </div>
    </div>

    <div v-if="entry.recentLogTail.length">
      <p class="text-muted text-xs uppercase mb-1.5">
        Recent log tail ({{ entry.recentLogTail.length }})
      </p>
      <div class="border border-default rounded-md divide-y divide-default max-h-80 overflow-auto">
        <div
          v-for="(t, i) in entry.recentLogTail"
          :key="i"
          class="flex items-start gap-2 px-2 py-1 text-xs font-mono"
        >
          <span class="text-dimmed whitespace-nowrap tabular-nums">
            {{ formatDateTime(t.timestampUtc) }}
          </span>
          <UBadge :color="levelColor(t.level)" variant="subtle" size="sm" :label="t.level" />
          <span class="text-muted break-all">{{ t.message }}</span>
        </div>
      </div>
    </div>
  </div>
</template>
