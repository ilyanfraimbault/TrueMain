import type { GameChange, GameState, GameUpdate } from '~/types/game'

/**
 * Apply one reading's changes. The same rule as `GameState::apply` in
 * `crates/live-client/src/game.rs`, which keeps Rust's copy of the game and
 * this one equal: every change carries the new value, never a delta.
 */
export function applyGameChanges(game: GameState, changes: GameChange[]): GameState {
  const players = game.players.map(player => ({ ...player }))
  let gold = game.gold
  let objectives = game.objectives
  let pace = game.pace
  for (const change of changes) {
    if (change.kind === 'gold') {
      gold = change.gold
      continue
    }
    if (change.kind === 'objectives') {
      objectives = change.objectives
      continue
    }
    if (change.kind === 'pace') {
      pace = change.pace
      continue
    }
    const player = players[change.player]
    if (!player) continue
    switch (change.kind) {
      case 'items': player.items = change.items; break
      case 'levelUp': player.level = change.level; break
      case 'score':
        player.kills = change.kills
        player.deaths = change.deaths
        player.assists = change.assists
        player.creepScore = change.creepScore
        break
      case 'died':
        player.dead = true
        player.respawnAt = change.respawnAt
        break
      case 'respawned':
        player.dead = false
        player.respawnAt = null
        break
    }
  }
  return { ...game, players, gold, objectives, pace }
}

/**
 * The running game, as the shell reads it through the game's own API while
 * the phase is `InProgress` (#1748).
 *
 * Rust sends a snapshot when a game is first read and then only what changed
 * — a purchase, a level, a death — each update numbered. One that does not
 * follow the revision held here means one was missed (the page mounted
 * between two), and the whole state is read again rather than drawn with a
 * gap. In a plain browser the dev scenarios set it instead (`useDevScenarios`).
 */
export function useLiveGame() {
  const game = useState<GameState | null>('live-game', () => null)
  /** When `game.gameTime` was read, on this page's clock: the clock on screen runs from it. */
  const syncedAt = useState<number>('live-game-synced-at', () => Date.now())
  const subscribed = useState<boolean>('live-game-subscribed', () => false)

  function set(next: GameState | null) {
    game.value = next
    syncedAt.value = Date.now()
  }

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(subscribe)
  }

  async function subscribe() {
    if (!insideTauri()) return

    const { invoke } = await import('@tauri-apps/api/core')
    const { listen } = await import('@tauri-apps/api/event')

    // Bumped by every event: a read that an event overtook is older than what
    // is already on screen, and is dropped.
    let epoch = 0
    async function resync() {
      const at = epoch
      const read = await invoke<GameState | null>('current_game')
      if (at === epoch) set(read)
    }

    const stopSnapshot = await listen<GameState | null>('game://snapshot', (event) => {
      epoch++
      set(event.payload)
    })
    const stopUpdate = await listen<GameUpdate>('game://update', (event) => {
      const update = event.payload
      const current = game.value
      if (current && update.revision <= current.revision) return
      if (!current || update.revision !== current.revision + 1) {
        void resync()
        return
      }
      epoch++
      set({ ...applyGameChanges(current, update.changes), revision: update.revision, gameTime: update.gameTime })
    })
    onScopeDispose(() => {
      stopSnapshot()
      stopUpdate()
    })

    // After subscribing, so nothing lands between the read and the listeners.
    await resync()
  }

  return { game, syncedAt, set }
}
