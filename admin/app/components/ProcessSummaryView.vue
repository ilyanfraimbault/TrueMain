<script setup lang="ts">
// Recursive renderer for a process run's free-form `summary` payload. Each
// process records whatever shape suits it (flat scalar maps, nested objects,
// or arrays of per-platform rows), so a single fixed field list can't display
// them all — anything non-flat used to fall back to a raw JSON dump.
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
import { formatNumber } from '~~/shared/utils/format'

const props = defineProps<{ value: unknown }>()

function humanizeKey(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/^./, char => char.toUpperCase())
    .trim()
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

// A "scalar" is anything we render as a single line of text rather than a
// nested structure: primitives plus null/undefined.
function isScalar(value: unknown): boolean {
  return value === null || value === undefined || typeof value !== 'object'
}

function formatScalar(value: unknown): string {
  if (value === null || value === undefined) {
    return '—'
  }
  if (typeof value === 'number') {
    return formatNumber(value)
  }
  if (typeof value === 'boolean') {
    return value ? 'Yes' : 'No'
  }
  return String(value)
}

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
</script>

<template>
  <!-- Scalar -->
  <span
    v-if="kind === 'scalar'"
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
          <ProcessSummaryView :value="entry.value" />
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
            <span v-if="isScalar(cellValue(row, col.key))" class="break-all">
              {{ formatScalar(cellValue(row, col.key)) }}
            </span>
            <ProcessSummaryView v-else :value="cellValue(row, col.key)" />
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
      <ProcessSummaryView :value="item" />
    </div>
  </div>
</template>
