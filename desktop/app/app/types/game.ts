/** Mirrors `GameState` in `crates/live-client/src/game.rs`. */
export interface GameState {
  /** Counts the changes sent so far: an update applies only on top of the revision before it. */
  revision: number
  /** Seconds of game time at the latest reading; the page runs its own clock between two. */
  gameTime: number
  gameMode: string
  mapNumber: number
  /** Our side; null when spectating. */
  myTeam: GameTeam | null
  /** All ten, blue side then red side. A change names a player by its index here. */
  players: GamePlayer[]
  /** Our unspent gold, floored to 50 (`GOLD_STEP`); 0 when spectating. */
  gold: number
  /** What each team has taken on the map; moves only when one falls. */
  objectives: GameObjectives
  /** Our creep score and gold earned at each whole minute so far. */
  pace: GamePace
}

/** Mirrors `Pace` in `crates/live-client/src/pace.rs`; empty when spectating. */
export interface GamePace {
  samples: { minute: number, cs: number, gold: number }[]
}

/** Mirrors `Objectives` in `crates/live-client/src/objectives.rs`. */
export interface GameObjectives {
  /** Blue side's. */
  order: TeamObjectives
  /** Red side's. */
  chaos: TeamObjectives
}

export interface TeamObjectives {
  /** Enemy turrets this team destroyed. */
  turrets: number
  /** For each enemy inhibitor this team destroyed, the game time it stands again. */
  inhibitors: number[]
  /** Elemental drakes this team slew; the Elder is not one of them. */
  dragons: number
  /** The game time this team's latest Baron buff ends at. */
  baronUntil: number | null
  /** The game time this team's latest Elder buff ends at. */
  elderUntil: number | null
}

/** `ORDER` is blue side, `CHAOS` red. */
export type GameTeam = 'ORDER' | 'CHAOS'

export interface GamePlayer {
  riotId: string
  /** Data Dragon's alias (`MonkeyKing`). */
  champion: string
  /** In the game's language. */
  championName: string
  team: GameTeam
  /** Upper-case lane; empty in queues without lanes. */
  position: string
  isMe: boolean
  isBot: boolean
  spells: GameSpell[]
  level: number
  /** By slot: 0-5 the inventory, 6 the trinket. */
  items: GameItem[]
  kills: number
  deaths: number
  assists: number
  dead: boolean
  /** Game time the player comes back at, while dead. */
  respawnAt: number | null
}

export interface GameItem {
  itemId: number
  slot: number
  count: number
}

export interface GameSpell {
  /** Data Dragon's id (`SummonerFlash`); empty when unrecognised. */
  key: string
  name: string
}

/** Mirrors `GameChange`: absolute values, so a change applied twice is harmless. */
export type GameChange =
  | { kind: 'items', player: number, items: GameItem[] }
  | { kind: 'levelUp', player: number, level: number }
  | { kind: 'score', player: number, kills: number, deaths: number, assists: number }
  | { kind: 'died', player: number, respawnAt: number }
  | { kind: 'objectives', objectives: GameObjectives }
  | { kind: 'pace', pace: GamePace }
  | { kind: 'respawned', player: number }
  | { kind: 'gold', gold: number }

/** Mirrors `GameUpdate` in `crates/live-client/src/feed.rs`. */
export interface GameUpdate {
  /** The revision once these are applied. */
  revision: number
  gameTime: number
  changes: GameChange[]
}
