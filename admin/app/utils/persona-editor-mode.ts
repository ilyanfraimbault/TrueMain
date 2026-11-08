/**
 * What the known-player editor is doing (#2471), keyed by the player it does it to.
 * The two flags it replaces (`renaming`, `busy`) and the draft name carried no
 * player: a rename opened on A survived a ⌘K jump to B — Save renamed B to A's
 * name — and survived A's removal.
 *
 * - `view`: reading.
 * - `renaming`: the name field is open on `personaId` with `draft`.
 * - `saving`: one edit (`action`) is in flight for `personaId`; `draft` is the
 *   rename to reopen if a rename fails.
 */
export type PersonaEditAction = 'rename' | 'addAccount' | 'unlinkAccount' | 'addLink' | 'removeLink' | 'remove'

export type PersonaEditorMode =
  | { s: 'view' }
  | { s: 'renaming', personaId: string, draft: string }
  | { s: 'saving', personaId: string, action: PersonaEditAction, draft: string | null }

export const VIEW_MODE: PersonaEditorMode = { s: 'view' }

/** The mode as `personaId` sees it: another player's rename or save reads as `view`. */
export function modeFor(mode: PersonaEditorMode, personaId: string): PersonaEditorMode {
  return mode.s === 'view' || mode.personaId === personaId ? mode : VIEW_MODE
}

/** An edit starts on `personaId`; an open rename of that player is kept to reopen on failure. */
export function startSaving(mode: PersonaEditorMode, personaId: string, action: PersonaEditAction, draft?: string): PersonaEditorMode {
  const current = modeFor(mode, personaId)
  const kept = draft ?? (current.s === 'renaming' ? current.draft : null)
  return { s: 'saving', personaId, action, draft: kept }
}

/**
 * The edit on `personaId` is done. A failed rename reopens its draft; anything else
 * goes back to `view`. When the mode already moved on (another player, a reset),
 * it is left alone: a late answer never rewrites what the operator is doing now.
 */
export function settleSaving(mode: PersonaEditorMode, personaId: string, ok: boolean): PersonaEditorMode {
  if (mode.s !== 'saving' || mode.personaId !== personaId) {
    return mode
  }
  return !ok && mode.action === 'rename' && mode.draft !== null
    ? { s: 'renaming', personaId, draft: mode.draft }
    : VIEW_MODE
}
