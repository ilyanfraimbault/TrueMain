// Pure rules over a Bash command string. The hook in register.ts supplies the
// git/GitHub context lazily so a command that triggers no rule costs nothing.

export const PROTECTED = new Set(['develop', 'master'])

// <type>/<issue>-<short-kebab> — the convention of CLAUDE.md "Git & PR workflow".
export const BRANCH_RE = /^(feat|fix|perf|refactor|chore|docs|ci|test|build|style)\/\d+-[a-z0-9][a-z0-9.-]*$/
const EXEMPT_BRANCH_RE = /^(dependabot\/|release\/|renovate\/)/

const TRAILER_RE = /^[ \t]*(Co-Authored-By: Claude[^\n]*|🤖 Generated with \[Claude Code\][^\n]*)\n?/gim

export type GitContext = {
  /** The checked-out branch, or null when detached or unknown. */
  currentBranch: () => Promise<string | null>
  /** Whether origin already has the branch; null when it cannot be told. */
  remoteHasBranch: (branch: string) => Promise<boolean | null>
  /** Paths changed on HEAD against origin/develop; null when unknown. */
  changedFiles: () => Promise<readonly string[] | null>
  /** The base and head branches of a PR (number, URL, branch, or null for the current branch). */
  pr: (selector: string | null) => Promise<{ base: string; head: string } | null>
}

export type Verdict = { deny: string } | { command: string; notes: readonly string[] }

/** Judges one Bash command: a denial with its reason, or the command to run (rewritten or not) and notes. */
export async function judge(command: string, ctx: GitContext): Promise<Verdict> {
  const notes: string[] = []
  let rewritten = command

  if (TRAILER_RE.test(rewritten)) {
    TRAILER_RE.lastIndex = 0
    rewritten = rewritten.replace(TRAILER_RE, '')
    notes.push('Claude attribution trailer removed (CLAUDE.md: no Co-Authored-By / generated-with footers).')
  }
  TRAILER_RE.lastIndex = 0

  for (const segment of segments(rewritten)) {
    const tokens = tokenize(segment)
    const git = indexOfCommand(tokens, 'git')
    const gh = indexOfCommand(tokens, 'gh')

    if (git !== -1) {
      const sub = tokens[git + 1]
      const args = tokens.slice(git + 2)
      if (sub === 'push') {
        const result = await judgePush(args, ctx)
        if ('deny' in result) return result
        if (result.forceRewrite) {
          rewritten = rewritten.replace(/(\bgit\s+push\b[^\n;&|]*?)(\s)(--force|-f)(?=\s|$)/, '$1$2--force-with-lease')
          notes.push('--force rewritten to --force-with-lease.')
        }
      } else if (sub === 'stash') {
        const deny = judgeStash(args)
        if (deny) return { deny }
      }
    }

    if (gh !== -1 && tokens[gh + 1] === 'pr') {
      const verb = tokens[gh + 2]
      const args = tokens.slice(gh + 3)
      if (verb === 'create') {
        const deny = await judgePrCreate(args, ctx)
        if (deny) return { deny }
      } else if (verb === 'merge') {
        const deny = await judgePrMerge(args, ctx)
        if (deny) return { deny }
      }
    }
  }

  return { command: rewritten, notes }
}

type PushVerdict = { deny: string } | { forceRewrite: boolean }

