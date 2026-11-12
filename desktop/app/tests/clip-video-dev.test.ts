import { describe, expect, it, vi } from 'vitest'
import type { Clip } from '../app/types/recordings'
import { devRecordingCall } from '../app/utils/recordings-dev'
import { devLoadingView } from '../app/utils/loading-dev'

/**
 * The browser-only stand-ins (`npm run dev`) move a clip between the places it
 * plays from as the shell's `ClipVideo::new` does, and the loading scenario
 * holds every state a read and a player can be in (#2472).
 */

async function clips(): Promise<Clip[]> {
  return (await devRecordingCall('recordingLibrary')).clips
}

describe('the dev clip video', () => {
  it('goes local → both → youtube as the player publishes and frees a clip', async () => {
    vi.spyOn(console, 'info').mockImplementation(() => {})
    const local = (await clips()).find(clip => clip.video.kind === 'local')!
    const both = await devRecordingCall('clipSetYoutubeUrl', local.id, 'https://youtu.be/jNQXAC9IVRw')
    expect(both.video).toEqual({ kind: 'both', url: 'https://www.youtube.com/watch?v=jNQXAC9IVRw' })
    expect(await devRecordingCall('clipYoutubeEmbed', local.id)).toContain('/embed/jNQXAC9IVRw')
    await devRecordingCall('clipOpenYoutube', local.id)

    const youtube = await devRecordingCall('clipDeleteVideo', local.id)
    expect(youtube.video).toEqual({ kind: 'youtube', url: 'https://www.youtube.com/watch?v=jNQXAC9IVRw' })
    await expect(devRecordingCall('clipSetYoutubeUrl', local.id, '')).rejects.toThrow('only on YouTube')
  })

  it('forgets a link back to the file alone, and refuses to free an unpublished clip', async () => {
    const local = (await clips()).find(clip => clip.video.kind === 'local')!
    await devRecordingCall('clipSetYoutubeUrl', local.id, 'https://youtu.be/jNQXAC9IVRw')
    const forgotten = await devRecordingCall('clipSetYoutubeUrl', local.id, '')
    expect(forgotten.video).toEqual({ kind: 'local' })
    await expect(devRecordingCall('clipDeleteVideo', local.id)).rejects.toThrow('only a clip published')
    await expect(devRecordingCall('clipYoutubeEmbed', local.id)).rejects.toThrow('no YouTube link')
  })

  it('lists a clip with nothing left to play as missing', async () => {
    expect((await clips()).some(clip => clip.video.kind === 'missing')).toBe(true)
  })
})

describe('the dev loading scenario', () => {
  it('holds every read state, a hidden player and a reading board', () => {
    const view = devLoadingView()
    expect(view.status).toBe('reading')
    const known = view.players.flatMap(player => (player.kind === 'known' ? [player] : []))
    expect(new Set(known.map(player => player.form.state))).toEqual(new Set(['ok', 'pending', 'failed']))
    expect(known.some(player => player.rank.state === 'ok' && player.rank.value === null)).toBe(true)
    expect(view.players.filter(player => player.kind === 'hidden')).toHaveLength(1)
    expect(view.players.filter(player => player.isMe)).toHaveLength(1)
  })
})

describe('RecordingsPublishLink', () => {
  const url = 'https://www.youtube.com/watch?v=jNQXAC9IVRw'
  const stubs = {
    UIcon: true,
    UInput: true,
    UButton: { props: ['label', 'ariaLabel'], template: '<button :aria-label="ariaLabel">{{ label }}</button>' },
  }

  async function link(video: Clip['video']) {
    const [{ mount }, { computed }, { default: RecordingsPublishLink }, { devLibrary }] = await Promise.all([
      import('@vue/test-utils'),
      import('vue'),
      import('../app/components/recordings/RecordingsPublishLink.vue'),
      import('../app/fixtures/recordings'),
    ])
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('useClipPublish', () => ({ openVideo: vi.fn() }))
    const clip = { ...devLibrary().clips[0]!, video }
    return mount(RecordingsPublishLink, { props: { clip, saving: false, error: null, link: '' }, global: { stubs } })
  }

  it('offers to forget the link and free the file only while the file is here', async () => {
    const both = await link({ kind: 'both', url })
    expect(both.text()).toContain(url)
    expect(both.find('[aria-label="Forget the link"]').exists()).toBe(true)
    expect(both.text()).toContain('Remove from this PC')

    const youtube = await link({ kind: 'youtube', url })
    expect(youtube.find('[aria-label="Forget the link"]').exists()).toBe(false)
    expect(youtube.text()).not.toContain('Remove from this PC')

    const local = await link({ kind: 'local' })
    expect(local.find('form').exists()).toBe(true)
    vi.unstubAllGlobals()
  })
})
