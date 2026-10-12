import type { Clip } from '~/types/recordings'
import { getQueueLabel } from '~/utils/queues'

/**
 * What a clip's publish dialog hands to YouTube's upload form (#1815): the
 * title, a description built from what the app knows about the game, and the
 * tags. The player reviews and copies each one; nothing is sent from here.
 */

/** YouTube's own limits: a title of 100 characters, a description of 5,000. */
export const YOUTUBE_TITLE_MAX = 100
export const YOUTUBE_DESCRIPTION_MAX = 5_000

/**
 * The public site the description links to — always production, whatever site
 * the build reads: the video is public, preprod is not.
 */
const PUBLIC_SITE = 'https://truemain.lol'

export interface ClipPublishText {
  title: string
  description: string
  /** As YouTube's tag field takes them: comma-separated. */
  tags: string
}

/** YouTube refuses `<` and `>` in a title or a description. */
const youtubeSafe = (text: string) => text.replace(/[<>]/g, '')

export function clipPublishText(
  clip: Pick<Clip, 'title' | 'championId' | 'win' | 'kills' | 'deaths' | 'assists' | 'queueId'>,
  champion: { name: string, alias: string | null } | null,
): ClipPublishText {
  const kda = clip.kills !== null && clip.deaths !== null && clip.assists !== null
    ? `${clip.kills}/${clip.deaths}/${clip.assists}`
    : null
  const result = clip.win === null ? null : clip.win ? 'Victory' : 'Defeat'
  const line = [champion?.name, result, kda, getQueueLabel(clip.queueId)].filter(Boolean).join(' · ')
  const page = champion?.alias ? `${PUBLIC_SITE}/champions/${champion.alias.toLowerCase()}` : PUBLIC_SITE
  const recordedWith = champion?.alias
    ? `Recorded with TrueMain. ${champion.name} builds, runes and matchups from its one-trick players: ${page}`
    : `Recorded with TrueMain: ${page}`

  return {
    title: youtubeSafe(clip.title).trim().slice(0, YOUTUBE_TITLE_MAX),
    description: youtubeSafe([line, recordedWith].join('\n\n')),
    tags: ['League of Legends', champion?.name].filter(Boolean).join(', '),
  }
}
