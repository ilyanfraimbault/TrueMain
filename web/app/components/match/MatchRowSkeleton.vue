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
    <div class="flex flex-1 items-center gap-3 px-3 py-2.5">
      <div class="flex w-[5.5rem] shrink-0 flex-col gap-1">
        <USkeleton class="h-3 w-14" />
        <USkeleton class="h-3 w-10" />
        <USkeleton class="h-3 w-12" />
        <USkeleton class="h-3 w-10" />
        <USkeleton class="h-3 w-12" />
      </div>
      <USkeleton class="size-12 shrink-0 rounded" />
      <div class="flex shrink-0 flex-col gap-0.5">
        <USkeleton class="size-[22px] rounded" />
        <USkeleton class="size-[22px] rounded" />
      </div>
      <div class="flex shrink-0 flex-col items-center gap-0.5">
        <USkeleton class="size-[22px] rounded-full" />
        <USkeleton class="size-[18px] rounded-full" />
      </div>
      <div class="flex min-w-[6rem] flex-col items-center gap-1">
        <USkeleton class="h-5 w-20" />
        <USkeleton class="h-3 w-14" />
        <USkeleton class="h-3 w-24" />
        <USkeleton class="h-3 w-12" />
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <div class="flex gap-1">
          <USkeleton v-for="i in 6" :key="`inv-${i}`" class="size-6 rounded" />
        </div>
        <USkeleton class="size-6 rounded-full" />
      </div>
      <div class="ml-auto shrink-0">
        <USkeleton class="h-5 w-10 rounded-full" />
      </div>
    </div>
  </article>
</template>
