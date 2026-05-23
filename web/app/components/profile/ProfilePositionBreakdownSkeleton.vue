<template>
  <section class="flex flex-col gap-2" aria-hidden="true">
    <USkeleton class="h-3 w-32" />
    <ul class="flex flex-col divide-y divide-default/40 overflow-hidden rounded-lg bg-elevated/40">
      <li
        v-for="i in 3"
        :key="i"
        class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-3 px-3 py-2"
      >
        <USkeleton class="size-5 rounded" />
        <USkeleton class="h-4 w-14" />
        <USkeleton class="h-3 w-16" />
        <USkeleton class="h-4 w-10" />
      </li>
    </ul>
  </section>
</template>