async function judgePush(args: readonly string[], ctx: GitContext): Promise<PushVerdict> {
  const positional: string[] = []
  let isDelete = false
  let hasTags = false
  let hasForce = false
  for (let i = 0; i < args.length; i += 1) {
    const arg = args[i] ?? ''
    if (arg === '--delete' || arg === '-d') isDelete = true
    else if (arg === '--tags') hasTags = true
    else if (arg === '--force' || arg === '-f') hasForce = true
    else if (arg === '-o' || arg === '--push-option' || arg === '--receive-pack' || arg === '--repo') i += 1
    else if (arg.startsWith('-')) continue
    else positional.push(arg)
  }
  const refspecs = positional.slice(1)
  const current = await ctx.currentBranch()

  const targets: { source: string | null; dest: string }[] = []
  if (refspecs.length === 0) {
    if (hasTags) return { forceRewrite: false }
    if (current === null) return { forceRewrite: false }
    targets.push({ source: current, dest: current })
  }
  for (const spec of refspecs) {
    const bare = spec.replace(/^\+/, '')
    if (isDelete) {
      targets.push({ source: null, dest: bare })
    } else if (bare.includes(':')) {
      const [src, dst] = bare.split(':', 2) as [string, string]
      targets.push({ source: src === '' ? null : src === 'HEAD' ? current : src, dest: dst })
    } else {
      targets.push({ source: bare === 'HEAD' ? current : bare, dest: bare === 'HEAD' ? current ?? bare : bare })
    }
  }

  for (const { source, dest } of targets) {
    const destBranch = dest.replace(/^refs\/heads\//, '')
    if (PROTECTED.has(destBranch)) {
      return {
        deny: `pushing straight to '${destBranch}' is not allowed (branch protection, CLAUDE.md "No direct push to develop"). Open a PR instead: feature work targets develop through the ship skill; develop→master goes through the release skill's PR.`,
      }
    }
    if (isDelete) {
      continue
    }
    if (source !== null && source === current) {
      if (!PROTECTED.has(source) && !BRANCH_RE.test(source) && !EXEMPT_BRANCH_RE.test(source)) {
        const onRemote = await ctx.remoteHasBranch(source)
        if (onRemote !== true) {
          return {
            deny: `branch '${source}' does not follow <type>/<issue>-<short-kebab> (e.g. feat/526-rank-scoped-builds; types: feat, fix, perf, refactor, chore, docs, ci, test, build, style). Rename it before the first push: git branch -m <type>/<issue>-<slug>`,
          }
        }
      }
      const files = await ctx.changedFiles()
      if (files !== null) {
        const hasMigration = files.some(path => path.startsWith('backend/Data/Migrations/'))
        const hasCompiledModel = files.some(path => path.startsWith('backend/Data/CompiledModels/'))
        if (hasMigration && !hasCompiledModel) {
          return {
            deny: `the branch adds an EF migration under backend/Data/Migrations/ but backend/Data/CompiledModels/ is unchanged against origin/develop. A stale compiled model silently drops columns: run \`dotnet ef dbcontext optimize\` (from backend/Data, output in Data/CompiledModels), commit the regenerated model, then push.`,
          }
        }
      }
    }
  }

  return { forceRewrite: hasForce }
}

function judgeStash(args: readonly string[]): string | null {
  const sub = args[0]
  const shared = 'the stash stack is shared with the main checkout and every other worktree (another session may push or pop concurrently).'
  if (sub === undefined || sub.startsWith('-')) {
    return `bare \`git stash\` is refused: ${shared} Prefer a temporary WIP commit; if you must stash: git stash push -u -m "<unique-tag>", then git stash list --format='%H %gs' and git stash apply <sha>.`
  }
  if (sub === 'pop') {
    return `\`git stash pop\` is refused: ${shared} It could pop another session's entry. Find yours by tag with git stash list --format='%H %gs', then git stash apply <sha> and drop it afterwards.`
  }
  if (sub === 'clear') {
    return `\`git stash clear\` is refused: ${shared} It would drop every other session's entries. Drop only your own entry, found by its tag.`
  }
  if (sub === 'push' || sub === 'save') {
    const hasMessage = args.some(arg => arg === '-m' || arg === '--message' || arg.startsWith('--message='))
    if (sub === 'save' || !hasMessage) {
      return `\`git stash ${sub}\` without a message is refused: ${shared} Use git stash push -u -m "<unique-tag>" so your entry can be told apart and re-found by tag.`
    }
  }
  return null
}

async function judgePrCreate(args: readonly string[], ctx: GitContext): Promise<string | null> {
  const base = flagValue(args, ['--base', '-B'])
  const head = flagValue(args, ['--head', '-H']) ?? (await ctx.currentBranch())
  if (base === 'master' && head !== 'develop') {
    return `a PR to master from '${head ?? 'this branch'}' is refused: the only PR allowed to target master is the release PR from develop (release skill). Feature PRs target develop (the default base, so drop --base master).`
  }
  if (head !== null && PROTECTED.has(head) && base !== 'master') {
    return `a PR from '${head}' to '${base ?? 'develop'}' is refused: develop only ever opens the release PR to master. Feature work goes on a <type>/<issue>-<slug> branch.`
  }
  return null
}

async function judgePrMerge(args: readonly string[], ctx: GitContext): Promise<string | null> {
  const flags = new Set<string>()
  let selector: string | null = null
  for (const arg of args) {
    if (arg.startsWith('--')) flags.add(arg.split('=')[0] ?? arg)
    else if (arg.startsWith('-') && arg.length > 1) for (const letter of arg.slice(1)) flags.add(`-${letter}`)
    else if (selector === null) selector = arg
  }
  const has = (...names: string[]) => names.some(name => flags.has(name))
  const pr = await ctx.pr(selector)
  if (pr === null) return null

  if (pr.base === 'master') {
    if (has('--squash', '-s', '--rebase', '-r')) {
      return `PR #${selector ?? ''} is a release PR (develop→master): merge it with a merge commit (\`gh pr merge ${selector ?? ''} --merge\`), never squash or rebase — a squash is what creates the false conflicts on the next release.`.replace('# ', '#')
    }
    if (has('--delete-branch', '-d')) {
      return `never delete develop: drop --delete-branch from the release PR merge (\`gh pr merge ${selector ?? ''} --merge\`).`
    }
    if (!has('--merge', '-m')) {
      return `a release PR (develop→master) is merged with an explicit merge commit: \`gh pr merge ${selector ?? ''} --merge\` (no --delete-branch).`
    }
    return null
  }

  if (pr.base === 'develop') {
    if (has('--merge', '-m', '--rebase', '-r')) {
      return `feature PRs into develop are squash-merged: \`gh pr merge ${selector ?? ''} --squash --delete-branch\` (the PR title becomes the squash commit).`
    }
    if (!has('--squash', '-s')) {
      return `say how to merge: feature PRs into develop use \`gh pr merge ${selector ?? ''} --squash --delete-branch\`.`
    }
    if (!has('--delete-branch', '-d')) {
      return `add --delete-branch: the repo does not auto-delete merged branches (\`gh pr merge ${selector ?? ''} --squash --delete-branch\`). From a worktree the local cleanup step may fail after the merge succeeded — then verify with gh pr view and \`git push origin --delete ${pr.head}\`.`
    }
  }
  return null
}

function flagValue(args: readonly string[], names: readonly string[]): string | null {
  for (let i = 0; i < args.length; i += 1) {
    const arg = args[i] ?? ''
    for (const name of names) {
      if (arg === name) return args[i + 1] ?? null
      if (arg.startsWith(`${name}=`)) return arg.slice(name.length + 1)
    }
  }
  return null
}

/** The simple commands of a shell line: split on newlines, `&&`, `||`, `;` and `|` (quotes are not honoured; the rules only read command heads). */
export function segments(command: string): string[] {
  return command
    .split(/\n|&&|\|\||;|\|/)
    .map(part => part.trim())
    .filter(part => part !== '')
}

/** Whitespace tokens, single- and double-quoted strings kept whole and unquoted. */
export function tokenize(segment: string): string[] {
  const tokens: string[] = []
  const re = /'([^']*)'|"([^"]*)"|(\S+)/g
  let match: RegExpExecArray | null
  while ((match = re.exec(segment)) !== null) {
    tokens.push(match[1] ?? match[2] ?? match[3] ?? '')
  }
  return tokens
}

/** Where `name` is run as a command in the tokens: at the head, or after env assignments / `sudo` / `command`. */
function indexOfCommand(tokens: readonly string[], name: string): number {
  for (let i = 0; i < tokens.length; i += 1) {
    const token = tokens[i] ?? ''
    if (/^[A-Za-z_][A-Za-z0-9_]*=/.test(token) || token === 'sudo' || token === 'command' || token === 'env') continue
    if (token === 'cd' || token === 'time' || token === 'nohup') return -1
    return token === name || token.endsWith(`/${name}`) ? i : -1
  }
  return -1
}
