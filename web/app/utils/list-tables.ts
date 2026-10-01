/**
 * The column grids of the two list tables (#1726) — the truemains leaderboard
 * and the champion directory — shared by each table's header, its rows and its
 * skeleton rows, so every cell sits under its label and lines up down the list.
 *
 * Each table is an `@container` and drops its secondary columns as it narrows,
 * header and rows together: a cell hidden at a width (`hidden @xl:block`) must
 * leave the template at that width too, or every column right of it slides one
 * slot over. The class names live in each cell; the templates below are the
 * other half of that contract, one per tier. Written out whole for Tailwind's
 * static scan — an interpolated `grid-cols-[…]` would never be generated.
 */

/**
 * #, Player, Lanes, Main, Score, Rank, Games, KDA, WR, follow.
 * Phone: #, Player, Main, Score, Rank, follow — the main as its portrait alone.
 * `@xl` adds games/KDA/WR, `@2xl` the lanes, `@4xl` swaps the portrait for the
 * signature-champion cluster (play rate, keystone, first item), `@5xl` adds the
 * two sub-mains beside it.
 */
export const TRUEMAINS_TABLE_GRID = [
  'grid-cols-[1.25rem_minmax(0,1fr)_2rem_2.25rem_2rem_1.75rem]',
  '@xl:grid-cols-[2rem_minmax(0,1fr)_2rem_3.25rem_3.25rem_3rem_3rem_3rem_1.75rem]',
  '@2xl:grid-cols-[2.5rem_minmax(0,1fr)_3.5rem_2rem_3.5rem_3.5rem_3.5rem_3rem_3rem_1.75rem]',
  '@4xl:grid-cols-[2.75rem_minmax(0,1fr)_3.5rem_9.5rem_3.5rem_3.5rem_3.5rem_3rem_3rem_1.75rem]',
  '@5xl:grid-cols-[2.75rem_minmax(0,1fr)_3.5rem_13rem_3.5rem_3.5rem_3.5rem_3rem_3rem_1.75rem]',
].join(' ')

/**
 * #, Champion, Lane, Tier, Runes, Build, WR, PR, BR, Games.
 * Phone: #, Champion, Lane, Tier, WR, PR. `@xl` adds ban rate and games, `@2xl`
 * the runes, `@4xl` the core build path.
 */
export const CHAMPIONS_TABLE_GRID = [
  'grid-cols-[1.5rem_minmax(0,1fr)_1.75rem_2rem_2.75rem_2.75rem]',
  '@xl:grid-cols-[2rem_minmax(0,1fr)_2rem_2.5rem_3.25rem_3.25rem_3.25rem_3.75rem]',
  '@2xl:grid-cols-[2.5rem_minmax(0,1fr)_2.5rem_2.5rem_2.5rem_3.5rem_3.5rem_3.5rem_4rem]',
  '@4xl:grid-cols-[2.75rem_minmax(0,1fr)_2.5rem_2.5rem_2.5rem_14rem_3.5rem_3.5rem_3.5rem_4.5rem]',
].join(' ')
