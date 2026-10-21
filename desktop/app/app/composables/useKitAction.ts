/**
 * Runs a change to the player's kits, and says so in a toast when it fails —
 * with the shell's reason when it gave one. Shared by the champion page's
 * kits and the My kits page (#2205).
 */
/** A change to run, and the toast's title should it fail; resolves to whether it went through. */
export type KitAction = (action: () => Promise<unknown>, failure: string) => Promise<boolean>

export function useKitAction(): KitAction {
  const { error } = useKits()
  const toast = useToast()
  return async function run(action: () => Promise<unknown>, failure: string): Promise<boolean> {
    try {
      await action()
      return true
    }
    catch {
      toast.add({ title: failure, description: error.value ?? undefined, icon: 'i-lucide-circle-x', color: 'error' })
      return false
    }
  }
}
