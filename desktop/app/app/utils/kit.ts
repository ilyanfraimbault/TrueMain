import type { Kit, KitNode, KitRunePage, KitStore } from '#shared/types/kit'
import type { RuneTreeResponse } from '#shared/types/static-data'
import type { BuildCoreView } from '~/types/build'
import { DEFAULT_SPELLS } from '~/utils/kit-items'
import { runeIssues } from '~/utils/kit-runes'
import { chainOf, cutAt, kitPaths, maxDepth, MAX_PATH_ITEMS } from '~/utils/kit-tree'

/** The longest name a kit takes; the shell keeps the same cap (`shell_state::kits`). */
export const KIT_NAME_MAX = 40

export const EMPTY_STORE: KitStore = { kits: [], active: {} }

/** The key a kit is active under: one kit per champion and position. */
export const activeKey = (championId: number, position: string) => `${championId}:${position}`

/** A kit's id: a random UUID from the platform's crypto (the webview and Node both have it). */
export function newKitId(): string {
  if (typeof globalThis.crypto.randomUUID === 'function') return globalThis.crypto.randomUUID()
  const bytes = globalThis.crypto.getRandomValues(new Uint8Array(16))
  return `kit-${Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('')}`
}

/**
 * A copy of a kit sharing nothing with it — what the editor works on. Field by field rather than
 * `structuredClone`: a kit read out of the reactive store carries proxies, which `structuredClone` refuses.
 */
export function cloneKit(kit: Kit): Kit {
  const cloneNodes = (nodes: KitNode[]): KitNode[] => nodes.map(node => ({ itemId: node.itemId, children: cloneNodes(node.children) }))
  return { ...kit, positions: [...kit.positions], runes: { ...kit.runes }, spells: [kit.spells[0], kit.spells[1]], starter: [...kit.starter], boots: [...kit.boots], tree: cloneNodes(kit.tree) }
}

/** The rune page of a build, without its measured figures. */
export function kitRunesOf(page: KitRunePage): KitRunePage {
  return {
    primaryStyleId: page.primaryStyleId,
    primaryKeystoneId: page.primaryKeystoneId,
    primaryPerk1Id: page.primaryPerk1Id,
    primaryPerk2Id: page.primaryPerk2Id,
    primaryPerk3Id: page.primaryPerk3Id,
    secondaryStyleId: page.secondaryStyleId,
    secondaryPerk1Id: page.secondaryPerk1Id,
    secondaryPerk2Id: page.secondaryPerk2Id,
    statOffense: page.statOffense,
    statFlex: page.statFlex,
    statDefense: page.statDefense,
  }
}

/**
 * A new kit out of TrueMain's build on screen: its starter, boots, core path,
 * rune page and spells, so the player starts from a full kit and swaps. What
 * the build lacks comes from `fallbackRunes` and Flash + Ignite.
 */
export function kitFromBuild(
  base: { championId: number, positions: string[], name: string },
  core: BuildCoreView | null,
  fallbackRunes: KitRunePage | null,
): Kit | null {
  const runes = core?.runePage ? kitRunesOf(core.runePage) : fallbackRunes
  if (!runes) return null
  const boots = core?.boots?.itemIds.slice(0, 1) ?? []
  const spells = core?.summonerSpells
  return {
    id: newKitId(),
    name: base.name.slice(0, KIT_NAME_MAX),
    championId: base.championId,
    positions: [...base.positions],
    runes,
    spells: spells ? [spells.spell1Id, spells.spell2Id] : [...DEFAULT_SPELLS],
    starter: [...(core?.starterItems?.itemIds ?? [])],
    boots,
    tree: chainOf((core?.itemPath?.itemIds ?? []).filter(id => !boots.includes(id)).slice(0, maxDepth(boots))),
    updatedAt: new Date().toISOString(),
  }
}

/** No measured figure: a kit is the player's, not a sample. */
const UNMEASURED = { games: 0, pickRate: 0, winRate: 0 }

/** A kit in the shape the build view's core blocks draw; the path is the tree's to draw. */
export function kitCoreView(kit: Kit): BuildCoreView {
  return {
    itemPath: null,
    boots: kit.boots.length ? { itemIds: kit.boots, ...UNMEASURED } : null,
    starterItems: kit.starter.length ? { itemIds: kit.starter, ...UNMEASURED } : null,
    summonerSpells: { spell1Id: kit.spells[0], spell2Id: kit.spells[1], ...UNMEASURED },
    skillOrder: null,
    runePage: { ...kit.runes, ...UNMEASURED },
  }
}

/** The most boots a kit picks between; the shell keeps the same cap (`shell_state::kits`). */
export const KIT_BOOTS_MAX = 4

