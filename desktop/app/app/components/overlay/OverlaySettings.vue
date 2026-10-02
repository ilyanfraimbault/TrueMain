<script setup lang="ts">
import type { OverlayAnchor, OverlayPanel, OverlaySettings, OverlayShow } from '~/types/overlay'
import { OVERLAY_OPACITY, OVERLAY_PANELS, OVERLAY_SCALE, PANEL_KEY } from '~/types/overlay'

/**
 * The in-game overlay's settings (#1795): the overlay on or off, then each
 * panel — on or off and where it sits — the next item's moment, and every
 * panel's size and opacity. Each change is saved as it is made; the shell's
 * answer is what then shows.
 *
 * Where a panel sits is chosen two ways: one of five spots on a small screen,
 * or anywhere by dragging the panel itself while the preview shows them all
 * on screen — the only time they take the mouse. Closing this ends the
 * preview.
 */
const open = defineModel<boolean>('open', { default: false })

const { view, save, preview } = useGameOverlay()
const current = computed(() => view.value?.settings ?? null)

const SHOWS: { value: OverlayShow, label: string }[] = [{ value: 'always', label: 'Whole game' }, { value: 'whileDead', label: 'While dead' }]
const PANELS: Record<OverlayPanel, { label: string, description: string }> = {
  'loading': { label: 'Loading screen', description: 'Each player\'s games and win rate on their champion, and their ranked streak.' },
  'next-item': { label: 'Next item', description: 'The next item to buy, and the gold it still needs.' },
  'win-probability': { label: 'Win probability', description: 'Each side\'s chance to win, estimated from the item-gold gap. On screen the whole game.' },
  'stats': { label: 'Your pace', description: 'CS per minute with its curve, and gold per minute.' },
  'item-value': { label: 'Item value', description: 'While TAB is held: what each team\'s items are worth, and each lane\'s gap.' },
}

function apply(change: Partial<OverlaySettings>) {
  void save(change)
}
function applyPanel(panel: OverlayPanel, change: Partial<OverlaySettings['nextItem']>) {
  if (!current.value) return
  const key = PANEL_KEY[panel]
  apply({ [key]: { ...current.value[key], ...change } })
}
function pick(panel: OverlayPanel, anchor: OverlayAnchor) {
  applyPanel(panel, { anchor, custom: null })
}

// Sliders save once they settle, not on every step of a drag.
const scale = ref(1)
const opacity = ref(0.95)
watch(() => current.value?.scale, value => (scale.value = value ?? 1), { immediate: true })
watch(() => current.value?.opacity, value => (opacity.value = value ?? 0.95), { immediate: true })
let timer: ReturnType<typeof setTimeout> | undefined
function settle(change: Partial<OverlaySettings>) {
  clearTimeout(timer)
  timer = setTimeout(() => apply(change), 250)
}
onBeforeUnmount(() => clearTimeout(timer))

watch(open, (isOpen) => {
  if (!isOpen) void preview(false)
})
onBeforeUnmount(() => void preview(false))
</script>

<template>
  <USlideover v-model:open="open" title="In-game overlay" description="Over the game only — never over the client or another app." :ui="{ content: 'max-w-md', body: 'flex flex-col gap-6' }">
    <template #body>
      <div v-if="view && !view.supported" class="flex items-start gap-2 text-sm">
        <UIcon name="i-lucide-monitor-x" class="mt-0.5 size-4 shrink-0 text-muted" />
        <p class="text-muted">The overlay works on macOS and Windows. The game page shows the next item in this window.</p>
      </div>
      <div v-else-if="view?.notice" class="flex items-start gap-2 text-sm">
        <UIcon name="i-lucide-info" class="mt-0.5 size-4 shrink-0 text-muted" />
        <p class="text-muted">{{ view.notice }}</p>
      </div>

      <template v-if="current">
        <USwitch
          :model-value="current.enabled"
          label="Show the overlay in game"
          description="Click-through: the game keeps every click and every key."
          @update:model-value="apply({ enabled: $event })"
        />

        <div class="flex flex-col gap-6" :class="!current.enabled && 'opacity-50'">
          <section class="flex flex-col gap-4">
            <h3 class="stat-label">Panels</h3>
            <div v-for="panel in OVERLAY_PANELS" :key="panel" class="flex items-start gap-4">
              <div class="flex min-w-0 flex-1 flex-col gap-2">
                <USwitch
                  :model-value="current[PANEL_KEY[panel]].enabled"
                  :label="PANELS[panel].label"
                  :description="PANELS[panel].description"
                  @update:model-value="applyPanel(panel, { enabled: $event })"
                />
                <RecordingsSegmented
                  v-if="panel === 'next-item' && current.nextItem.enabled"
                  class="ml-11"
                  :model-value="current.show"
                  :items="SHOWS"
                  @update:model-value="apply({ show: $event })"
                />
              </div>
              <OverlayAnchorPicker
                :settings="current[PANEL_KEY[panel]]"
                :class="!current[PANEL_KEY[panel]].enabled && 'pointer-events-none opacity-40'"
                @pick="pick(panel, $event)"
              />
            </div>

            <div class="flex items-center gap-3 rounded-lg bg-elevated/60 px-3 py-2 ring-1 ring-default">
              <p class="flex-1 text-xs text-muted">
                {{ view?.preview ? 'The panels are on your screen: drag each where it goes over your game.' : 'Or show the panels on screen and drag each anywhere.' }}
              </p>
              <UButton
                :label="view?.preview ? 'Done' : 'Place on screen'"
                :icon="view?.preview ? 'i-lucide-check' : 'i-lucide-move'"
                size="xs"
                :color="view?.preview ? 'primary' : 'neutral'"
                :variant="view?.preview ? 'solid' : 'outline'"
                :disabled="!view?.supported"
                @click="preview(!view?.preview)"
              />
            </div>
          </section>

          <section class="flex flex-col gap-2">
            <div class="flex items-baseline justify-between">
              <h3 class="stat-label">Size</h3>
              <span class="stat-value text-sm">{{ Math.round(scale * 100) }}%</span>
            </div>
            <USlider
              :model-value="scale"
              :min="OVERLAY_SCALE.min"
              :max="OVERLAY_SCALE.max"
              :step="0.05"
              size="sm"
              @update:model-value="(value) => { scale = value ?? scale; settle({ scale }) }"
            />
          </section>

          <section class="flex flex-col gap-2">
            <div class="flex items-baseline justify-between">
              <h3 class="stat-label">Opacity</h3>
              <span class="stat-value text-sm">{{ Math.round(opacity * 100) }}%</span>
            </div>
            <USlider
              :model-value="opacity"
              :min="OVERLAY_OPACITY.min"
              :max="OVERLAY_OPACITY.max"
              :step="0.05"
              size="sm"
              @update:model-value="(value) => { opacity = value ?? opacity; settle({ opacity }) }"
            />
          </section>

          <section class="flex flex-col gap-2">
            <h3 class="stat-label">Shortcut</h3>
            <div class="flex items-center gap-2 text-sm text-muted">
              <UKbd size="md">{{ view?.shortcut }}</UKbd>
              <span>hides the overlay until the game ends; again to bring it back.</span>
            </div>
          </section>
        </div>
      </template>
    </template>
  </USlideover>
</template>
