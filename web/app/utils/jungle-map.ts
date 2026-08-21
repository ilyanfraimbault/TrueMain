/**
 * Display names for Summoner's Rift jungle camps, keyed by the backend's
 * `JungleCamp` enum name as sent in `jungleClear.startCamp`.
 *
 * There is deliberately no map geometry here any more (#1195). Riot samples a
 * jungler's position once per minute while a first clear runs from the 1:30
 * spawn to ~3:15, so three to four camps fall between two consecutive samples.
 * Plotting those samples drew five points that read as a route while the camps
 * were cleared in the gaps — the map could not show a clear it had no data for.
 * Only the opening camp is nameable, because the jungler waits on it while
 * their jungle CS is still 0.
 */
export const JUNGLE_CAMP_LABELS: Record<string, string> = {
  BlueGromp: 'Gromp',
  BlueBlueBuff: 'Blue Buff',
  BlueWolves: 'Wolves',
  BlueRaptors: 'Raptors',
  BlueRedBuff: 'Red Buff',
  BlueKrugs: 'Krugs',
  RedGromp: 'Gromp',
  RedBlueBuff: 'Blue Buff',
  RedWolves: 'Wolves',
  RedRaptors: 'Raptors',
  RedRedBuff: 'Red Buff',
  RedKrugs: 'Krugs',
}
