<script setup lang="ts">
// The two known players of a conflict, lined up (#2275): one column each, and every
// account and link on the row of its counterpart, so the same channel on both sides
// reads as one line marked identical and two channels of one kind as one line marked
// different. What the operator reads to tell a duplicate from a wrong account.
import type { PersonaOps } from '~~/shared/types/ops'
import type { CompareMatch } from '~~/shared/utils/persona-compare'
import { computed } from 'vue'
import { compareAccounts, compareLinks, compareSummary, compareVerdict } from '~~/shared/utils/persona-compare'
import { linkHandle } from '~~/shared/utils/persona-homonyms'

const props = defineProps<{
  /** The known player whose source lists the account. */
  claimant: PersonaOps
  /** The known player holding it. */
  owner: PersonaOps
}>()

const accounts = computed(() => compareAccounts(props.claimant, props.owner))
const links = computed(() => compareLinks(props.claimant, props.owner))
const verdict = computed(() => compareVerdict(compareSummary(accounts.value, links.value), links.value))

const sides = computed(() => [
  { label: 'Claims it', persona: props.claimant },
  { label: 'Holds it', persona: props.owner },
])

const MARK: Record<CompareMatch, { icon: string, class: string, title: string } | null> = {
  same: { icon: 'i-lucide-equal', class: 'text-success', title: 'Identical on both' },
  differs: { icon: 'i-lucide-not-equal', class: 'text-warning', title: 'Same kind, different channel' },
  only: null,
}

const ROW_TONE: Record<CompareMatch, string> = {
  same: 'bg-success/10',
  differs: 'bg-warning/10',
  only: '',
}

const VERDICT_TONE = { success: 'text-success', warning: 'text-warning', neutral: 'text-muted' }
</script>

<template>
  <section class="space-y-4">
    <p data-verdict class="flex items-center gap-1.5 text-sm" :class="VERDICT_TONE[verdict.tone]">
      <UIcon :name="verdict.tone === 'success' ? 'i-lucide-link' : verdict.tone === 'warning' ? 'i-lucide-triangle-alert' : 'i-lucide-info'" class="size-4 shrink-0" />
      {{ verdict.text }}
    </p>

    <div class="overflow-hidden rounded-lg ring-1 ring-default">
      <div class="grid grid-cols-[minmax(0,1fr)_2rem_minmax(0,1fr)] border-b border-default bg-elevated/50">
        <template v-for="(side, index) in sides" :key="side.label">
          <span v-if="index === 1" />
          <header class="min-w-0 px-3 py-2">
            <p class="text-xs uppercase text-muted">
              {{ side.label }}
            </p>
            <p class="truncate text-base font-semibold text-highlighted">
              {{ side.persona.displayName }}
            </p>
            <p class="truncate text-xs text-muted">
              <span class="font-mono">{{ side.persona.slug }}</span> · from {{ side.persona.source }}
            </p>
          </header>
        </template>
      </div>

      <p class="border-b border-default px-3 py-1.5 text-xs uppercase text-muted">
        Accounts
      </p>
      <div class="divide-y divide-default">
        <div
          v-for="row in accounts"
          :key="row.key"
          data-account-row
          :data-match="row.match"
          class="grid grid-cols-[minmax(0,1fr)_2rem_minmax(0,1fr)] items-center text-sm"
          :class="row.disputed ? 'bg-warning/10' : ROW_TONE[row.match]"
        >
          <template v-for="(cell, index) in [row.left, row.right]" :key="index">
            <span v-if="index === 1" class="flex justify-center">
              <UIcon v-if="MARK[row.match]" :name="MARK[row.match]!.icon" :class="MARK[row.match]!.class" class="size-4" />
            </span>
            <div class="flex min-w-0 items-center gap-2 px-3 py-1.5">
              <template v-if="cell">
                <span class="truncate text-highlighted">{{ cell.gameName }}<span class="text-dimmed">#{{ cell.tagLine }}</span></span>
                <span class="font-mono text-xs text-muted">{{ cell.platformId }}</span>
                <span class="ml-auto shrink-0 text-xs text-dimmed">{{ cell.source }}{{ cell.riotAccountId ? '' : ' · untracked' }}</span>
              </template>
              <span v-else-if="row.disputed" class="text-xs text-warning">claims this account</span>
              <span v-else class="text-xs text-dimmed">—</span>
            </div>
          </template>
        </div>
        <p v-if="accounts.length === 0" class="px-3 py-1.5 text-xs text-dimmed">
          No account on either side.
        </p>
      </div>

      <p class="border-y border-default px-3 py-1.5 text-xs uppercase text-muted">
        Links
      </p>
      <div class="divide-y divide-default">
        <div
          v-for="row in links"
          :key="row.key"
          data-link-row
          :data-match="row.match"
          class="grid grid-cols-[minmax(0,1fr)_2rem_minmax(0,1fr)] items-center text-sm"
          :class="ROW_TONE[row.match]"
        >
          <template v-for="(cell, index) in [row.left, row.right]" :key="index">
            <span v-if="index === 1" class="flex justify-center" :title="MARK[row.match]?.title">
              <UIcon v-if="MARK[row.match]" :name="MARK[row.match]!.icon" :class="MARK[row.match]!.class" class="size-4" />
            </span>
            <div class="flex min-w-0 items-center gap-2 px-3 py-1.5">
              <template v-if="cell">
                <span class="w-20 shrink-0 text-xs text-muted">{{ row.kind }}</span>
                <a
                  :href="cell.url"
                  target="_blank"
                  rel="noopener noreferrer"
                  :title="cell.url"
                  class="truncate text-primary hover:underline"
                >{{ linkHandle(cell.url, 28) }}</a>
                <span class="ml-auto shrink-0 text-xs text-dimmed">{{ cell.source }}</span>
              </template>
              <span v-else class="text-xs text-dimmed">—</span>
            </div>
          </template>
        </div>
        <p v-if="links.length === 0" class="px-3 py-1.5 text-xs text-dimmed">
          No link on either side.
        </p>
      </div>
    </div>
  </section>
</template>
