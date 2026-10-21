import type { AutoClipKind, Clip } from '~/types/recordings'
import { isMarked } from '~/utils/recording-moments'

/**
 * The automatic clips (#1766): the plays the shell cuts as clips of their own
 * once a game is finalised, and how a clip says where it came from.
 */

/** Every kind, in the shell's order (`AutoClipKind::ALL`), named as the settings list them. */
export const AUTO_CLIP_KINDS: { value: AutoClipKind, label: string, description: string }[] = [
  { value: 'multiKill', label: 'Multi-kills', description: 'A triple kill, a quadra kill, a pentakill.' },
  { value: 'combo', label: 'Kills into an objective', description: 'A multi-kill, then a dragon, a Baron, an inhibitor.' },
  { value: 'steal', label: 'Steals', description: 'An epic monster your side stole.' },
  { value: 'ace', label: 'Aces', description: 'Your side aced the other, you in the fight.' },
  { value: 'teamfight', label: 'Teamfights', description: 'Four champions or more down, you in the fight.' },
  { value: 'firstBlood', label: 'First blood', description: 'When you draw it.' },
]

const SHELL_ORDER: AutoClipKind[] = ['multiKill', 'steal', 'ace', 'firstBlood', 'teamfight', 'combo']

/** The kinds with one turned on or off, in the shell's order. */
export function toggleAutoClip(kinds: readonly AutoClipKind[], kind: AutoClipKind, on: boolean): AutoClipKind[] {
  return SHELL_ORDER.filter(entry => (entry === kind ? on : kinds.includes(entry)))
}

/** A clip's icon and what it says: marked in game, clipped automatically, or cut by hand. */
export function clipOrigin(clip: Pick<Clip, 'moments' | 'automatic'>): { icon: string, label: string } {
  if (clip.moments.some(isMarked)) return { icon: 'i-lucide-bookmark', label: 'Marked in game' }
  if (clip.automatic.length) return { icon: 'i-lucide-sparkles', label: 'Clipped automatically' }
  return { icon: 'i-lucide-scissors', label: 'Clip' }
}
