import { describe, expect, it } from 'vitest'
import { clipPublishText } from '../app/utils/clip-publish'

const clip = { title: 'Triple kill on Ahri', championId: 103, win: true, kills: 12, deaths: 3, assists: 8, queueId: 420 }
const ahri = { name: 'Ahri', alias: 'Ahri' }

describe('clipPublishText', () => {
  it('describes the game and links the champion on the public site', () => {
    const text = clipPublishText(clip, ahri)
    expect(text.title).toBe('Triple kill on Ahri')
    expect(text.description).toBe(
      'Ahri · Victory · 12/3/8 · Solo/Duo\n\n'
      + 'Recorded with TrueMain. Ahri builds, runes and matchups from its one-trick players: https://truemain.lol/champions/ahri',
    )
    expect(text.tags).toBe('League of Legends, Ahri')
  })

  it('leaves out what the game does not know yet', () => {
    const text = clipPublishText({ ...clip, win: null, kills: null }, null)
    expect(text.description).toBe('Solo/Duo\n\nRecorded with TrueMain: https://truemain.lol')
    expect(text.tags).toBe('League of Legends')
  })

  it('drops what YouTube refuses and keeps to its title length', () => {
    const text = clipPublishText({ ...clip, title: `<b>${'a'.repeat(120)}` }, ahri)
    expect(text.title.startsWith('ba')).toBe(true)
    expect(text.title).toHaveLength(100)
    expect(text.description).not.toMatch(/[<>]/)
  })
})
