import { describe, expect, test } from 'claude-code/testing'

import { judge, segments, tokenize, type GitContext } from '../hooks/rules'

const ctx = (over: Partial<GitContext> = {}): GitContext => ({
  currentBranch: async () => 'feat/526-rank-scoped-builds',
  remoteHasBranch: async () => false,
  changedFiles: async () => ['web/app/pages/index.vue'],
  pr: async () => ({ base: 'develop', head: 'feat/526-rank-scoped-builds' }),
  ...over,
})

const denial = async (command: string, c = ctx()) => {
  const verdict = await judge(command, c)
  return 'deny' in verdict ? verdict.deny : null
}

describe('git push', () => {
  test('a push of the current conventional branch passes untouched', async () => {
    const verdict = await judge('git push -u origin feat/526-rank-scoped-builds', ctx())
    expect(verdict).toEqual({ command: 'git push -u origin feat/526-rank-scoped-builds', notes: [] })
  })

  test('a push to develop or master is refused, by refspec or by checkout', async () => {
    expect(await denial('git push origin develop')).toMatch(/pushing straight to 'develop'/)
    expect(await denial('git push origin HEAD:master')).toMatch(/pushing straight to 'master'/)
    expect(await denial('git push', ctx({ currentBranch: async () => 'develop' }))).toMatch(/'develop'/)
    expect(await denial('git fetch && git push --force origin master')).toMatch(/'master'/)
  })

  test('an unconventional branch is refused before its first push, allowed once it exists on origin', async () => {
    const slug = ctx({ currentBranch: async () => 'mods-workflow-analysis-9efee6' })
    expect(await denial('git push -u origin mods-workflow-analysis-9efee6', slug)).toMatch(/git branch -m/)
    expect(await denial('git push', slug)).toMatch(/does not follow/)
    expect(await denial('git push', ctx({ currentBranch: async () => 'claude/foo' }))).toMatch(/does not follow/)
    expect(await denial('git push', { ...slug, remoteHasBranch: async () => true })).toBe(null)
  })

  test('tags and deletions of feature branches pass', async () => {
    expect(await denial('git push origin 1.25.0')).toBe(null)
    expect(await denial('git push --tags')).toBe(null)
    expect(await denial('git push origin --delete feat/1-old')).toBe(null)
    expect(await denial('git push origin --delete develop')).toMatch(/'develop'/)
  })

  test('a migration without a regenerated compiled model is refused', async () => {
    const c = ctx({ changedFiles: async () => ['backend/Data/Migrations/20261016_AddFoo.cs'] })
    expect(await denial('git push', c)).toMatch(/dotnet ef dbcontext optimize/)
    const regenerated = ctx({
      changedFiles: async () => ['backend/Data/Migrations/20261016_AddFoo.cs', 'backend/Data/CompiledModels/TrueMainDbContextModel.cs'],
    })
    expect(await denial('git push', regenerated)).toBe(null)
  })

  test('--force becomes --force-with-lease on a feature branch', async () => {
    const verdict = await judge('git push --force origin feat/526-rank-scoped-builds', ctx())
    expect(verdict).toEqual({
      command: 'git push --force-with-lease origin feat/526-rank-scoped-builds',
      notes: ['--force rewritten to --force-with-lease.'],
    })
  })
})

describe('git stash', () => {
  test('bare stash, pop and clear are refused; tagged push, list and apply pass', async () => {
    expect(await denial('git stash')).toMatch(/shared with the main checkout/)
    expect(await denial('git stash pop')).toMatch(/pop/)
    expect(await denial('git stash clear')).toMatch(/clear/)
    expect(await denial('git stash push -u')).toMatch(/without a message/)
    expect(await denial('git stash push -u -m "guard-wip"')).toBe(null)
    expect(await denial("git stash list --format='%H %gs'")).toBe(null)
    expect(await denial('git stash apply abc123')).toBe(null)
  })
})

describe('gh pr create', () => {
  test('a PR to master is only allowed from develop', async () => {
    expect(await denial('gh pr create --base master --title "x" --body "y"')).toMatch(/release PR/)
    expect(await denial('gh pr create --base master --head develop --title "release: 1.25.0" --body "y"')).toBe(null)
    expect(await denial('gh pr create --title "x" --body "y"')).toBe(null)
    expect(await denial('gh pr create --title "x"', ctx({ currentBranch: async () => 'develop' }))).toMatch(/develop only ever opens/)
  })

  test('the generated-with footer and Claude co-author trailers are stripped', async () => {
    const command = [
      'git commit -m "$(cat <<\'EOF\'',
      'feat: add thing',
      '',
      'Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>',
      'EOF',
      ')"',
    ].join('\n')
    const verdict = await judge(command, ctx())
    expect('deny' in verdict).toBe(false)
    if (!('deny' in verdict)) {
      expect(verdict.command).not.toMatch(/Co-Authored-By/)
      expect(verdict.command).toMatch(/feat: add thing\n\nEOF/)
      expect(verdict.notes.length).toBe(1)
    }
    const body = await judge('gh pr create --title "x" --body "Summary\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)\n"', ctx())
    if (!('deny' in body)) expect(body.command).not.toMatch(/Generated with/)
  })
})

describe('gh pr merge', () => {
  test('feature PRs need --squash --delete-branch', async () => {
    expect(await denial('gh pr merge 2063 --squash --delete-branch')).toBe(null)
    expect(await denial('gh pr merge 2063 -sd')).toBe(null)
    expect(await denial('gh pr merge 2063 --squash')).toMatch(/--delete-branch/)
    expect(await denial('gh pr merge 2063 --merge --delete-branch')).toMatch(/squash-merged/)
    expect(await denial('gh pr merge 2063')).toMatch(/--squash --delete-branch/)
  })

  test('release PRs need --merge and keep develop', async () => {
    const release = ctx({ pr: async () => ({ base: 'master', head: 'develop' }) })
    expect(await denial('gh pr merge 2070 --merge', release)).toBe(null)
    expect(await denial('gh pr merge 2070 --squash', release)).toMatch(/merge commit/)
    expect(await denial('gh pr merge 2070 --merge --delete-branch', release)).toMatch(/never delete develop/)
    expect(await denial('gh pr merge 2070', release)).toMatch(/--merge/)
  })

  test('an unknown PR passes', async () => {
    expect(await denial('gh pr merge 1 --rebase', ctx({ pr: async () => null }))).toBe(null)
  })
})

describe('parsing', () => {
  test('segments and tokens', () => {
    expect(segments('cd web && npm ci; git push || echo no')).toEqual(['cd web', 'npm ci', 'git push', 'echo no'])
    expect(tokenize(`git commit -m 'a b' --author "c d" e`)).toEqual(["git", "commit", "-m", "a b", "--author", "c d", "e"])
  })
})
