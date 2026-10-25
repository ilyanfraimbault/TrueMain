<script setup lang="ts">
// One known player of a homonyms group (#2273), on one line so the group reads
// top-down: who it is (name, slug, source, created), its accounts as Riot ID chips
// (untracked ones dimmed) and its channels as labelled chips — the ones another
// player of the group lists too marked, the strongest hint the two are one person.
import type { PersonaOps } from '~~/shared/types/ops'
import { computed } from 'vue'
import { linkHandle, linkKey } from '~~/shared/utils/persona-homonyms'

const props = defineProps<{
  persona: PersonaOps
  kept: boolean
  included: boolean
  busy: boolean
  /** Link keys another player of the group lists too. */
  sharedLinks: string[]
  /** "Different from …" when pairs of the group are already marked. */
  note: string
}>()

defineEmits<{ keep: [], toggle: [] }>()

const links = computed(() => props.persona.links.map(link => ({
  ...link,
  handle: linkHandle(link.url),
  shared: props.sharedLinks.includes(linkKey(link.url)),
})))
</script>

<template>
  <div
    :data-persona="persona.slug"
    class="grid gap-x-6 gap-y-2 px-3 py-3 md:grid-cols-[15rem_minmax(0,1fr)_minmax(0,1fr)]"
    :class="[kept ? 'bg-primary/5' : '', included ? '' : 'opacity-50']"
  >
    <div class="min-w-0 space-y-1">
      <div class="flex items-center gap-1 -ml-1.5">
        <UButton
          data-keep
          size="xs"
          :icon="kept ? 'i-lucide-crown' : 'i-lucide-circle'"
          :color="kept ? 'primary' : 'neutral'"
          :variant="kept ? 'soft' : 'ghost'"
          :label="kept ? 'Kept' : 'Keep'"
          :disabled="busy"
          @click="$emit('keep')"
        />
        <UButton
          data-select
          size="xs"
          :icon="included ? 'i-lucide-square-check' : 'i-lucide-square'"
          color="neutral"
          variant="ghost"
          :label="included ? 'Included' : 'Left out'"
          :disabled="busy || kept"
          @click="$emit('toggle')"
        />
      </div>
      <p class="truncate text-sm font-semibold text-highlighted">
        {{ persona.displayName }}
      </p>
      <p class="truncate font-mono text-xs text-muted">
        {{ persona.slug }}
      </p>
      <p class="text-xs text-dimmed">
        {{ persona.source }} · {{ persona.createdAtUtc.slice(0, 10) }}
      </p>
      <p v-if="note" class="text-xs text-warning">
        {{ note }}
      </p>
    </div>

    <div class="min-w-0">
      <p class="mb-1 text-xs text-muted md:hidden">
        Accounts
      </p>
      <div class="flex flex-wrap gap-1.5">
        <span
          v-for="account in persona.accounts"
          :key="`${account.platformId}:${account.gameName}#${account.tagLine}`"
          data-account
          class="inline-flex max-w-full items-center gap-1 rounded-md px-2 py-0.5 text-xs ring-1"
          :class="account.riotAccountId ? 'text-highlighted ring-default' : 'text-muted ring-default/60 ring-dashed'"
          :title="account.riotAccountId ? `Tracked · listed by ${account.source}` : `Not tracked · listed by ${account.source}`"
        >
          <span class="truncate">{{ account.gameName }}<span class="text-dimmed">#{{ account.tagLine }}</span></span>
          <span class="font-mono text-[10px] text-dimmed">{{ account.platformId }}</span>
        </span>
        <span v-if="persona.accounts.length === 0" class="text-xs text-dimmed">No account</span>
      </div>
    </div>

    <div class="min-w-0">
      <p class="mb-1 text-xs text-muted md:hidden">
        Links
      </p>
      <div class="flex flex-wrap gap-1.5">
        <a
          v-for="link in links"
          :key="link.url"
          :href="link.url"
          target="_blank"
          rel="noopener noreferrer"
          :data-shared="link.shared || undefined"
          :title="`${link.url} · ${link.source}`"
          class="inline-flex max-w-full items-center gap-1 rounded-md px-2 py-0.5 text-xs ring-1 hover:underline"
          :class="link.shared ? 'bg-success/10 text-success ring-success/40' : 'text-default ring-default'"
        >
          <UIcon v-if="link.shared" name="i-lucide-link" class="size-3 shrink-0" />
          <span class="text-muted">{{ link.kind }}</span>
          <span class="truncate">{{ link.handle }}</span>
        </a>
        <span v-if="persona.links.length === 0" class="text-xs text-dimmed">No link</span>
      </div>
    </div>
  </div>
</template>
