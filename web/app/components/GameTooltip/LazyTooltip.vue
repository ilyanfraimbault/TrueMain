<script setup lang="ts">
/**
 * A `UTooltip` that only exists once the pointer has been over its trigger (#1585).
 *
 * A champion page renders a few hundred item, rune and spell icons, and every
 * `UTooltip` is a Reka tooltip root, trigger and context with Nuxt UI's prop and
 * class machinery on top — measured, that per-icon machinery was a large share of
 * the page's main-thread work, for tooltips nobody hovers. Until the first hover
 * the icon renders bare; the first `mouseover` mounts the real tooltip around it.
 *
 * Why this does not break "a tooltip trigger keeps the same DOM element"
 * (decisions/web-frontend-rules.md): Reka snapshots its trigger when the tooltip
 * mounts, and here it mounts once, on the element it keeps for good. Reka opens on
 * `pointermove`, not `pointerenter`, so the pointer already resting on the icon
 * opens it on its next move, with the usual delay — nothing is opened by hand,
 * so a fast sweep across a row of icons cannot leave one stuck open.
 */
defineOptions({ inheritAttrs: false })

defineProps<{
  disabled?: boolean
}>()

// Armed on any hover, even while `disabled`: the icon's data (item, rune and spell
// maps) can land after the pointer arrived, and a pointer resting on the icon sends
// no second `mouseover`. Once armed, `UTooltip` owns `disabled` and opens on the next
// move after the data is there.
const armed = ref(false)

function arm() {
  armed.value = true
}
</script>

<template>
  <UTooltip
    v-if="armed"
    v-bind="$attrs"
    :disabled="disabled"
  >
    <slot />
    <template
      v-if="$slots.content"
      #content
    >
      <slot name="content" />
    </template>
  </UTooltip>
  <!-- `display: contents`: the wrapper generates no box, so the icon lays out
       exactly as it will inside the tooltip. `mouseover` bubbles from the icon. -->
  <span
    v-else
    class="contents"
    @mouseover="arm"
  >
    <slot />
  </span>
</template>
