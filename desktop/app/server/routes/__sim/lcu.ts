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
 * The other way, the shell's champion select requests (#1909) — the reads
 * before a write, a hover, a lock — reach the page, which answers them as the
 * client would:
 *
 * - `POST { op: 'request', method, path, body }` (the shell) waits for the
 *   page's answer and returns it as `{ status, body }` — a 504 when no page
 *   answers in time.
 * - `GET ?op=requests` (the page) takes the requests waiting.
 * - `POST { op: 'answer', id, status, body }` (the page) answers one.
 *
 * In memory, per dev server: nothing is written anywhere.
 */
interface Relay {
  generation: number
  readings: unknown[]
}

interface Request {
  id: number
  method: string
  path: string
  body: unknown
}

interface Answer {
  status: number
  body: unknown
}

type Body =
  | { op: 'reset' }
  | { op: 'push', readings: unknown[] }
  | { op: 'request', method: string, path: string, body: unknown }
  | { op: 'answer', id: number, status: number, body: unknown }

/** Long enough for a page polling a few times a second, short enough for the shell's own deadline. */
const ANSWER_TIMEOUT_MS = 4000

const relay: Relay = { generation: 1, readings: [] }
const waiting: Request[] = []
const answers = new Map<number, (answer: Answer) => void>()
let nextRequest = 1

function ask(method: string, path: string, body: unknown): Promise<Answer> {
  const id = nextRequest++
  return new Promise((resolve) => {
    const timeout = setTimeout(() => {
      answers.delete(id)
      const index = waiting.findIndex(request => request.id === id)
      if (index >= 0) waiting.splice(index, 1)
      resolve({ status: 504, body: { message: 'The draft simulator page did not answer' } })
    }, ANSWER_TIMEOUT_MS)
    answers.set(id, (answer) => {
      clearTimeout(timeout)
      answers.delete(id)
      resolve(answer)
    })
    waiting.push({ id, method, path, body })
  })
}

export default defineEventHandler(async (event) => {
  // A static build has no server; this is here only so it can never answer outside `npm run dev`.
  if (!import.meta.dev) throw createError({ statusCode: 404 })

  if (event.method === 'POST') {
    const body = await readBody<Body>(event)
    if (body.op === 'request') return ask(body.method, body.path, body.body)
    if (body.op === 'answer') {
      answers.get(body.id)?.({ status: body.status, body: body.body })
      return { ok: true }
    }
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
  if (query.op === 'requests') return waiting.splice(0)

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
