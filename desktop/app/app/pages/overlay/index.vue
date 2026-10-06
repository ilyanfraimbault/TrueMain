<script setup lang="ts">
import type { OverlayPanel, OverlayPanelSettings, OverlaySettings, OverlayShow, PanelTrigger } from '~/types/overlay'
import type { Point } from '~/utils/overlay-layout'
import { OVERLAY_OPACITY, OVERLAY_PANELS, OVERLAY_SCALE, PANEL_KEY } from '~/types/overlay'

/**
 * The in-game overlay (#1795), set up out of game (#1819): which panels show
 * and where, laid out on a small copy of the screen (`OverlayLayoutEditor`),
 * then what brings each panel up in game (always, a chord held or toggled,
 * #1915), and what goes for every panel — the next item's moment, size,
 * opacity, the hide shortcut. Each change is saved as it is made; the shell's answer is what
 * then shows.
 *
 * Not `/overlay/<panel>`: those are the panels' own windows (`[panel].vue`),
 * drawn outside the app's shell.
 */
const { view, save, preview, readBinds } = useGameOverlay()
const current = computed(() => view.value?.settings ?? null)

const SHOWS: { value: OverlayShow, label: string }[] = [{ value: 'always', label: 'Whole game' }, { value: 'whileDead', label: 'While dead' }]

function apply(change: Partial<OverlaySettings>) {
  void save(change)
}
function applyPanel(panel: OverlayPanel, change: Partial<OverlayPanelSettings>) {
  if (!current.value) return
  const key = PANEL_KEY[panel]
  apply({ [key]: { ...current.value[key], ...change } })
}
const place = (panel: OverlayPanel, custom: Point) => applyPanel(panel, { enabled: true, custom })
const show = (panel: OverlayPanel) => applyPanel(panel, { enabled: true })
const hide = (panel: OverlayPanel) => applyPanel(panel, { enabled: false })
const setTrigger = (panel: OverlayPanel, trigger: PanelTrigger) => applyPanel(panel, { trigger })

const shownPanels = computed(() => OVERLAY_PANELS.filter(panel => current.value?.[PANEL_KEY[panel]].enabled))
// The chords' warnings read the player's own League keybindings when the client is there.
onMounted(() => void readBinds())

/** Every panel back on its own spot. */
const moved = computed(() => OVERLAY_PANELS.some(panel => current.value?.[PANEL_KEY[panel]].custom))
function resetPositions() {
  if (!current.value) return
  const settings = current.value
  apply(Object.fromEntries(OVERLAY_PANELS.map(panel => [PANEL_KEY[panel], { ...settings[PANEL_KEY[panel]], custom: null }])))
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

// The on-screen preview lasts as long as this page.
onBeforeUnmount(() => void preview(false))
</script>

<template>
  <div class="flex h-full flex-col gap-4 overflow-y-auto p-5">
    <div class="flex items-center gap-3">
      <UIcon name="i-lucide-layers" class="size-6 text-primary" />
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">Overlay</h1>
      <USwitch
        v-if="current"
        class="ml-auto"
        :model-value="current.enabled"
        label="Show in game"
        :disabled="view?.supported === false"
        @update:model-value="apply({ enabled: $event })"
      />
    </div>
    <p class="-mt-2 text-sm text-muted">
      Over the game only — never over the client or another app. Click-through: the game keeps every click and every key.
    </p>

    <UAlert
      v-if="view && !view.supported"
      color="neutral"
      variant="subtle"
      icon="i-lucide-monitor-x"
      title="The overlay works on macOS and Windows."
      description="The game page shows the same panels in this window."
    />
    <UAlert v-else-if="view?.notice" color="neutral" variant="subtle" icon="i-lucide-info" :description="view.notice" />

    <USkeleton v-if="!current" class="aspect-video w-full rounded-lg" />
    <OverlayLayoutEditor
      v-else
      :settings="current"
      :chords="view?.chords"
      :class="!current.enabled && 'opacity-60'"
      @place="place"
      @show="show"
      @hide="hide"
    >
      <section class="flex flex-col gap-2">
        <h3 class="stat-label">On your screen</h3>
        <p class="text-xs text-muted">
          {{ view?.preview ? 'The panels are over your screen: drag each where it goes, then Done.' : 'Or show the panels over your real screen and drag them there.' }}
        </p>
        <div class="flex gap-2">
          <UButton
            :label="view?.preview ? 'Done' : 'Place on screen'"
            :icon="view?.preview ? 'i-lucide-check' : 'i-lucide-move'"
            size="xs"
            :color="view?.preview ? 'primary' : 'neutral'"
            :variant="view?.preview ? 'solid' : 'outline'"
            :disabled="!view?.supported"
            @click="preview(!view?.preview)"
          />
          <UButton v-if="moved" label="Reset positions" icon="i-lucide-rotate-ccw" size="xs" color="neutral" variant="ghost" @click="resetPositions" />
        </div>
      </section>

      <section v-if="current.nextItem.enabled" class="flex flex-col gap-2">
        <h3 class="stat-label">Next item shows</h3>
        <RecordingsSegmented :model-value="current.show" :items="SHOWS" @update:model-value="apply({ show: $event })" />
      </section>

      <section v-if="shownPanels.length" class="flex flex-col gap-3">
        <h3 class="stat-label">In game, shown</h3>
        <OverlayTriggerPicker
          v-for="panel in shownPanels"
          :key="panel"
          :panel="panel"
          :trigger="current[PANEL_KEY[panel]].trigger"
          :chord="view?.chords[panel] ?? null"
          @change="setTrigger(panel, $event)"
        />
        <p class="text-xs text-dimmed">
          The game gets the keys too: a shortcut never stops what they do in game.
          {{ view?.ownBinds ? 'Checked against your League keybindings.' : 'Checked against League\'s default keybindings.' }}
        </p>
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
        <div class="flex items-center gap-2 text-xs text-muted">
          <UKbd size="md">{{ view?.shortcut }}</UKbd>
          <span>hides it until the game ends; again to bring it back.</span>
        </div>
      </section>
    </OverlayLayoutEditor>
  </div>
</template>
