/**
 * Summoner's Rift geometry for the jungle first-clear map (#1186): camp
 * centroids, fountains and the game-coordinate → minimap-pixel transform.
 *
 * Coordinates mirror backend/Core/Lol/Map/JungleCamps.cs and LolMap.cs — keep
 * the two in sync (the backend's JungleCampsTests pins the exact values). The
 * bundled /map/map11.png is ddragon's 512×512 minimap, which shares the
 * timeline coordinate frame this transform assumes.
 */

export const MAP_VIEW = 512

const MAP_BOUNDS = { minX: -120, maxX: 14870, minY: -120, maxY: 14980 }

export interface JungleCampSpot {
  x: number
  y: number
  label: string
}

/** Keyed by the backend's JungleCamp enum name, as sent in `steps[].camp`. */
export const JUNGLE_CAMPS: Record<string, JungleCampSpot> = {
  BlueGromp: { x: 2150, y: 8420, label: 'Gromp' },
  BlueBlueBuff: { x: 3820, y: 7920, label: 'Blue Buff' },
  BlueWolves: { x: 3650, y: 6500, label: 'Wolves' },
  BlueRaptors: { x: 6900, y: 5500, label: 'Raptors' },
  BlueRedBuff: { x: 7770, y: 3800, label: 'Red Buff' },
  BlueKrugs: { x: 8400, y: 2750, label: 'Krugs' },
  RedGromp: { x: 12600, y: 6560, label: 'Gromp' },
  RedBlueBuff: { x: 10930, y: 7060, label: 'Blue Buff' },
  RedWolves: { x: 11100, y: 8480, label: 'Wolves' },
  RedRaptors: { x: 7850, y: 9480, label: 'Raptors' },
  RedRedBuff: { x: 6980, y: 11180, label: 'Red Buff' },
  RedKrugs: { x: 6350, y: 12230, label: 'Krugs' },
}

/** Fountain spots — a recall detours to the *jungler's* fountain (by teamId). */
export const FOUNTAINS = {
  blue: { x: 554, y: 581 },
  red: { x: 14340, y: 14390 },
}

/**
 * Maps a game-world coordinate to the 512-unit SVG viewBox. Game y grows
 * bottom→top, SVG y grows top→bottom, so y is inverted.
 */
export function toMapView(x: number, y: number): { x: number, y: number } {
  const spanX = MAP_BOUNDS.maxX - MAP_BOUNDS.minX
  const spanY = MAP_BOUNDS.maxY - MAP_BOUNDS.minY
  return {
    x: ((x - MAP_BOUNDS.minX) / spanX) * MAP_VIEW,
    y: MAP_VIEW - ((y - MAP_BOUNDS.minY) / spanY) * MAP_VIEW,
  }
}
