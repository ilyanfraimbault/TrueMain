<script setup lang="ts">
// Mirrors MatchRow.vue's column shape so the skeleton placeholders line up
// visually with the eventual content — height, padding and gap match the
// real row to avoid layout shift on resolve.
</script>

<template>
  <article
    class="flex overflow-hidden rounded-lg bg-elevated/30"
    aria-hidden="true"
  >
    <div class="w-1 shrink-0 bg-default/40" />
    <div class="flex flex-1 flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
      <div class="flex w-24 shrink-0 flex-col gap-1">
        <USkeleton class="h-3 w-14" />
        <USkeleton class="h-3 w-10" />
        <USkeleton class="h-3 w-12" />
        <USkeleton class="h-3 w-10" />
        <USkeleton class="h-3 w-12" />
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <USkeleton class="size-14 rounded" />
        <div class="flex flex-col gap-1">
          <USkeleton class="size-6 rounded" />
          <USkeleton class="size-6 rounded" />
        </div>
        <div class="flex flex-col gap-1">
          <USkeleton class="size-6 rounded-full" />
          <USkeleton class="size-6 rounded-full" />
        </div>
      </div>
      <div class="flex min-w-[7rem] flex-col items-center gap-1">
        <USkeleton class="h-5 w-20" />
        <USkeleton class="h-3 w-14" />
        <USkeleton class="h-3 w-20" />
        <USkeleton class="h-3 w-12" />
      </div>
      <div class="grid shrink-0 grid-cols-4 gap-1">
        <USkeleton v-for="i in 7" :key="i" class="size-7 rounded" />
      </div>
      <div class="ml-auto grid grid-cols-2 gap-x-2">
        <div class="flex flex-col gap-0.5">
          <div v-for="i in 5" :key="`b${i}`" class="flex items-center gap-1">
            <USkeleton class="size-4 rounded-sm" />
            <USkeleton class="h-3 w-14" />
          </div>
        </div>
        <div class="flex flex-col gap-0.5">
          <div v-for="i in 5" :key="`r${i}`" class="flex items-center gap-1">
            <USkeleton class="size-4 rounded-sm" />
            <USkeleton class="h-3 w-14" />
          </div>
        </div>
      </div>
    </div>
  </article>
</template>
