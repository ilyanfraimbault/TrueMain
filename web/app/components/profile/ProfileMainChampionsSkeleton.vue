<template>
  <section class="flex flex-col gap-2" aria-hidden="true">
    <USkeleton class="h-3 w-32" />
    <ul class="flex flex-col divide-y divide-default/40 overflow-hidden rounded-lg bg-elevated/40">
      <li
        v-for="i in 4"
        :key="i"
        class="flex items-center gap-3 px-3 py-2"
      >
        <USkeleton class="size-9 rounded" />
        <div class="flex min-w-0 flex-1 flex-col gap-1">
          <USkeleton class="h-4 w-24" />
          <USkeleton class="h-3 w-16" />
        </div>
        <USkeleton class="h-4 w-8" />
      </li>
    </ul>
  </section>
</template>
