// Prompt counter for the five-segment build id vYear.Month.Commit.Prompts.ChangedLines. It lives in .run/devbuild.json
// (git-ignored) and is only written into version.txt by the pre-commit hook - the shown version changes with a commit
// only; the post-commit hook resets it to 0.
// Usage: node scripts/devbuild.mjs --bump=N   adds N prompts (several prompts can belong to one piece of work);
//        node scripts/devbuild.mjs            prints the current count.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const stateFile = join(root, '.run', 'devbuild.json')

let prompts = 0
try {
  prompts = Number(JSON.parse(readFileSync(stateFile, 'utf8')).prompts) || 0
} catch {
  prompts = 0
}

const bumpArg = process.argv.find((arg) => arg.startsWith('--bump'))
if (bumpArg) {
  const amount = Number(bumpArg.split('=')[1] ?? 1)
  if (!Number.isInteger(amount) || amount < 1) {
    console.error('--bump erwartet eine positive ganze Zahl, z. B. --bump=3')
    process.exit(1)
  }
  prompts += amount
  mkdirSync(dirname(stateFile), { recursive: true })
  writeFileSync(stateFile, `${JSON.stringify({ prompts }, null, 2)}\n`)
}

console.log(prompts)
