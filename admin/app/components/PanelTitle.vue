<script setup lang="ts">
// The portal's panel header (#1414): a title, at most one line under it, and an
// info control holding everything longer.
//
// The rule this component exists to enforce is "a panel answers in one line".
// Before it, most cards opened on a three-line caption and ended on the figure,
// so the operator read the caveats before the state. Nothing was wrong with the
// prose — it explains real measurement subtleties — but printed on every visit
// it buried the answer. Here the explanation moves behind the `i-lucide-info`
// button: still one keystroke away, no longer in the way.
//
// `variant` covers the two header shapes the portal already uses: `title` for a
// card header (a named panel), `label` for a section label inside a card (a
// chart's caption, a footer group). Both share the info control.

defineProps<{
  title: string
  /** One line, at most. Anything longer belongs in `info`. */
  subtitle?: string
  /** The explanation, shown in the info popover. `#info` wins over this. */
  info?: string
  variant?: 'title' | 'label'
}>()

defineSlots<{
  /** Rich replacement for `info` (markup, interpolated figures). */
  info?: () => unknown
  /** Rich replacement for `subtitle` — still one line. */
  subtitle?: () => unknown
}>()
</script>

<template>
  <div class="min-w-0">
    <div class="flex items-center gap-1">
      <p
        :class="variant === 'label'
          ? 'text-xs text-muted uppercase'
          : 'text-sm font-medium text-highlighted'"
      >
        {{ title }}
      </p>
      <UPopover v-if="info || $slots.info">
        <UButton
          icon="i-lucide-info"
          color="neutral"
          variant="ghost"
          size="xs"
          :aria-label="`About ${title}`"
          :ui="{ base: '-my-1 p-0.5 text-dimmed hover:text-muted' }"
        />
        <template #content>
          <div class="max-w-sm space-y-2 p-3 text-xs text-muted">
            <slot name="info">
              {{ info }}
            </slot>
          </div>
        </template>
      </UPopover>
    </div>
    <p v-if="subtitle || $slots.subtitle" class="mt-0.5 text-xs text-dimmed">
      <slot name="subtitle">
        {{ subtitle }}
      </slot>
    </p>
  </div>
</template>
