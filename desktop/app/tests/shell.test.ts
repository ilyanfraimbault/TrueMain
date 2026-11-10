import { describe, expect, it } from 'vitest'
import { follow } from '~/utils/shell'

/** A shell event the test fires by hand. */
function event<T>() {
  let handler: ((event: { payload: T }) => void) | null = null
  return {
    listen: async (next: (event: { payload: T }) => void) => {
      handler = next
      return () => (handler = null)
    },
    fire: (payload: T) => handler?.({ payload }),
  }
}

describe('follow', () => {
  it('listens before it reads, so a change pushed meanwhile is kept over the older read', async () => {
    const pushed = event<number>()
    const applied: number[] = []
    let answer: (value: number) => void = () => {}
    const following = follow(pushed, () => new Promise<number>(resolve => (answer = resolve)), value => applied.push(value))
    await Promise.resolve()
    pushed.fire(2)
    answer(1)
    await following
    expect(applied).toEqual([2])
  })

  it('applies the read when nothing was pushed, then every push, until stopped', async () => {
    const pushed = event<number>()
    const applied: number[] = []
    const stop = await follow(pushed, async () => 1, value => applied.push(value))
    pushed.fire(2)
    stop()
    pushed.fire(3)
    expect(applied).toEqual([1, 2])
  })
})
