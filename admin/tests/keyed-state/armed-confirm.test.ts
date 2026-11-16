import { flushPromises } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { nextTick, ref } from 'vue'
import { useArmedConfirm } from '~/composables/useArmedConfirm'
import { armedKey, pressArmed } from '~/utils/armed-confirm'

// The two-click confirmation shared by the known-players actions (#2471).

describe('pressArmed', () => {
  it('arms on the first click and sends on the second', () => {
    const first = pressArmed({ s: 'idle' }, 'remove')
    expect(first).toEqual({ state: { s: 'armed', key: 'remove' }, send: false })
    expect(pressArmed(first.state, 'remove')).toEqual({ state: { s: 'sending', key: 'remove' }, send: true })
  })

  it('re-arms on another key instead of sending it', () => {
    expect(pressArmed({ s: 'armed', key: 'remove' }, 'takedown')).toEqual({ state: { s: 'armed', key: 'takedown' }, send: false })
  })

  it('sends at once an action that needs no confirmation', () => {
    expect(pressArmed({ s: 'armed', key: 'merge' }, 'keep', false)).toEqual({ state: { s: 'sending', key: 'keep' }, send: true })
  })

  it('ignores every click while a send is in flight', () => {
    const sending = { s: 'sending' as const, key: 'remove' }
    expect(pressArmed(sending, 'remove')).toEqual({ state: sending, send: false })
    expect(pressArmed(sending, 'other', false)).toEqual({ state: sending, send: false })
  })

  it('names the armed key only while armed', () => {
    expect(armedKey({ s: 'armed', key: 'x' })).toBe('x')
    expect(armedKey({ s: 'sending', key: 'x' })).toBeNull()
    expect(armedKey({ s: 'idle' })).toBeNull()
  })
})

describe('useArmedConfirm', () => {
  it('settles a container-sent action when its busy flag falls', async () => {
    const busy = ref(false)
    const confirm = useArmedConfirm({ busy: () => busy.value })
    expect(confirm.press('a')).toBe(false)
    expect(confirm.armed.value).toBe('a')
    expect(confirm.press('a')).toBe(true)
    expect(confirm.sending.value).toBe('a')
    busy.value = true
    await nextTick()
    expect(confirm.press('b')).toBe(false)
    busy.value = false
    await nextTick()
    expect(confirm.state.value).toEqual({ s: 'idle' })
  })

  it('does not stay locked when the container never took the send up', () => {
    const confirm = useArmedConfirm({ busy: () => false })
    confirm.press('a')
    confirm.press('a')
    expect(confirm.press('b')).toBe(false)
    expect(confirm.armed.value).toBe('b')
  })

  it('disarms when its scope moves, and leaves a send in flight alone', async () => {
    const scope = ref('p1')
    const confirm = useArmedConfirm({ scope })
    confirm.press('remove')
    scope.value = 'p2'
    await nextTick()
    expect(confirm.state.value).toEqual({ s: 'idle' })

    let finish!: () => void
    void confirm.run('remove', () => new Promise<void>((resolve) => {
      finish = resolve
    }), { confirm: false })
    scope.value = 'p3'
    await nextTick()
    expect(confirm.sending.value).toBe('remove')
    finish()
    await flushPromises()
    expect(confirm.state.value).toEqual({ s: 'idle' })
  })

  it('runs its own call on the second click and settles even when it throws', async () => {
    const confirm = useArmedConfirm()
    let calls = 0
    const call = async () => {
      calls++
      throw new Error('boom')
    }
    await confirm.run('bulk', call)
    expect(calls).toBe(0)
    await expect(confirm.run('bulk', call)).rejects.toThrow('boom')
    expect(calls).toBe(1)
    expect(confirm.state.value).toEqual({ s: 'idle' })
  })

  it('drops an armed action on disarm', () => {
    const confirm = useArmedConfirm()
    confirm.press('a')
    confirm.disarm()
    expect(confirm.armed.value).toBeNull()
  })
})
