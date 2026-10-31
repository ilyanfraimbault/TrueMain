/** How long a card armed by an ally's pick intent waits for its second click. */
export const ARMED_MS = 4000

/**
 * A click on a suggested champion (#2381). During our pick it hovers the
 * champion in the client, as a click on it in the client would — no button
 * between the player and the hover; a champion an ally is showing as their
 * pick intent asks first, by arming the card until a second click. Anywhere
 * else (out of turn, a champion the client would refuse or already shows
 * hovered, a tape) the click previews the champion's build, as it always did.
 *
 * Bans keep their own control (`DraftChampionAction`): a ban cannot be undone.
 */
export function useDraftCardClick(mode: () => 'pick' | 'ban', preview: (championId: number) => void) {
  const { writable, kind, pending, availability, allyHovering, allyName, hover } = useChampSelectActions()

  const armed = ref<number | null>(null)
  let disarm: ReturnType<typeof setTimeout> | undefined

  function setArmed(championId: number | null) {
    clearTimeout(disarm)
    armed.value = championId
    if (championId !== null) disarm = setTimeout(() => { armed.value = null }, ARMED_MS)
  }

  onBeforeUnmount(() => clearTimeout(disarm))
  // The draft moved: an armed question no longer stands.
  watch(() => [kind.value, mode()].join(':'), () => setArmed(null))

  /** Whether a click on `championId` hovers it, rather than previewing its build. */
  function hovers(championId: number) {
    return mode() === 'pick' && writable.value && kind.value === 'pick' && availability(championId).allowed
  }

  /** What the card says over its art: the client's hover, why it cannot be hovered, or the armed question. */
  function status(championId: number): { text: string, tone: 'primary' | 'warning' | 'muted' } | null {
    if (mode() !== 'pick' || !writable.value) return null
    if (armed.value === championId) {
      const ally = allyHovering(championId)
      return { text: ally ? `${allyName(ally)}'s pick — click again` : 'Click again', tone: 'warning' }
    }
    const { allowed, reason } = availability(championId)
    if (allowed || !reason || reason.startsWith('Not your turn')) return null
    return { text: reason === 'Hovered in the client' ? 'Hovered' : reason, tone: reason === 'Hovered in the client' ? 'primary' : 'muted' }
  }

  function click(championId: number) {
    if (!hovers(championId)) {
      setArmed(null)
      preview(championId)
      return
    }
    if (pending.value) return
    if (allyHovering(championId) && armed.value !== championId) {
      setArmed(championId)
      return
    }
    setArmed(null)
    void hover(championId)
  }

  return { hovers, status, click, armed }
}
