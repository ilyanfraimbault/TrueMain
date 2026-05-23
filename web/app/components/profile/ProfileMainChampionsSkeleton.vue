<template>
  <section class="flex flex-col gap-3" aria-hidden="true">
    <USkeleton class="h-3 w-32" />
    <div class="flex gap-3 overflow-x-auto pb-1">
      <div
        v-for="i in 6"
        :key="i"
        class="flex w-32 shrink-0 flex-col items-center gap-1.5 rounded-lg bg-elevated/40 p-3"
      >
        <USkeleton class="size-16 rounded" />
        <USkeleton class="h-4 w-20" />
        <USkeleton class="h-3 w-16" />
        <USkeleton class="h-3 w-12" />
      </div>
    </div>
  </section>
</template>
