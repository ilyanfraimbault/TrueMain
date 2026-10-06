// The checkbox column of the row-selectable tables (#722): the Logs list and the
// Crashes list, whose selections can be copied as JSON. Split out in #1436.
import { h } from 'vue'
import type { TableColumn } from '@nuxt/ui'
import { UCheckbox } from '#components'

export function selectColumn<T>(): TableColumn<T> {
  return {
    id: 'select',
    header: ({ table }) =>
      h(UCheckbox, {
        'modelValue': table.getIsSomePageRowsSelected()
          ? 'indeterminate'
          : table.getIsAllPageRowsSelected(),
        'onUpdate:modelValue': (value: unknown) =>
          table.toggleAllPageRowsSelected(!!value),
        'aria-label': 'Select all rows',
      }),
    cell: ({ row }) =>
      h(UCheckbox, {
        'modelValue': row.getIsSelected(),
        'onUpdate:modelValue': (value: unknown) =>
          row.toggleSelected(!!value),
        // The row itself opens the detail slide-over on click; the checkbox
        // must not bubble into that.
        'onClick': (event: Event) => event.stopPropagation(),
        'aria-label': 'Select row',
      }),
  }
}
