<script setup lang="ts">
// Placeholder of `MatchRow`: same 54 px line, same columns at the same
// @container tiers, so a list does not jump a size when the games resolve.
</script>

<template>
  <article class="@container surface overflow-hidden rounded-lg" aria-hidden="true">
    <div class="flex h-[54px] items-center gap-1.5 pl-3 pr-2 @sm:gap-2.5 @sm:pl-4 @sm:pr-3">
      <USkeleton class="size-9 shrink-0 rounded-md" />
      <div class="hidden w-20 shrink-0 space-y-1 @[39rem]:block">
        <USkeleton class="h-3.5 w-14" />
        <USkeleton class="h-2.5 w-12" />
      </div>
      <div class="w-[4.5rem] shrink-0 space-y-1">
        <USkeleton class="h-3.5 w-16" />
        <USkeleton class="h-2.5 w-12" />
      </div>
      <div class="hidden w-14 shrink-0 flex-col items-end gap-1 @[33rem]:flex">
        <USkeleton class="h-3 w-10" />
        <USkeleton class="h-2.5 w-12" />
      </div>
      <div class="flex shrink-0 items-center gap-1">
        <div class="flex flex-col gap-0.5">
          <USkeleton class="size-[18px] rounded" />
          <USkeleton class="size-[18px] rounded" />
        </div>
        <div class="flex flex-col items-center gap-0.5">
          <USkeleton class="size-[18px] rounded-full" />
          <USkeleton class="size-3.5 rounded-full" />
        </div>
        <USkeleton class="ml-0.5 h-[46px] w-[90px] rounded-lg" />
      </div>
      <div class="ml-auto flex shrink-0 items-center gap-1.5 @sm:gap-2.5">
        <div class="hidden flex-col gap-0.5 @[29rem]:flex">
          <div v-for="side in 2" :key="side" class="flex gap-0.5">
            <USkeleton v-for="i in 5" :key="i" class="size-4 rounded-sm" />
          </div>
        </div>
        <USkeleton class="size-5 rounded-full" />
      </div>
    </div>
  </article>
</template>
