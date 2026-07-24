<script setup lang="ts">
// Recursive renderer for a process run's free-form `summary` payload. Each
// process records whatever shape suits it (flat scalar maps, nested objects,
// or arrays of per-platform rows), so a single fixed field list can't display
// them all — without this, anything non-flat would only read as raw JSON.
//
// This component classifies the value at each level and renders accordingly:
//   - scalar            -> formatted text
//   - object            -> label/value field list; non-scalar values recurse
//                          into an indented sub-group
//   - array of objects  -> a compact table (columns = union of row keys)
//   - array of scalars  -> a row of chips
//   - mixed array       -> an indexed list, each item recursing
// It references itself for nested shapes (Nuxt auto-import resolves the SFC's
// own name), so arbitrarily deep payloads stay readable.
//
// The value-classification helpers live in `shared/utils/process-summary` so
// they can be unit-tested without mounting the component.
import { computed } from 'vue'
import { formatScalar, humanizeKey, isPlainObject, isScalar } from '~~/shared/utils/process-summary'

// `depth` is internal recursion bookkeeping (callers pass only `value`). Past
// MAX_DEPTH we stop recursing and fall back to a raw JSON block, so a
// pathologically deep payload can't blow the render stack.
const props = withDefaults(defineProps<{ value: unknown, depth?: number }>(), {
  depth: 0,
})
const MAX_DEPTH = 8

const kind = computed<'scalar' | 'object' | 'array'>(() => {
  if (isScalar(props.value)) {
    return 'scalar'
  }
  return Array.isArray(props.value) ? 'array' : 'object'
})

// --- Object shape ------------------------------------------------------------
interface ObjectEntry {
  key: string
  label: string
  value: unknown
  leaf: boolean
}
const objectEntries = computed<ObjectEntry[]>(() => {
  if (!isPlainObject(props.value)) {
    return []
  }
  return Object.entries(props.value).map(([key, value]) => ({
    key,
    label: humanizeKey(key),
    value,
    leaf: isScalar(value),
  }))
})

// --- Array shape -------------------------------------------------------------
const arrayItems = computed<unknown[]>(() =>
  Array.isArray(props.value) ? props.value : [],
)

// Tabular = every element is a plain object, so the union of their keys makes a
// sensible column set (the per-platform breakdown case).
const isTabular = computed(() =>
  arrayItems.value.length > 0 && arrayItems.value.every(isPlainObject),
)
const allScalars = computed(() =>
  arrayItems.value.length > 0 && arrayItems.value.every(isScalar),
)

interface Column {
  key: string
  label: string
}
const columns = computed<Column[]>(() => {
  // First-seen order, deduped via a Set for O(1) membership.
  const keys: string[] = []
  const seen = new Set<string>()
  for (const row of arrayItems.value) {
    if (!isPlainObject(row)) {
      continue
    }
    for (const key of Object.keys(row)) {
      if (!seen.has(key)) {
        seen.add(key)
        keys.push(key)
      }
    }
  }
  return keys.map(key => ({ key, label: humanizeKey(key) }))
})
const tableRows = computed<Record<string, unknown>[]>(() =>
  isTabular.value ? (arrayItems.value as Record<string, unknown>[]) : [],
)

function cellValue(row: Record<string, unknown>, key: string): unknown {
  return Object.prototype.hasOwnProperty.call(row, key) ? row[key] : undefined
}

// Past the cap we stop recursing and dump the remaining subtree as raw JSON.
const tooDeep = computed(() => props.depth >= MAX_DEPTH)
const rawJson = computed(() => {
  try {
    return JSON.stringify(props.value, null, 2)
  }
  catch {
    return String(props.value)
  }
})
</script>

<template>
  <!-- Depth cap: pathologically deep payloads fall back to raw JSON instead of
       recursing further. -->
  <pre
    v-if="tooDeep"
    class="text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto"
  >{{ rawJson }}</pre>

  <!-- Scalar -->
  <span
    v-else-if="kind === 'scalar'"
    class="text-sm text-highlighted tabular-nums break-all"
  >{{ formatScalar(props.value) }}</span>

  <!-- Object: label/value list; nested values recurse into a sub-group -->
  <p
    v-else-if="kind === 'object' && objectEntries.length === 0"
    class="text-sm text-dimmed"
  >
    (empty)
  </p>
  <dl
    v-else-if="kind === 'object'"
    class="text-sm border border-default rounded-md divide-y divide-default"
  >
    <div
      v-for="entry in objectEntries"
      :key="entry.key"
      :class="entry.leaf
        ? 'flex justify-between gap-3 px-3 py-1.5'
        : 'px-3 py-2 space-y-1.5'"
    >
      <template v-if="entry.leaf">
        <dt class="text-muted">
          {{ entry.label }}
        </dt>
        <dd class="text-highlighted text-right tabular-nums break-all">
          {{ formatScalar(entry.value) }}
        </dd>
      </template>
      <template v-else>
        <dt class="text-muted text-xs uppercase tracking-wide">
          {{ entry.label }}
        </dt>
        <dd>
          <ProcessSummaryView :value="entry.value" :depth="props.depth + 1" />
        </dd>
      </template>
    </div>
  </dl>

  <!-- Array -->
  <p
    v-else-if="arrayItems.length === 0"
    class="text-sm text-dimmed"
  >
    (empty)
  </p>
  <!-- Array of objects -> table -->
  <div
    v-else-if="isTabular"
    class="overflow-x-auto border border-default rounded-md"
  >
    <table class="w-full text-sm">
      <thead>
        <tr class="border-b border-default">
          <th
            v-for="col in columns"
            :key="col.key"
            class="text-left font-medium text-muted px-3 py-1.5 whitespace-nowrap"
          >
            {{ col.label }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(row, index) in tableRows"
          :key="index"
          class="border-b border-default last:border-0"
        >
          <td
            v-for="col in columns"
            :key="col.key"
            class="px-3 py-1.5 align-top text-highlighted tabular-nums"
          >
            <!-- Bind the cell once via a single-item v-for so scalar cells
                 don't call cellValue() twice. -->
            <template v-for="cell in [cellValue(row, col.key)]" :key="col.key">
              <span v-if="isScalar(cell)" class="break-all">{{ formatScalar(cell) }}</span>
              <ProcessSummaryView v-else :value="cell" :depth="props.depth + 1" />
            </template>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
  <!-- Array of scalars -> chips -->
  <div
    v-else-if="allScalars"
    class="flex flex-wrap gap-1.5"
  >
    <span
      v-for="(item, index) in arrayItems"
      :key="index"
      class="text-xs text-highlighted tabular-nums bg-elevated/50 border border-default rounded px-1.5 py-0.5 break-all"
    >
      {{ formatScalar(item) }}
    </span>
  </div>
  <!-- Mixed array -> indexed list, each item recursing -->
  <div v-else class="space-y-2">
    <div
      v-for="(item, index) in arrayItems"
      :key="index"
      class="space-y-1.5"
    >
      <p class="text-muted text-xs">
        #{{ index + 1 }}
      </p>
      <ProcessSummaryView :value="item" :depth="props.depth + 1" />
    </div>
  </div>
</template>
