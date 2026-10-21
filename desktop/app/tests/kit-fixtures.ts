import type { Kit, KitRunePage } from '#shared/types/kit'
import type { RuneTreeResponse, StaticItemData } from '#shared/types/static-data'

/** Two trees of the shape Community Dragon's are: a keystone row, three rows of three, and the three shard rows. */
export const RUNE_TREE: RuneTreeResponse = {
  styles: [
    { styleId: 8100, name: 'Domination', iconUrl: '', keystones: [8112, 8128], subRows: [[8126, 8139, 8143], [8136, 8120, 8138], [8135, 8105, 8106]] },
    { styleId: 8200, name: 'Sorcery', iconUrl: '', keystones: [8214, 8229], subRows: [[8224, 8226, 8275], [8210, 8234, 8233], [8237, 8232, 8236]] },
    { styleId: 8000, name: 'Precision', iconUrl: '', keystones: [8005, 8008], subRows: [[9101, 9111, 8009], [9104, 9105, 9103], [8014, 8017, 8299]] },
  ],
  perks: { 8112: { id: 8112, name: 'Electrocute', iconUrl: 'electrocute.png' }, 8128: { id: 8128, name: 'Dark Harvest', iconUrl: 'harvest.png' } },
  perkStyles: { 8100: { id: 8100, name: 'Domination', iconUrl: '' }, 8200: { id: 8200, name: 'Sorcery', iconUrl: '' }, 8000: { id: 8000, name: 'Precision', iconUrl: '' } },
  shardSlots: [[5008, 5005, 5007], [5008, 5010, 5001], [5011, 5013, 5001]],
}

export const PAGE: KitRunePage = {
  primaryStyleId: 8100,
  primaryKeystoneId: 8112,
  primaryPerk1Id: 8139,
  primaryPerk2Id: 8138,
  primaryPerk3Id: 8135,
  secondaryStyleId: 8200,
  secondaryPerk1Id: 8226,
  secondaryPerk2Id: 8210,
  statOffense: 5008,
  statFlex: 5008,
  statDefense: 5011,
}

export function item(id: number, overrides: Partial<StaticItemData> = {}): StaticItemData {
  return { id, name: `Item ${id}`, iconUrl: `${id}.png`, totalGold: 3000, tags: [], maps: [11], ...overrides }
}

/** Luden's → (Lich Bane → Rabadon's) | Shadowflame, with Sorcerer's Shoes and a Doran's start. */
export function kit(overrides: Partial<Kit> = {}): Kit {
  return {
    id: 'k1',
    name: 'Into tanks',
    championId: 103,
    position: 'MIDDLE',
    runes: { ...PAGE },
    spells: [4, 14],
    starter: [1056, 2003, 2003],
    boots: [3020],
    tree: [{ itemId: 6655, children: [{ itemId: 3100, children: [{ itemId: 3089, children: [] }] }, { itemId: 4645, children: [] }] }],
    updatedAt: '2026-10-20T10:00:00.000Z',
    ...overrides,
  }
}
