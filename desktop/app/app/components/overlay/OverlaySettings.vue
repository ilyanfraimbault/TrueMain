<script setup lang="ts">
import type { OverlayAnchor, OverlaySettings, OverlayShow } from '~/types/overlay'
import { OVERLAY_OPACITY, OVERLAY_SCALE } from '~/types/overlay'

/**
 * The in-game overlay's settings (#1795): on or off, when it shows, where,
 * how large and how see-through. Each change is
 * saved as it is made; the shell's answer is what then shows.
 *
 * Where it sits is chosen two ways: one of four spots on a small screen, or
 * anywhere by dragging the panel itself while the preview shows it on screen
 * — the only time it takes the mouse. Closing the panel ends the preview.
 */
const open = defineModel<boolean>('open', { default: false })

const { view, save, preview } = useGameOverlay()
const current = computed(() => view.value?.settings ?? null)

const SHOWS: { value: OverlayShow, label: string }[] = [{ value: 'always', label: 'Whole game' }, { value: 'whileDead', label: 'While dead' }]
const ANCHORS: { value: OverlayAnchor, label: string, spot: string }[] = [
  { value: 'top-left', label: 'Top left', spot: 'left-1.5 top-2' },
  { value: 'top-right', label: 'Top right', spot: 'right-1.5 top-2' },
  { value: 'center-left', label: 'Middle left', spot: 'left-1.5 top-1/2 -translate-y-1/2' },
  { value: 'center-right', label: 'Middle right', spot: 'right-1.5 top-1/2 -translate-y-1/2' },
]

const place = computed(() => {
  if (!current.value) return ''
  if (current.value.custom) return 'Where you dragged it'
  return ANCHORS.find(anchor => anchor.value === current.value!.anchor)?.label ?? ''
})

function apply(change: Partial<OverlaySettings>) {
  void save(change)
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
  <USlideover v-model:open="open" title="In-game overlay" description="Over the game only — never over the client or another app." :ui="{ content: 'max-w-sm', body: 'flex flex-col gap-6' }">
    <template #body>
      <div v-if="view && !view.supported" class="flex items-start gap-2 text-sm">
        <UIcon name="i-lucide-monitor-x" class="mt-0.5 size-4 shrink-0 text-muted" />
        <p class="text-muted">The overlay is macOS-only for now. The game page shows the same panel in this window.</p>
      </div>

      <template v-if="current">
        <USwitch
          :model-value="current.enabled"
          label="Show the overlay in game"
          description="Click-through: the game keeps every click and every key."
          @update:model-value="apply({ enabled: $event })"
        />

        <div class="flex flex-col gap-6" :class="!current.enabled && 'opacity-50'">
          <section class="flex flex-col gap-2">
            <h3 class="stat-label">When</h3>
            <RecordingsSegmented :model-value="current.show" :items="SHOWS" @update:model-value="apply({ show: $event })" />
            <p class="text-xs text-muted">
              <template v-if="current.show === 'whileDead'">Shows while your champion is dead — when the shop is the decision at hand.</template>
              <template v-else>Shows from the moment the game has loaded to its end.</template>
            </p>
          </section>

          <section class="flex flex-col gap-2">
            <div class="flex items-baseline justify-between">
              <h3 class="stat-label">Position</h3>
              <span class="text-xs text-muted">{{ place }}</span>
            </div>
            <div class="flex items-center gap-4">
              <!-- The screen, small: a spot per anchor, and the dragged place when there is one. -->
              <div class="relative aspect-video w-36 shrink-0 rounded-md bg-elevated ring-1 ring-default">
                <button
                  v-for="anchor in ANCHORS"
                  :key="anchor.value"
                  type="button"
                  :aria-label="anchor.label"
                  :title="anchor.label"
                  class="absolute h-3.5 w-7 rounded-sm ring-1 transition-colors"
                  :class="[anchor.spot, !current.custom && current.anchor === anchor.value ? 'bg-primary ring-primary' : 'bg-accented ring-default hover:ring-primary/60']"
                  @click="apply({ anchor: anchor.value, custom: null })"
                />
                <span
                  v-if="current.custom"
                  class="absolute h-3.5 w-7 -translate-x-1/2 -translate-y-1/2 rounded-sm bg-primary ring-1 ring-primary"
                  :style="{ left: `${current.custom.x * 100}%`, top: `${current.custom.y * 100}%` }"
                />
              </div>
              <div class="flex flex-col items-start gap-1.5">
                <UButton
                  :label="view?.preview ? 'Done placing' : 'Place on screen'"
                  :icon="view?.preview ? 'i-lucide-check' : 'i-lucide-move'"
                  size="xs"
                  :color="view?.preview ? 'primary' : 'neutral'"
                  :variant="view?.preview ? 'solid' : 'outline'"
                  :disabled="!view?.supported"
                  @click="preview(!view?.preview)"
                />
                <p class="text-xs text-muted">{{ view?.preview ? 'The overlay is on your screen: drag it where it goes over your game.' : 'Shows it on screen to drag it anywhere.' }}</p>
              </div>
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
              <span>hides it until the game ends; again to bring it back.</span>
            </div>
          </section>
        </div>
      </template>
    </template>
  </USlideover>
</template>
