import type { H3Event } from 'h3'
import { EventEmitter } from 'node:events'
import { describe, expect, it, vi } from 'vitest'
import { abortOnAbandonment, isReportableAbandonment, watchForAbandonment } from '~~/server/utils/abandoned-request'

// A client that leaves before the server answered (#1569). The Node response is an
// EventEmitter carrying `writableFinished`, which is all the watcher reads.
function eventWithResponse() {
  const res = Object.assign(new EventEmitter(), { writableFinished: false })
  return { event: { node: { res } } as unknown as H3Event, res }
}

describe('watchForAbandonment', () => {
  it('reports a response closed before it finished, once', () => {
    const { event, res } = eventWithResponse()
    const onAbandoned = vi.fn()

    watchForAbandonment(event, onAbandoned)
    res.emit('close')
    res.emit('close')

    expect(onAbandoned).toHaveBeenCalledTimes(1)
  })

  it('stays silent for a response that was delivered', () => {
    const { event, res } = eventWithResponse()
    const onAbandoned = vi.fn()

    watchForAbandonment(event, onAbandoned)
    res.writableFinished = true
    res.emit('close')

    expect(onAbandoned).not.toHaveBeenCalled()
  })

  it('ignores an event without a Node response', () => {
    expect(() => watchForAbandonment({} as H3Event, vi.fn())).not.toThrow()
  })
})

describe('abortOnAbandonment', () => {
  it('aborts the upstream signal when the client leaves', () => {
    const { event, res } = eventWithResponse()

    const signal = abortOnAbandonment(event)
    expect(signal.aborted).toBe(false)
    res.emit('close')

    expect(signal.aborted).toBe(true)
  })
})

describe('isReportableAbandonment', () => {
  it.each([
    ['/champions/ahri', true],
    ['/api/champions/103/roam', true],
    ['/_nuxt/entry.abc123.js', false],
    ['/_ipx/f_webp&s_64x64/https://ddragon.example/icon.png', false],
    [undefined, false],
  ])('%s → %s', (path, expected) => {
    expect(isReportableAbandonment(path)).toBe(expected)
  })
})
