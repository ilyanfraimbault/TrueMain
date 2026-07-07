<script setup lang="ts">
// Mirrors the tier list's SectionCard groups so the loading state keeps the
// real card chrome (badge header + wrapped entry pills) and doesn't shift when
// the data resolves. Each number is a group's pill count — tapered to read like
// a plausible S→D tier spread rather than uniform blocks.
const GROUPS = [6, 9, 7, 5, 3]
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

      <ul class="flex flex-wrap gap-2">
        <li
          v-for="i in count"
          :key="i"
        >
          <div class="flex items-center gap-2 rounded-md border border-default/60 bg-elevated/40 px-2 py-1.5">
            <USkeleton class="size-8 rounded" />
            <div class="flex flex-col gap-1">
              <USkeleton class="h-4 w-20" />
              <USkeleton class="h-3 w-24" />
            </div>
          </div>
        </li>
      </ul>
    </SectionCard>
  </div>
</template>
