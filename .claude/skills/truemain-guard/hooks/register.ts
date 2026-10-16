import type { Register } from 'claude-code'

import { judge, type GitContext } from './rules'

export const register: Register = on => {
  on('tool.call', { tool: 'Bash' }, async ($, e, next) => {
    if (!/\b(git|gh)\b/.test(e.command) && !/Co-Authored-By: Claude|Generated with \[Claude Code\]/.test(e.command)) {
      return next(e)
    }

    const cwd = await $.session.cwd()
    const run = async (argv: readonly string[], timeoutMs = 15_000) => {
      try {
        return await $.process.run(argv, { cwd, timeoutMs })
      } catch {
        return null
      }
    }

    const ctx: GitContext = {
      currentBranch: async () => {
        const ran = await run(['git', 'rev-parse', '--abbrev-ref', 'HEAD'])
        if (ran === null || ran.exitCode !== 0) return null
        const name = ran.stdout.trim()
        return name === '' || name === 'HEAD' ? null : name
      },
      remoteHasBranch: async branch => {
        const ran = await run(['git', 'ls-remote', '--heads', 'origin', branch], 20_000)
        if (ran === null || ran.exitCode !== 0) return null
        return ran.stdout.trim() !== ''
      },
      changedFiles: async () => {
        const ran = await run(['git', 'diff', '--name-only', 'origin/develop...HEAD'])
        if (ran === null || ran.exitCode !== 0) return null
        return ran.stdout.split('\n').map(line => line.trim()).filter(line => line !== '')
      },
      pr: async selector => {
        const argv = ['gh', 'pr', 'view', ...(selector === null ? [] : [selector]), '--json', 'baseRefName,headRefName']
        const ran = await run(argv, 20_000)
        if (ran === null || ran.exitCode !== 0) return null
        try {
          const parsed = JSON.parse(ran.stdout) as { baseRefName?: string; headRefName?: string }
          if (typeof parsed.baseRefName !== 'string' || typeof parsed.headRefName !== 'string') return null
          return { base: parsed.baseRefName, head: parsed.headRefName }
        } catch {
          return null
        }
      },
    }

    const verdict = await judge(e.command, ctx)
    if ('deny' in verdict) {
      $.ui.toast(`truemain-guard refused a command: ${verdict.deny.slice(0, 80)}…`)
      return { deny: `truemain-guard: ${verdict.deny}` }
    }
    for (const note of verdict.notes) $.ui.toast(`truemain-guard: ${note}`)

    return verdict.command === e.command ? next(e) : next({ ...e, command: verdict.command })
  }).catch(($, e, next) => next(e))
}
