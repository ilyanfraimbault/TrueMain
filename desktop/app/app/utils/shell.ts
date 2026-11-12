import { commands, events } from '~/generated/shell'

export type ShellCommands = typeof commands
export type ShellEvents = typeof events
export type ShellCommand = keyof ShellCommands
/** What `commands[K]` resolves to. */
export type ShellAnswer<K extends ShellCommand> = Awaited<ReturnType<ShellCommands[K]>>

/**
 * Whether the app runs on the dev server in a browser, with no shell: the
 * fake one answers instead (`dev-shell/`). Always false in a production build.
 */
export const fakeShell = import.meta.dev === true && !('__TAURI_INTERNALS__' in globalThis)

/** An object whose methods load `load`'s on first call: the fake shell never reaches a production bundle. */
export function later<T extends object>(load: () => Promise<T>): T {
  return new Proxy({} as T, {
    get: (_, name) => (...args: unknown[]) => load().then(loaded => (loaded[name as keyof T] as (...args: unknown[]) => unknown)(...args)),
  })
}

/** The shell to talk to: the generated bindings, or the fake shell when `fake`. */
export function shellFor(fake: boolean): { shell: ShellCommands, shellEvents: ShellEvents } {
  if (!fake) return { shell: commands, shellEvents: events }
  return {
    shell: later(() => import('~/dev-shell').then(dev => dev.devCommands as unknown as ShellCommands)),
    shellEvents: new Proxy({} as ShellEvents, {
      get: (_, name) => later(() => import('~/dev-shell').then(dev => dev.devEvents[name as keyof ShellEvents])),
    }),
  }
}

/**
 * The shell's commands and events, generated from Rust
 * (`desktop/src-tauri/src/contract.rs`): the webview's only way to the shell —
 * the real one inside the app, the fake one on the dev server.
 */
export const { shell, shellEvents } = shellFor(fakeShell)

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
