import { describe, expect, it } from 'vitest'
import type { StaticItemData } from '~~/shared/types/static-data'
import { itemSlots } from '~~/shared/utils/build'

function item(id: number): StaticItemData {
  return { id, name: `Item ${id}`, iconUrl: `https://cdn/${id}.png` } as StaticItemData
}

describe('itemSlots', () => {
  it('keeps one slot per id when the map resolves every one', () => {
    const map = { 3009: item(3009), 3190: item(3190) }
    expect(itemSlots([3009, 3190], map)).toEqual([
      { id: 3009, item: map[3009] },
      { id: 3190, item: map[3190] },
    ])
  })

  it('keeps a slot for every id while the map is still empty', () => {
    // The whole point: the item map is fetched separately from the build, so
    // during that window nothing resolves. Dropping the ids here is what made a
    // build that had boots render its "No data" state.
    expect(itemSlots([3009], {})).toEqual([{ id: 3009, item: null }])
  })

  it('keeps duplicate ids as distinct slots', () => {
    // A starter set is legitimately [3869, 2003, 2003] — two potions are two
    // icons, not one.
    expect(itemSlots([3869, 2003, 2003], {}).map(slot => slot.id)).toEqual([3869, 2003, 2003])
  })

  it('preserves order, resolved or not', () => {
    const map = { 3050: item(3050) }
    expect(itemSlots([3190, 3050, 3109], map).map(slot => slot.item?.id ?? null))
      .toEqual([null, 3050, null])
  })

  it('yields nothing for a dimension the build does not carry', () => {
    // The only state that legitimately reads as "No data".
    expect(itemSlots(null, {})).toEqual([])
    expect(itemSlots(undefined, {})).toEqual([])
    expect(itemSlots([], {})).toEqual([])
  })
})