/** The kit with other boots, every path cut to keep within a full build. */
export function withBoots(kit: Kit, boots: number[]): Kit {
  return { ...kit, boots, tree: cutAt(kit.tree, maxDepth(boots)) }
}

/** The first name `Name`, `Name 2`, `Name 3`… no other kit of the champion's uses. */
export function freeName(name: string, taken: string[]): string {
  const base = name.slice(0, KIT_NAME_MAX - 3)
  if (!taken.includes(base)) return base
  for (let n = 2; ; n++) {
    const candidate = `${base} ${n}`
    if (!taken.includes(candidate)) return candidate
  }
}

/** Whether a path holds an item twice. */
function repeats(path: number[]): boolean {
  return new Set(path).size !== path.length
}

/**
 * Why a kit cannot be saved, in the player's words; empty when it can. The
 * rune checks need the patch's trees and are left out until they are read.
 */
export function kitIssues(kit: Kit, runeTree: RuneTreeResponse | null): string[] {
  const issues: string[] = []
  if (!kit.name.trim()) issues.push('Name the kit.')
  if (kit.positions.length === 0) issues.push('Choose at least one position.')
  if (kit.tree.length === 0) issues.push('Add a first item.')
  const paths = kitPaths(kit.tree)
  const slots = kit.boots.length > 0 ? 1 : 0
  if (paths.some(path => path.length + slots > MAX_PATH_ITEMS)) issues.push(`A path holds at most ${MAX_PATH_ITEMS} items, boots included.`)
  if (paths.some(repeats)) issues.push('An item appears twice on one path.')
  if (paths.some(path => path.some(id => kit.boots.includes(id)))) issues.push('The boots go in their own slot, not on a path.')
  const [first, second] = kit.spells
  if (!first || !second || first === second) issues.push('Choose two different summoner spells.')
  if (runeTree) issues.push(...runeIssues(kit.runes, runeTree))
  return issues
}

const isNumber = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value > 0

function readNodes(value: unknown, depth: number): KitNode[] {
  if (!Array.isArray(value) || depth <= 0) return []
  return value
    .filter((node): node is { itemId: number, children?: unknown } => isNumber((node as { itemId?: unknown } | null)?.itemId))
    .map(node => ({ itemId: node.itemId, children: readNodes(node.children, depth - 1) }))
}

/** One kit read leniently, or `null` when it is not one. */
function readKit(value: unknown): Kit | null {
  const kit = value as (Partial<Kit> & { position?: unknown }) | null
  if (!kit || typeof kit.id !== 'string' || typeof kit.name !== 'string' || !isNumber(kit.championId)) return null
  // One position before a kit could serve several.
  const named: unknown[] = [...(typeof kit.position === 'string' ? [kit.position] : []), ...(Array.isArray(kit.positions) ? kit.positions : [])]
  const positions = [...new Set(named.filter((p): p is string => typeof p === 'string' && p.length > 0))].slice(0, 5)
  if (positions.length === 0) return null
  const runes = kit.runes as KitRunePage | undefined
  if (!runes || !Object.values(kitRunesOf(runes)).every(isNumber)) return null
  const spells = Array.isArray(kit.spells) && kit.spells.length === 2 && kit.spells.every(isNumber) ? kit.spells as [number, number] : [...DEFAULT_SPELLS] as [number, number]
  // One id or none before a kit could hold several boots.
  const listed: unknown[] = Array.isArray(kit.boots) ? kit.boots : [kit.boots]
  const boots = [...new Set(listed.filter(isNumber))].slice(0, KIT_BOOTS_MAX)
  return {
    id: kit.id,
    name: kit.name.slice(0, KIT_NAME_MAX),
    championId: kit.championId,
    positions,
    runes: kitRunesOf(runes),
    spells,
    starter: Array.isArray(kit.starter) ? kit.starter.filter(isNumber) : [],
    boots,
    tree: readNodes(kit.tree, maxDepth(boots)),
    updatedAt: typeof kit.updatedAt === 'string' ? kit.updatedAt : new Date(0).toISOString(),
  }
}

/**
 * A store read leniently — the browser's copy in development, or anything the
 * shell answers: a kit that does not read is dropped, not the store, and an
 * active entry naming no kit of its champion is forgotten.
 */
export function readStore(value: unknown): KitStore {
  const raw = value as Partial<KitStore> | null
  const kits = (Array.isArray(raw?.kits) ? raw.kits : []).map(readKit).filter((kit): kit is Kit => kit !== null)
  const byId = new Map(kits.map(kit => [kit.id, kit]))
  const active: Record<string, string> = {}
  for (const [key, id] of Object.entries(raw?.active ?? {})) {
    const kit = typeof id === 'string' ? byId.get(id) : undefined
    if (kit && kit.positions.some(position => activeKey(kit.championId, position) === key)) active[key] = id
  }
  return { kits: [...byId.values()], active }
}
