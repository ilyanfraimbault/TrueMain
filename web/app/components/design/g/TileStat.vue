<script setup lang="ts">
import { formatCompactCount } from '~~/shared/utils/counts'

// A headline total on digit wheels: they roll in the first time the tile is on
// screen and roll once more whenever the cursor enters it. Compact form on
// purpose (`5.5M`, `formatCompactCount`): the claim is the order of magnitude,
// not a live counter. `#extra` sits at the tile's top right.
const props = defineProps<{
  area: string
  index: number
  to: string
  label: string
  icon: string
  value: number | null
  pending: boolean
}>()

const odometer = ref<{ spin: () => void } | null>(null)
const printed = computed(() => (props.value !== null && props.value > 0 ? formatCompactCount(props.value) : null))
</script>

<template>
  <DesignGTile
    :area="area"
    :index="index"
    :to="to"
    :label="label"
    :icon="icon"
    class="g-statile"
    @pointerenter="odometer?.spin()"
  >
    <slot name="extra" />
    <span class="g-statile__figure">
      <span
        v-if="pending"
        class="g-skel h-[0.8em] w-[2.6em]"
        aria-hidden="true"
      />
      <DesignGOdometer
        v-else-if="printed"
        ref="odometer"
        :value="printed"
      />
      <span
        v-else
        class="g-empty text-base"
      >—</span>
    </span>
  </DesignGTile>
</template>
