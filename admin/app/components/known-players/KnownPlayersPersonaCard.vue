<script setup lang="ts">
// One side of a conflict in the detail panel (#2253): the known player, every account
// its sources list — the disputed one marked — and every link, each with its source.
// What the operator reads to tell a duplicate from a wrong account.
import type { PersonaOps } from '~~/shared/types/ops'
import { computed } from 'vue'

const props = defineProps<{
  persona: PersonaOps
  /** "Claims it" or "Holds it". */
  side: string
}>()

const tracked = computed(() => props.persona.accounts.filter(a => a.riotAccountId).length)
</script>

<template>
  <section class="space-y-3">
    <header>
      <p class="text-xs uppercase text-muted">
        {{ side }}
      </p>
      <p class="text-base font-semibold text-highlighted">
        {{ persona.displayName }}
      </p>
      <p class="text-xs text-muted">
        <span class="font-mono">{{ persona.slug }}</span> · from {{ persona.source }}
      </p>
    </header>

    <div>
      <p class="text-xs uppercase text-muted mb-1">
        Accounts · {{ persona.accounts.length }} ({{ tracked }} tracked)
      </p>
      <ul class="space-y-1 text-sm">
        <li
          v-for="account in persona.accounts"
          :key="`${account.platformId}:${account.gameName}#${account.tagLine}`"
          :data-disputed="account.disputed || undefined"
          class="flex items-center gap-2 rounded px-2 py-1"
          :class="account.disputed ? 'bg-warning/10 ring-1 ring-warning/40' : ''"
        >
          <span class="text-highlighted truncate">{{ account.gameName }}<span class="text-dimmed">#{{ account.tagLine }}</span></span>
          <span class="font-mono text-xs text-muted">{{ account.platformId }}</span>
          <span class="ml-auto text-xs text-dimmed">{{ account.source }}{{ account.riotAccountId ? '' : ' · untracked' }}</span>
        </li>
        <li v-if="persona.accounts.length === 0" class="text-xs text-dimmed px-2">
          No account.
        </li>
      </ul>
    </div>

    <div>
      <p class="text-xs uppercase text-muted mb-1">
        Links · {{ persona.links.length }}
      </p>
      <ul class="space-y-1 text-sm">
        <li v-for="link in persona.links" :key="link.url" class="flex items-center gap-2 px-2">
          <span class="text-xs text-muted w-20 shrink-0">{{ link.kind }}</span>
          <a :href="link.url" target="_blank" rel="noopener noreferrer" class="truncate text-primary hover:underline">{{ link.url }}</a>
          <span class="ml-auto text-xs text-dimmed shrink-0">{{ link.source }}</span>
        </li>
        <li v-if="persona.links.length === 0" class="text-xs text-dimmed px-2">
          No link.
        </li>
      </ul>
    </div>
  </section>
</template>
