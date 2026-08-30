import type { Column } from '@tanstack/vue-table'
import { UButton } from '#components'

/**
 * Build a sortable column `header` renderer for `UTable`. Renders a ghost
 * Button whose trailing icon reflects the current sort direction and toggles
 * `asc -> desc -> none` on click, using TanStack Table's sorting API.
 *
 * Usage in a column def:
 *   { accessorKey: 'games', header: ({ column }) => sortableHeader(column, 'Games') }
 *
 * `align` right-justifies numeric headers so they line up with right-aligned
 * cells.
 */
export function sortableHeader<T>(
  column: Column<T>,
  label: string,
  align: 'left' | 'right' = 'left',
) {
  const sorted = column.getIsSorted()
  return h(UButton, {
    color: 'neutral',
    variant: 'ghost',
    label,
    icon: sorted === 'asc'
      ? 'i-lucide-arrow-up-narrow-wide'
      : sorted === 'desc'
        ? 'i-lucide-arrow-down-wide-narrow'
        : 'i-lucide-arrow-up-down',
    trailing: true,
    class: align === 'right'
      ? '-mx-2.5 w-full justify-end'
      : '-mx-2.5',
    'aria-label': `Sort by ${label}`,
    onClick: () => column.toggleSorting(column.getIsSorted() === 'asc'),
  })
}
