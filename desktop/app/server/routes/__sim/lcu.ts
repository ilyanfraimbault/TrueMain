/**
 * The relay between the draft simulator page and the app's shell, in `npm run
 * dev` only (#1671). The page (`/dev/draft-sim`) posts what the League client
 * would send — gameflow phase, champion select session, summoner, mastery — as
 * tape readings (`lcu::tape::Reading`); the shell, started with
 * `TRUEMAIN_LCU_SIM` pointing here (`npm run tauri:sim`), polls them and
 * applies each one the way a live session's events are applied.
 *
 * - `POST { op: 'reset' }` starts a new session (a new generation), dropping
 *   the readings of the last one.
 * - `POST { op: 'push', readings: [...] }` appends readings.
 * - `GET ?generation=G&after=N` answers the readings past `N`, or — when `G` is
 *   not the current generation — every reading of the current one with
 *   `reset: true`, so a shell started mid-draft catches up from the start.
 *
 * In memory, per dev server: nothing is written anywhere.
 */
interface Relay {
  generation: number
  readings: unknown[]
}

const relay: Relay = { generation: 1, readings: [] }

export default defineEventHandler(async (event) => {
  // A static build has no server; this is here only so it can never answer outside `npm run dev`.
  if (!import.meta.dev) throw createError({ statusCode: 404 })

  if (event.method === 'POST') {
    const body = await readBody<{ op: 'reset' } | { op: 'push', readings: unknown[] }>(event)
    if (body.op === 'reset') {
      relay.generation++
      relay.readings = []
    }
    else if (body.op === 'push' && Array.isArray(body.readings)) {
      relay.readings.push(...body.readings)
    }
    return { generation: relay.generation, size: relay.readings.length }
  }

  const query = getQuery(event)
  const generation = Number(query.generation)
  const after = Number(query.after) || 0
  const current = generation === relay.generation
  return {
    generation: relay.generation,
    reset: !current,
    next: relay.readings.length,
    readings: relay.readings.slice(current ? after : 0),
  }
})
