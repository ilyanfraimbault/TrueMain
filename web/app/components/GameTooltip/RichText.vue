<script setup lang="ts">
import type { ParsedDocument } from '~~/shared/utils/tooltip-parser'
import { classForTag } from '~~/shared/utils/tooltip-parser'

defineProps<{
  segments: ParsedDocument
}>()
</script>

<template>
  <span>
    <template
      v-for="(seg, index) in segments"
      :key="index"
    >
      <br v-if="seg.kind === 'break'">
      <span
        v-else-if="seg.kind === 'meleeRanged'"
        class="inline-block rounded-md border border-default/60 px-1.5 align-baseline text-xs"
      >
        <span class="font-semibold">{{ seg.melee }}</span>
        <span class="text-muted"> melee · </span>
        <span class="font-semibold">{{ seg.ranged }}</span>
        <span class="text-muted"> ranged</span>
      </span>
      <span
        v-else
        :class="classForTag(seg.tag)"
      >{{ seg.text }}</span>
    </template>
  </span>
</template>
