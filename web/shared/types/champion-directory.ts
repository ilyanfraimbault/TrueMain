// What the champion directory (#1734) offers to order by: `GET /champions/directory`'s `sort` and `order`.

/** The columns the directory can be ordered by; the API falls back to `pickRate` on anything else. */
export const CHAMPION_DIRECTORY_SORTS = ['pickRate', 'winRate', 'banRate', 'games', 'tier'] as const
export type ChampionDirectorySort = typeof CHAMPION_DIRECTORY_SORTS[number]

/** `desc` is strongest first; the API reads anything but `asc` as `desc`. */
export type SortOrder = 'asc' | 'desc'

