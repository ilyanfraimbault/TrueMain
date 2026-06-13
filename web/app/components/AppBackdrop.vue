<script setup lang="ts">
// App-wide ambient backdrop. Sits fixed behind every page (`-z-10`) so the
// surface is never a flat slab of neutral-900 — a soft emerald glow anchors
// the top of the viewport and a faint, masked dot grid adds texture that
// fades out before it becomes busy. Pages render their own (mostly
// translucent) surfaces on top, so this reads through the gaps between rows
// and sections. The homepage hero layers its animated aurora above this.
//
// Purely decorative and static (no JS), emerald-only per the project's
// palette rule, and tuned subtle enough to stay legible in both colour modes.
</script>

<template>
  <div
    aria-hidden="true"
    class="pointer-events-none fixed inset-0 -z-10 overflow-hidden"
  >
    <!-- Primary glow: emerald wash anchored just above the top edge. -->
    <div
      class="absolute inset-x-0 top-0 h-[75vh]"
      style="background: radial-gradient(60% 50% at 50% -8%, color-mix(in oklch, var(--ui-color-primary-500) 13%, transparent), transparent 72%);"
    />
    <!-- Secondary off-axis glow for depth — keeps the field from reading as a
         single centred halo. -->
    <div
      class="absolute right-0 top-[12vh] h-[55vh] w-[55vw]"
      style="background: radial-gradient(50% 50% at 80% 0%, color-mix(in oklch, var(--ui-color-primary-400) 7%, transparent), transparent 70%);"
    />
    <!-- Faint dot grid, masked to a top-weighted band so the texture is felt,
         not counted. -->
    <div
      class="absolute inset-0 opacity-60 [background-image:radial-gradient(color-mix(in_oklch,var(--ui-color-primary-400)_11%,transparent)_1px,transparent_1px)] [background-size:30px_30px] [mask-image:radial-gradient(120%_80%_at_50%_-10%,black,transparent_70%)]"
    />
  </div>
</template>
