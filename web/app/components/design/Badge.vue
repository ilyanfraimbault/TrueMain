<script setup lang="ts">
// Design exploration (prototype): names the variant this port serves and lets
// the reviewer force the FX level, to judge the lighter rendering too.
const design = useDesignVariant()
const fx = useFx()
const info = computed(() => (design ? DESIGN_VARIANTS[design] : null))
const mounted = ref(false)
onMounted(() => (mounted.value = true))

function toggle() {
  setFxLevel(fx.value.level === 'full' ? 'lite' : 'full')
  window.location.reload()
}
</script>

<template>
  <div
    v-if="info"
    class="fixed bottom-3 left-3 z-[60] flex items-center gap-2 rounded-full bg-black/70 py-1 pl-3 pr-1 text-[11px] text-white/80 shadow-lg ring-1 ring-white/10 backdrop-blur-md"
  >
    <span class="font-semibold uppercase tracking-wider text-white">{{ design }}</span>
    <span>{{ info.name }}</span>
    <button
      v-if="mounted"
      type="button"
      class="rounded-full bg-white/10 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wider text-white/80 transition hover:bg-white/20"
      :title="fx.refraction ? 'Refraction on' : 'Refraction off (Chromium + full only)'"
      @click="toggle"
    >
      fx: {{ fx.level }}{{ fx.refraction ? ' · refr' : '' }}
    </button>
  </div>
</template>
