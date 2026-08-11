<script setup lang="ts">
// Mirrors the tier list's SectionCard groups so the loading state keeps the
// real card chrome (badge header + wrapped portraits) and doesn't shift when
// the data resolves. Each number is a group's portrait count — tapered to read
// like a plausible S→D tier spread rather than uniform blocks.
const GROUPS = [12, 18, 15, 10, 6]
</script>

<template>
  <div
    class="space-y-3"
    aria-hidden="true"
  >
    <SectionCard
      v-for="(count, group) in GROUPS"
      :key="group"
    >
      <template #title>
        <div class="flex items-center gap-2">
          <USkeleton class="size-6 rounded" />
          <USkeleton class="h-3 w-20" />
        </div>
      </template>

      <ul class="flex flex-wrap gap-3">
        <li
          v-for="i in count"
          :key="i"
        >
          <USkeleton class="size-12 rounded-lg" />
        </li>
      </ul>
    </SectionCard>
  </div>
</template>
