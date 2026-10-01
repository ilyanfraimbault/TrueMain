<script setup lang="ts">
import type { DesktopPlatform } from '~~/shared/types/desktop'

// The one-time trust prompt of an unsigned beta (#1719), both platforms side by
// side as numbered steps. The visitor's own platform, once the page has read it,
// comes first and is ringed; until then — or on a phone — macOS leads.
const props = defineProps<{
  platform: DesktopPlatform | null
}>()

const GUIDES: Record<DesktopPlatform, { label: string, icon: string }> = {
  mac: { label: 'On macOS', icon: 'i-simple-icons-apple' },
  windows: { label: 'On Windows', icon: 'i-simple-icons-windows' },
}

/** The rose-gold disc each step's number sits in. */
const STEP_NUMBER = 'flex size-6 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold tabular-nums text-primary ring-1 ring-primary/30'

const order = computed<DesktopPlatform[]>(() => (props.platform === 'windows' ? ['windows', 'mac'] : ['mac', 'windows']))
</script>

<template>
  <div class="grid gap-4 md:grid-cols-2">
    <article
      v-for="target in order"
      :key="target"
      class="surface rounded-xl p-5 sm:p-6"
      :class="target === platform ? 'ring-1 ring-primary/40' : ''"
    >
      <header class="flex items-center gap-2.5">
        <UIcon
          :name="GUIDES[target].icon"
          class="size-5 text-highlighted"
        />
        <h3 class="font-semibold text-highlighted">
          {{ GUIDES[target].label }}
        </h3>
        <UBadge
          v-if="target === platform"
          color="primary"
          variant="subtle"
          size="sm"
          label="Your computer"
          class="ml-auto"
        />
      </header>
      <ol
        v-if="target === 'mac'"
        class="mt-4 space-y-3 text-sm leading-relaxed text-muted [&_code]:text-default [&_strong]:font-medium [&_strong]:text-highlighted"
      >
        <li class="flex gap-3">
          <span aria-hidden="true" :class="STEP_NUMBER">1</span>
          <span>Open the downloaded <code>.dmg</code> and drag TrueMain into Applications.</span>
        </li>
        <li class="flex gap-3">
          <span aria-hidden="true" :class="STEP_NUMBER">2</span>
          <span>Open TrueMain. macOS says it cannot check it for malicious software: choose <strong>Done</strong>.</span>
        </li>
        <li class="flex gap-3">
          <span aria-hidden="true" :class="STEP_NUMBER">3</span>
          <span>Open <strong>System Settings → Privacy &amp; Security</strong>, scroll to Security and choose <strong>Open Anyway</strong> next to TrueMain, then confirm.</span>
        </li>
      </ol>
      <ol
        v-else
        class="mt-4 space-y-3 text-sm leading-relaxed text-muted [&_code]:text-default [&_strong]:font-medium [&_strong]:text-highlighted"
      >
        <li class="flex gap-3">
          <span aria-hidden="true" :class="STEP_NUMBER">1</span>
          <span>Run the downloaded installer.</span>
        </li>
        <li class="flex gap-3">
          <span aria-hidden="true" :class="STEP_NUMBER">2</span>
          <span>If Windows shows <strong>“Windows protected your PC”</strong>, choose <strong>More info</strong>, then <strong>Run anyway</strong>.</span>
        </li>
        <li class="flex gap-3">
          <span aria-hidden="true" :class="STEP_NUMBER">3</span>
          <span>The installer finishes on its own and TrueMain opens.</span>
        </li>
      </ol>
    </article>
  </div>
</template>

