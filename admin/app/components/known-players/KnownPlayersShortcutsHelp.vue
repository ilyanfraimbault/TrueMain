<script setup lang="ts">
// The `?` overlay of the open conflict queue (#2293): every key it answers, printed
// from the same map the queue listens with.
import { shortcutsIn } from '~~/shared/utils/conflict-shortcuts'

const open = defineModel<boolean>('open', { required: true })

const GROUPS = [
  { title: 'In the queue', shortcuts: shortcutsIn('queue') },
  { title: 'In the compare', shortcuts: shortcutsIn('compare') },
]
</script>

<template>
  <UModal v-model:open="open" title="Keyboard shortcuts" description="Ignored while typing in a field. Actions that ask twice still ask: the key arms them, Enter sends them.">
    <template #body>
      <div class="space-y-5">
        <section v-for="group in GROUPS" :key="group.title" data-shortcut-group>
          <p class="mb-2 text-xs uppercase text-muted">
            {{ group.title }}
          </p>
          <ul class="space-y-1.5">
            <li v-for="shortcut in group.shortcuts" :key="shortcut.command" data-shortcut class="flex items-center justify-between gap-3 text-sm">
              <span class="text-default">{{ shortcut.label }}</span>
              <span class="flex shrink-0 gap-1">
                <UKbd v-for="kbd in shortcut.kbds" :key="kbd" :value="kbd" />
              </span>
            </li>
          </ul>
        </section>
      </div>
    </template>
  </UModal>
</template>
