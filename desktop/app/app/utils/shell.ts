import type { commands } from '~/generated/shell'

/**
 * The shell's commands and events, generated from Rust
 * (`desktop/src-tauri/src/contract.rs`): the webview's only way to the shell.
 */
export { commands as shell, events as shellEvents } from '~/generated/shell'

export type ShellCommands = typeof commands
export type ShellCommand = keyof ShellCommands
/** What `commands[K]` resolves to. */
export type ShellAnswer<K extends ShellCommand> = Awaited<ReturnType<ShellCommands[K]>>

/**
 * What a command that forwards a TrueMain API read answers, as the caller's
 * type: the shell passes the API's JSON through untouched, so its shape is the
 * API's contract, not the shell's.
 */
export async function apiAnswer<T>(answer: Promise<unknown>): Promise<T> {
  return await answer as T
}

interface ShellEvent<T> {
  listen: (handler: (event: { payload: T }) => void) => Promise<() => void>
}

/**
 * Follow a value the shell pushes: listen to its event, then read its current
 * value — in that order, so a change landing between the two is never lost.
 * A read that an event overtook is older than what is already applied, and is
 * dropped. Resolves to the function that stops listening.
 */
export async function follow<T>(event: ShellEvent<T>, read: () => Promise<T>, apply: (value: T) => void): Promise<() => void> {
  let pushed = 0
  const stop = await event.listen(({ payload }) => {
    pushed++
    apply(payload)
  })
  const before = pushed
  const current = await read()
  if (pushed === before) apply(current)
  return stop
}
