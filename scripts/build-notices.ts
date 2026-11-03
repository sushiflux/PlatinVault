// Generates THIRD-PARTY-NOTICES.md, the third-party notices that ship with every PlatinVault artifact (Docker image, bridge ZIP,
// in-app "Lizenzen" viewer). Run it through scripts/build-notices.ps1 (or "node scripts/build-notices.ts [--check]", Node 24 runs
// TypeScript directly) whenever dependencies change; at the latest before a beta/release ("-Check" must report it as current).
//
// What goes into the file is what actually ships, not what merely sits in a dependency tree:
//   (a) browser app: the production closure of frontend/package-lock.json (the root "dependencies" and everything they pull in,
//       optional peer dependencies excluded); licence type and licence file from frontend/node_modules (a superset of the
//       bundled code: Node-only and type-only packages of a dependency are listed too)
//   (b) server, bridge, Windows program and installer: the NuGet packages of the PUBLISHED build (deps.json of a temporary
//       "dotnet publish" of each project, built with --artifacts-path into the temp folder),
//       so build-time-only packages (EF Core Design, Roslyn, analyzers, ILLink) are correctly left out
//   (c) .NET runtime, Windows Desktop runtime and ASP.NET Core runtime (licence and third-party notices of the official runtime
//       packs in the NuGet cache), because the bridge exe is self-contained and the image runs on the official runtime
//   (d) bundled media and fonts from scripts/notices-data/sources.json (PDF fonts, trophy sounds). SQLite itself ships inside the NuGet
//       package SQLitePCLRaw.lib.e_sqlite3; its licence (public domain) is part of the SQLitePCLRaw NOTICE text in notices-data/texts
// Identical licence texts are written once ("L01", ...) and referenced. A component without a determinable licence or licence
// text makes the script fail loudly with the list; nothing is skipped silently and the file is not touched in that case.
//
// Console output is German and ASCII-only (like the other scripts); the generated file is UTF-8 and German.

import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const dataDir = join(root, 'scripts', 'notices-data')
const outputFile = join(root, 'THIRD-PARTY-NOTICES.md')
const checkOnly = process.argv.includes('--check')

type TextKind = 'license' | 'notices'

interface LicenseText {
  id: string
  kind: TextKind
  title: string
  text: string
  users: string[]
}

interface Component {
  name: string
  version: string
  license: string
  copyright: string[]
  source: string
  usedIn: string
  texts: LicenseText[]
  note?: string
  licenseUrl?: string
  asset?: boolean
}

interface Overrides {
  templates: Record<string, string>
  textOverrides: Record<string, { file: string; source: string; why: string }>
  // Packages that declare their licence only as a file (<license type="file">): the SPDX id that file was read as, plus phrases the
  // file must still contain - a new package version with a different licence text then fails instead of passing unseen
  licenseFileIds?: Record<string, { spdx: string; mustContain: string[]; why: string }>
  assets: {
    name: string
    version: string
    usedIn: string
    license: string
    copyright: string[]
    source: string
    textFile?: string
    licenseUrl?: string
    note?: string
    mustExist?: string[]
  }[]
}

const sources = JSON.parse(readFileSync(join(dataDir, 'sources.json'), 'utf8')) as Overrides
const problems: string[] = []

function log(message: string): void {
  console.log(message)
}

// ---------------------------------------------------------------------------------------------------------------------------
// Licence text registry: identical texts are stored once and referenced by id
// ---------------------------------------------------------------------------------------------------------------------------

function normalizeText(raw: string): string {
  return raw
    .replace(/^﻿/, '')
    .replace(/\r\n?/g, '\n')
    .split('\n')
    .map((line) => line.trimEnd())
    .join('\n')
    .replace(/^\n+/, '')
    .replace(/\n+$/, '')
}

const registry = new Map<string, LicenseText>()
const counters: Record<TextKind, number> = { license: 0, notices: 0 }

function registerText(raw: string, kind: TextKind, title: string, user: string, preferTitle = false): LicenseText {
  const text = normalizeText(raw)
  const key = kind + ':' + createHash('sha256').update(text).digest('hex')
  let entry = registry.get(key)
  if (!entry) {
    counters[kind] += 1
    const id = (kind === 'license' ? 'L' : 'N') + String(counters[kind]).padStart(2, '0')
    entry = { id, kind, title, text, users: [] }
    registry.set(key, entry)
  }
  if (preferTitle) entry.title = title
  if (!entry.users.includes(user)) entry.users.push(user)
  return entry
}

// ---------------------------------------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------------------------------------

function readTextFile(path: string): string {
  return readFileSync(path, 'utf8')
}

function readDataText(file: string): string {
  return readTextFile(join(dataDir, 'texts', file))
}

function sortKey(name: string): string {
  return name.toLowerCase()
}

function byName(a: { name: string; version: string }, b: { name: string; version: string }): number {
  const x = sortKey(a.name)
  const y = sortKey(b.name)
  if (x !== y) return x < y ? -1 : 1
  return a.version < b.version ? -1 : a.version > b.version ? 1 : 0
}

// Copyright lines of a licence text: lines that start with "Copyright", "(c)" or the copyright sign (first few, unique)
function extractCopyright(text: string): string[] {
  const found: string[] = []
  for (const raw of text.split('\n')) {
    const line = raw.trim().replace(/^[*#>\s-]+/, '')
    if (/^(copyright\b|\(c\)|©)/i.test(line)) {
      const shortened = line.length > 220 ? line.slice(0, 217) + '...' : line
      if (!found.includes(shortened)) found.push(shortened)
    }
    if (found.length >= 4) break
  }
  return found
}

function isUsableLicense(license: string | undefined): license is string {
  if (!license) return false
  const value = license.trim()
  return value.length > 0 && !/^(unlicensed|none|see license in .*|custom)$/i.test(value)
}

function normalizeRepository(value: unknown): string {
  const raw = typeof value === 'string' ? value : value && typeof value === 'object' ? String((value as { url?: unknown }).url ?? '') : ''
  let url = raw.trim()
  if (!url) return ''
  url = url.replace(/^git\+/, '').replace(/^git:\/\//, 'https://').replace(/^ssh:\/\/git@/, 'https://').replace(/^git@github\.com:/, 'https://github.com/')
  if (/^github:/.test(url)) url = 'https://github.com/' + url.slice('github:'.length)
  else if (/^[\w.-]+\/[\w.-]+$/.test(url)) url = 'https://github.com/' + url
  return url.replace(/\.git$/, '')
}

function decodeXml(value: string): string {
  return value
    .replace(/&#x([0-9a-f]+);/gi, (_, hex: string) => String.fromCodePoint(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, dec: string) => String.fromCodePoint(parseInt(dec, 10)))
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&')
}

function run(command: string, args: string[], cwd: string = root): string {
  try {
    return execFileSync(command, args, {
      cwd,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      maxBuffer: 256 * 1024 * 1024,
      env: { ...process.env, DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_CLI_UI_LANGUAGE: 'en' },
    })
  } catch (error) {
    const failure = error as { stdout?: string; stderr?: string; message: string }
    const output = `${failure.stdout ?? ''}\n${failure.stderr ?? ''}`.trim().split('\n').slice(-25).join('\n')
    throw new Error(`"${command} ${args.join(' ')}" ist fehlgeschlagen:\n${output || failure.message}`)
  }
}

// ---------------------------------------------------------------------------------------------------------------------------
// (a) Browser app: npm production dependencies from the lock file
// ---------------------------------------------------------------------------------------------------------------------------

const LICENSE_FILE = /^(licen[sc]e|copying|unlicen[sc]e)([-._].*)?$/i
const MIN_TEXT_LENGTH = 40

interface LockEntry {
  version?: string
  license?: string
  dev?: boolean
  dependencies?: Record<string, string>
  optionalDependencies?: Record<string, string>
  peerDependencies?: Record<string, string>
  peerDependenciesMeta?: Record<string, { optional?: boolean }>
}

// Lockfile keys ("node_modules/a", "node_modules/a/node_modules/b") of everything the production dependencies need: dependencies,
// optionalDependencies and the peer dependencies that are not marked optional. Resolution follows Node: the nearest node_modules.
function productionClosure(packages: Record<string, LockEntry>): Set<string> {
  const resolve = (fromKey: string, name: string): string | null => {
    let base = fromKey
    for (;;) {
      const candidate = `${base ? base + '/' : ''}node_modules/${name}`
      if (packages[candidate]) return candidate
      if (!base) return null
      const index = base.lastIndexOf('/node_modules/')
      base = index >= 0 ? base.slice(0, index) : ''
    }
  }
  const seen = new Set<string>()
  const visit = (key: string) => {
    const entry = packages[key]
    if (!entry) return
    const names = [
      ...Object.keys(entry.dependencies ?? {}),
      ...Object.keys(entry.optionalDependencies ?? {}),
      ...Object.keys(entry.peerDependencies ?? {}).filter((name) => entry.peerDependenciesMeta?.[name]?.optional !== true),
    ]
    for (const name of names) {
      const target = resolve(key, name)
      if (target && !seen.has(target)) {
        seen.add(target)
        visit(target)
      }
    }
  }
  visit('')
  return seen
}

function collectNpm(): Component[] {
  const frontend = join(root, 'frontend')
  if (!existsSync(join(frontend, 'node_modules'))) {
    throw new Error('frontend/node_modules fehlt - bitte zuerst "npm ci" im Ordner frontend ausfuehren.')
  }

  const lock = JSON.parse(readFileSync(join(frontend, 'package-lock.json'), 'utf8')) as { packages: Record<string, LockEntry> }
  const shipped = productionClosure(lock.packages)
  const components: Component[] = []

  for (const [key, entry] of Object.entries(lock.packages)) {
    if (key === '' || !shipped.has(key)) continue

    const dir = join(frontend, key)
    const name = key.slice(key.lastIndexOf('node_modules/') + 'node_modules/'.length)
    const label = `${name}@${entry.version ?? '?'}`
    if (!existsSync(join(dir, 'package.json'))) {
      problems.push(`npm ${label}: ${key} fehlt in node_modules (npm ci ausfuehren).`)
      continue
    }

    const pj = JSON.parse(readTextFile(join(dir, 'package.json'))) as {
      version?: string
      license?: unknown
      licenses?: { type?: string }[]
      author?: unknown
      repository?: unknown
      homepage?: string
    }
    let license = entry.license
    if (!isUsableLicense(license)) license = typeof pj.license === 'string' ? pj.license : pj.licenses?.map((l) => l.type).filter(Boolean).join(' OR ')
    if (!isUsableLicense(license)) {
      problems.push(`npm ${label}: keine Lizenz in package-lock.json/package.json angegeben.`)
      continue
    }

    const files = readdirSync(dir).filter((f) => LICENSE_FILE.test(f) && statSync(join(dir, f)).isFile()).sort()
    const fileTexts = files.map((f) => readTextFile(join(dir, f))).filter((t) => t.trim().length >= MIN_TEXT_LENGTH)

    const texts: LicenseText[] = []
    let copyright: string[] = []
    let note: string | undefined
    if (fileTexts.length > 0) {
      for (const text of fileTexts) {
        copyright.push(...extractCopyright(normalizeText(text)))
        texts.push(registerText(text, 'license', `${license} - ${extractCopyright(normalizeText(text))[0] ?? name}`, label))
      }
    } else {
      const override = sources.textOverrides[`npm:${name}`]
      const template = sources.templates[license]
      if (override) {
        const text = readDataText(override.file)
        copyright = extractCopyright(normalizeText(text))
        texts.push(registerText(text, 'license', `${license} - ${copyright[0] ?? name}`, label))
        note = `Das Paket enthält keine Lizenzdatei; Text aus ${override.source}.`
      } else if (template) {
        texts.push(registerText(readDataText(template), 'license', `${license} (Standardtext, ohne Copyright-Zeile)`, label))
        note = 'Das Paket enthält keine Lizenzdatei; Standardtext der Lizenz, Copyright-Zeile aus den Paketangaben.'
      } else {
        problems.push(`npm ${label}: Lizenz "${license}" angegeben, aber keine Lizenzdatei im Paket und kein Text in scripts/notices-data/sources.json.`)
        continue
      }
    }

    if (copyright.length === 0) {
      const author = typeof pj.author === 'string' ? pj.author : pj.author && typeof pj.author === 'object' ? String((pj.author as { name?: unknown }).name ?? '') : ''
      if (author) copyright = [`Autor laut package.json: ${author}`]
    }
    copyright = [...new Set(copyright)].slice(0, 4)

    components.push({
      name,
      version: entry.version ?? pj.version ?? '?',
      license,
      copyright,
      source: normalizeRepository(pj.repository) || pj.homepage || '',
      usedIn: 'Browser-App (Oberfläche)',
      texts,
      note,
    })
  }

  return components.sort(byName)
}

// ---------------------------------------------------------------------------------------------------------------------------
// (b) + (c) .NET: published build -> deps.json -> NuGet cache
// ---------------------------------------------------------------------------------------------------------------------------

interface DepsLibrary {
  type: string
  path?: string
}

function readDeps(file: string): { packages: { id: string; version: string; path: string }[]; runtimePacks: { id: string; version: string }[] } {
  const deps = JSON.parse(readTextFile(file)) as { libraries: Record<string, DepsLibrary> }
  const packages: { id: string; version: string; path: string }[] = []
  const runtimePacks: { id: string; version: string }[] = []
  for (const [key, library] of Object.entries(deps.libraries)) {
    const slash = key.lastIndexOf('/')
    const id = key.slice(0, slash)
    const version = key.slice(slash + 1)
    if (library.type === 'package') packages.push({ id, version, path: library.path ?? `${id.toLowerCase()}/${version}` })
    else if (library.type === 'runtimepack') runtimePacks.push({ id: id.replace(/^runtimepack\./, ''), version })
  }
  return { packages, runtimePacks }
}

interface Nuspec {
  license: string
  licenseFile: string
  licenseUrl: string
  copyright: string
  authors: string
  projectUrl: string
  repositoryUrl: string
}

function readNuspec(packageDir: string): Nuspec | null {
  const file = existsSync(packageDir) ? readdirSync(packageDir).find((f) => f.toLowerCase().endsWith('.nuspec')) : undefined
  if (!file) return null
  const xml = readTextFile(join(packageDir, file))
  const tag = (name: string): string => decodeXml(xml.match(new RegExp(`<${name}(?:\\s[^>]*)?>([\\s\\S]*?)</${name}>`, 'i'))?.[1]?.trim() ?? '')
  const expression = xml.match(/<license\s+type="expression"[^>]*>([\s\S]*?)<\/license>/i)?.[1]?.trim() ?? ''
  const fileRef = xml.match(/<license\s+type="file"[^>]*>([\s\S]*?)<\/license>/i)?.[1]?.trim() ?? ''
  const repository = xml.match(/<repository\b[^>]*\burl="([^"]*)"/i)?.[1] ?? ''
  return {
    license: decodeXml(expression),
    licenseFile: decodeXml(fileRef),
    licenseUrl: tag('licenseUrl'),
    copyright: tag('copyright'),
    authors: tag('authors'),
    projectUrl: tag('projectUrl'),
    repositoryUrl: decodeXml(repository),
  }
}

function nugetCacheDir(): string {
  const output = run('dotnet', ['nuget', 'locals', 'global-packages', '--list'])
  const match = output.match(/global-packages:\s*(.+)/i)
  const directory = match?.[1]?.trim()
  if (!directory) throw new Error('Der NuGet-Cache (global-packages) konnte nicht ermittelt werden.')
  return directory
}

function licenseFilesIn(dir: string): string[] {
  if (!existsSync(dir)) return []
  return readdirSync(dir)
    .filter((f) => LICENSE_FILE.test(f) && statSync(join(dir, f)).isFile())
    .sort()
}

function noticeFilesIn(dir: string): string[] {
  if (!existsSync(dir)) return []
  return readdirSync(dir)
    .filter((f) => /^third[-_ ]?party[-_ ]?notices?(\..*)?$/i.test(f) && statSync(join(dir, f)).isFile())
    .sort()
}

function nugetComponent(id: string, version: string, packageDir: string, usedIn: string): Component | null {
  const label = `${id}@${version}`
  const nuspec = readNuspec(packageDir)
  if (!nuspec) {
    problems.push(`NuGet ${label}: Paket nicht im NuGet-Cache gefunden (${packageDir}).`)
    return null
  }

  const fileLicence = nuspec.licenseFile ? sources.licenseFileIds?.[`nuget:${id}`] : undefined
  let license = nuspec.license || (nuspec.licenseFile ? (fileLicence?.spdx ?? 'siehe Lizenzdatei') : '')
  if (!isUsableLicense(license)) {
    problems.push(`NuGet ${label}: keine Lizenz im nuspec angegeben (nur licenseUrl "${nuspec.licenseUrl || '-'}").`)
    return null
  }

  const texts: LicenseText[] = []
  let copyright: string[] = []
  let note: string | undefined

  const declaredFile = nuspec.licenseFile ? join(packageDir, nuspec.licenseFile.replace(/\\/g, '/')) : ''
  const fileNames = declaredFile && existsSync(declaredFile) ? [declaredFile] : licenseFilesIn(packageDir).map((f) => join(packageDir, f))
  const fileTexts = fileNames.map(readTextFile).filter((t) => t.trim().length >= MIN_TEXT_LENGTH)

  if (fileLicence && !nuspec.license) {
    const all = fileTexts.map(normalizeText).join('\n')
    const missing = fileLicence.mustContain.filter((phrase) => !all.includes(phrase))
    if (missing.length > 0) {
      problems.push(`NuGet ${label}: Lizenzdatei passt nicht mehr zu ${fileLicence.spdx} (fehlt: ${missing.join(' | ')}) - Lizenz neu pruefen.`)
      license = 'siehe Lizenzdatei'
    }
  }

  if (fileTexts.length > 0) {
    for (const text of fileTexts) {
      copyright.push(...extractCopyright(normalizeText(text)))
      texts.push(registerText(text, 'license', `${license} - ${extractCopyright(normalizeText(text))[0] ?? id}`, label))
    }
  } else {
    const override = sources.textOverrides[`nuget:${id}`]
    const template = sources.templates[license]
    if (override) {
      const text = readDataText(override.file)
      copyright = extractCopyright(normalizeText(text))
      texts.push(registerText(text, 'license', `${license} - ${copyright[0] ?? id}`, label))
      note = `Das Paket enthält keine Lizenzdatei; Text aus ${override.source}.`
    } else if (template) {
      texts.push(registerText(readDataText(template), 'license', `${license} (Standardtext, ohne Copyright-Zeile)`, label))
      note = 'Das Paket enthält keine Lizenzdatei; Standardtext der Lizenz, Copyright-Zeile aus den Paketangaben (nuspec).'
    } else {
      problems.push(`NuGet ${label}: Lizenz "${license}" angegeben, aber keine Lizenzdatei im Paket und kein Text in scripts/notices-data/sources.json.`)
      return null
    }
  }

  // Package-specific third-party notices (for example the runtime's THIRD-PARTY-NOTICES.TXT inside Microsoft.Extensions.*)
  for (const name of noticeFilesIn(packageDir)) {
    texts.push(registerText(readTextFile(join(packageDir, name)), 'notices', `Hinweise zu Drittanbieter-Code (${name})`, label))
  }

  if (copyright.length === 0 && nuspec.copyright) copyright = [nuspec.copyright]
  if (copyright.length === 0 && nuspec.authors) copyright = [`Autoren laut nuspec: ${nuspec.authors}`]
  copyright = [...new Set(copyright)].slice(0, 4)

  return {
    name: id,
    version,
    license,
    copyright,
    source: nuspec.projectUrl || normalizeRepository(nuspec.repositoryUrl),
    usedIn,
    texts,
    note,
  }
}

interface RuntimeDefinition {
  name: string
  packId: string
  usedIn: string
  source: string
}

const RUNTIMES: RuntimeDefinition[] = [
  {
    name: '.NET Runtime (Microsoft.NETCore.App)',
    packId: 'Microsoft.NETCore.App.Runtime.win-x64',
    usedIn: 'Windows-Programm und Installer (eigenständig enthalten), Bridge (eigenständig in PlatinVault.Bridge.exe enthalten) und Docker-Image (offizielles Basis-Image mcr.microsoft.com/dotnet/aspnet:10.0)',
    source: 'https://github.com/dotnet/runtime',
  },
  {
    name: '.NET Windows Desktop Runtime (Microsoft.WindowsDesktop.App, Windows Forms)',
    packId: 'Microsoft.WindowsDesktop.App.Runtime.win-x64',
    usedIn: 'Windows-Programm und Installer (eigenständig enthalten), Bridge (eigenständig in PlatinVault.Bridge.exe enthalten)',
    source: 'https://github.com/dotnet/winforms',
  },
  {
    name: 'ASP.NET Core Runtime (Microsoft.AspNetCore.App)',
    packId: 'Microsoft.AspNetCore.App.Runtime.win-x64',
    usedIn: 'Windows-Programm (eigenständig enthalten, Server im Programm) und Docker-Image (Server, offizielles Basis-Image mcr.microsoft.com/dotnet/aspnet:10.0)',
    source: 'https://github.com/dotnet/aspnetcore',
  },
]

// Restores the runtime pack into the NuGet cache through a throw-away project when it is not there yet (a fresh machine has the
// .NET and Windows Desktop packs from the bridge restore, but not the ASP.NET Core one)
function ensureRuntimePack(cache: string, packId: string, version: string, scratch: string): string {
  const dir = join(cache, packId.toLowerCase(), version)
  if (existsSync(dir)) return dir

  const helper = join(scratch, 'pack-restore')
  mkdirSync(helper, { recursive: true })
  writeFileSync(
    join(helper, 'pack-restore.csproj'),
    `<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><SelfContained>true</SelfContained></PropertyGroup></Project>`,
  )
  log(`   Lade ${packId} ${version} in den NuGet-Cache ...`)
  run('dotnet', ['restore', join(helper, 'pack-restore.csproj')], helper)
  return existsSync(dir) ? dir : join(cache, packId.toLowerCase(), version)
}

function runtimeComponent(def: RuntimeDefinition, version: string, packDir: string): Component | null {
  const label = `${def.packId}@${version}`
  const nuspec = readNuspec(packDir)
  if (!nuspec || !isUsableLicense(nuspec.license)) {
    problems.push(`Laufzeit ${label}: Paket oder Lizenzangabe nicht gefunden (${packDir}).`)
    return null
  }
  const licenseTexts = licenseFilesIn(packDir).map((f) => readTextFile(join(packDir, f))).filter((t) => t.trim().length >= MIN_TEXT_LENGTH)
  if (licenseTexts.length === 0) {
    problems.push(`Laufzeit ${label}: keine Lizenzdatei im Laufzeitpaket (${packDir}).`)
    return null
  }

  const texts: LicenseText[] = []
  const copyright: string[] = []
  for (const text of licenseTexts) {
    const lines = extractCopyright(normalizeText(text))
    copyright.push(...lines)
    texts.push(registerText(text, 'license', `${nuspec.license} - ${lines[0] ?? def.name}`, def.name))
  }
  for (const name of noticeFilesIn(packDir)) {
    texts.push(registerText(readTextFile(join(packDir, name)), 'notices', `Hinweise zu Drittanbieter-Code in ${def.name}`, def.name, true))
  }

  return {
    name: def.name,
    version,
    license: nuspec.license,
    copyright: [...new Set(copyright)].slice(0, 4),
    source: def.source,
    usedIn: def.usedIn,
    texts,
  }
}

interface DotnetResult {
  server: Component[]
  bridge: Component[]
  // Packages of the Windows program that neither the server nor the bridge ship (the server's packages are inside it too)
  desktop: Component[]
  desktopTotal: number
  runtimes: Component[]
  serverUniverse: number | null
  bridgeUniverse: number | null
}

function packageUniverse(project: string): number | null {
  try {
    const output = run('dotnet', ['list', project, 'package', '--include-transitive', '--format', 'json', '--no-restore'])
    const parsed = JSON.parse(output.slice(output.indexOf('{'))) as {
      projects: { frameworks?: { topLevelPackages?: unknown[]; transitivePackages?: unknown[] }[] }[]
    }
    const names = new Set<string>()
    for (const framework of parsed.projects.flatMap((p) => p.frameworks ?? [])) {
      for (const pkg of [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])]) names.add((pkg as { id: string }).id)
    }
    return names.size
  } catch {
    return null
  }
}

function collectDotnet(scratch: string): DotnetResult {
  const serverProject = join(root, 'src', 'Companion.Api', 'Companion.Api.csproj')
  const bridgeProject = join(root, 'src', 'PlatinVault.Bridge', 'PlatinVault.Bridge.csproj')
  const desktopProject = join(root, 'src', 'PlatinVault.Desktop', 'PlatinVault.Desktop.csproj')
  const setupProject = join(root, 'src', 'PlatinVault.Setup', 'PlatinVault.Setup.csproj')
  const serverOut = join(scratch, 'server')
  const bridgeOut = join(scratch, 'bridge')
  const desktopOut = join(scratch, 'desktop')
  const setupOut = join(scratch, 'setup')
  // Builds go to a scratch folder: a running "dotnet watch" of the dev stack keeps its own obj/bin untouched
  const artifacts = ['--artifacts-path', join(scratch, 'dotnet')]
  // The Windows program restores the server project for win-x64: its own build folder, so the two restores never overwrite each other
  const windowsArtifacts = ['--artifacts-path', join(scratch, 'dotnet-windows')]

  log('-> Stelle die NuGet-Pakete wieder her ...')
  for (const project of [serverProject, bridgeProject]) run('dotnet', ['restore', project, ...artifacts])

  log('-> Veroeffentliche den Server in einen temporaeren Ordner (nur fuer die Abhaengigkeitsliste) ...')
  run('dotnet', ['publish', serverProject, '-c', 'Release', '--no-restore', '-p:UseAppHost=false', '-o', serverOut, ...artifacts])
  log('-> Veroeffentliche die Bridge in einen temporaeren Ordner (nur fuer die Abhaengigkeitsliste) ...')
  // Not single-file here: the single-file exe embeds deps.json, a plain self-contained publish leaves it next to the exe.
  run('dotnet', ['publish', bridgeProject, '-c', 'Release', '--no-restore', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-o', bridgeOut, ...artifacts])
  log('-> Veroeffentliche das Windows-Programm und den Installer (nur fuer die Abhaengigkeitsliste) ...')
  run('dotnet', ['publish', desktopProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', desktopOut, ...windowsArtifacts])
  run('dotnet', ['publish', setupProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-o', setupOut, ...windowsArtifacts])

  const cache = nugetCacheDir()
  const serverDeps = readDeps(join(serverOut, 'Companion.Api.deps.json'))
  const bridgeDeps = readDeps(join(bridgeOut, 'PlatinVault.Bridge.deps.json'))

  const server: Component[] = []
  for (const pkg of serverDeps.packages) {
    const component = nugetComponent(pkg.id, pkg.version, join(cache, pkg.path), 'Server (Docker-Image)')
    if (component) server.push(component)
  }
  const bridge: Component[] = []
  for (const pkg of bridgeDeps.packages) {
    const component = nugetComponent(pkg.id, pkg.version, join(cache, pkg.path), 'Bridge (Windows-Programm)')
    if (component) bridge.push(component)
  }

  // Windows program and installer: everything they ship that is not already listed for the server or the bridge
  const known = new Set([...serverDeps.packages, ...bridgeDeps.packages].map((p) => `${p.id.toLowerCase()}@${p.version}`))
  const windowsPackages = [...readDeps(join(desktopOut, 'PlatinVault.deps.json')).packages, ...readDeps(join(setupOut, 'PlatinVault-Setup.deps.json')).packages]
  const desktop: Component[] = []
  const seen = new Set<string>()
  for (const pkg of windowsPackages) {
    const key = `${pkg.id.toLowerCase()}@${pkg.version}`
    if (known.has(key) || seen.has(key)) continue
    seen.add(key)
    const component = nugetComponent(pkg.id, pkg.version, join(cache, pkg.path), 'Windows-Programm (PlatinVault.exe)')
    if (component) desktop.push(component)
  }

  const netcore = bridgeDeps.runtimePacks.find((p) => p.id.toLowerCase().startsWith('microsoft.netcore.app.runtime'))
  if (!netcore) throw new Error('Die Bridge enthaelt laut deps.json keine .NET-Laufzeit (runtimepack) - ist sie noch eigenstaendig veroeffentlicht?')

  const runtimes: Component[] = []
  for (const def of RUNTIMES) {
    const version = bridgeDeps.runtimePacks.find((p) => p.id.toLowerCase() === def.packId.toLowerCase())?.version ?? netcore.version
    const packDir = ensureRuntimePack(cache, def.packId, version, scratch)
    const component = runtimeComponent(def, version, packDir)
    if (component) runtimes.push(component)
  }

  return {
    server: server.sort(byName),
    bridge: bridge.sort(byName),
    desktop: desktop.sort(byName),
    desktopTotal: new Set(windowsPackages.map((p) => `${p.id.toLowerCase()}@${p.version}`)).size,
    runtimes,
    serverUniverse: packageUniverse(serverProject),
    bridgeUniverse: packageUniverse(bridgeProject),
  }
}

// ---------------------------------------------------------------------------------------------------------------------------
// (d) Bundled fonts and media
// ---------------------------------------------------------------------------------------------------------------------------

function collectAssets(): Component[] {
  const components: Component[] = []
  for (const asset of sources.assets) {
    const missing = (asset.mustExist ?? []).filter((p) => !existsSync(join(root, p)))
    if (missing.length > 0) {
      problems.push(`Medium/Schrift "${asset.name}": erwartete Dateien fehlen (${missing.join(', ')}) - Eintrag in sources.json veraltet oder Artefakt nicht erzeugt.`)
      continue
    }
    const texts: LicenseText[] = []
    if (asset.textFile) texts.push(registerText(readDataText(asset.textFile), 'license', `${asset.license} - ${asset.copyright[0] ?? asset.name}`, asset.name))
    components.push({
      name: asset.name,
      version: asset.version,
      license: asset.license,
      copyright: asset.copyright,
      source: asset.source,
      usedIn: asset.usedIn,
      texts,
      note: asset.note,
      licenseUrl: asset.licenseUrl,
      asset: true,
    })
  }
  return components
}

// ---------------------------------------------------------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------------------------------------------------------

function fence(text: string): string {
  let longest = 2
  for (const match of text.matchAll(/`+/g)) longest = Math.max(longest, match[0].length)
  const marker = '`'.repeat(longest + 1)
  return `${marker}text\n${text}\n${marker}`
}

function renderComponent(component: Component): string {
  // Packages show name + version in the heading; media and fonts carry a free-text version, so it gets its own line
  const lines = component.asset ? [`### ${component.name}`, '', `- Stand: ${component.version}`] : [`### ${component.name} ${component.version}`, '']
  lines.push(`- Lizenz: ${component.license}`)
  lines.push(`- Copyright: ${component.copyright.length > 0 ? component.copyright.join(' / ') : '(keine Angabe in der Lizenzdatei)'}`)
  if (component.source) lines.push(`- Quelle: ${component.source}`)
  lines.push(`- Enthalten in: ${component.usedIn}`)
  const licenseRefs = component.texts.filter((t) => t.kind === 'license').map((t) => t.id)
  if (licenseRefs.length > 0) lines.push(`- Lizenztext: ${licenseRefs.join(', ')}`)
  else if (component.licenseUrl) lines.push(`- Lizenztext: ${component.licenseUrl}`)
  const noticeRefs = component.texts.filter((t) => t.kind === 'notices').map((t) => t.id)
  if (noticeRefs.length > 0) lines.push(`- Weitere Hinweise: ${noticeRefs.join(', ')}`)
  if (component.note) lines.push(`- Hinweis: ${component.note}`)
  return lines.join('\n')
}

function renderSection(title: string, intro: string, components: Component[]): string {
  const parts = [`## ${title}`, '', intro, '']
  if (components.length === 0) parts.push('(keine)', '')
  for (const component of components) parts.push(renderComponent(component), '')
  return parts.join('\n')
}

function renderTexts(kind: TextKind): string {
  const entries = [...registry.values()].filter((t) => t.kind === kind)
  const parts: string[] = []
  for (const entry of entries) {
    parts.push(`### ${entry.id} - ${entry.title}`, '')
    parts.push(`Gilt für: ${entry.users.join(', ')}`, '')
    parts.push(fence(entry.text), '')
  }
  return parts.join('\n')
}

function buildDocument(npm: Component[], dotnet: DotnetResult, assets: Component[]): string {
  const licenseTexts = [...registry.values()].filter((t) => t.kind === 'license').length
  const noticeTexts = [...registry.values()].filter((t) => t.kind === 'notices').length
  const universe = (shipped: number, all: number | null): string => (all === null ? `${shipped}` : `${shipped} (von ${all} im Abhängigkeitsbaum)`)

  const out: string[] = []
  out.push('# Drittanbieter-Hinweise / Third-Party Notices', '')
  out.push(
    'PlatinVault enthält Open-Source-Komponenten und frei nutzbare Medien Dritter. Deren Lizenzen verlangen, dass der Urheberrechtsvermerk und der Lizenztext mit jeder Kopie weitergegeben werden - das geschieht mit dieser Datei. Die Komponenten bleiben unter ihren eigenen Lizenzen; PlatinVault selbst ist freie Software unter der GNU Affero General Public License, Version 3 (AGPL-3.0-only), Lizenztext in der Datei `LICENSE`, Quellcode unter https://github.com/sushiflux/PlatinVault.',
    '',
  )
  out.push(
    'Aufgeführt ist, was tatsächlich im ausgelieferten Programm steckt: bei den .NET-Paketen der Inhalt des veröffentlichten Builds (reine Entwicklungswerkzeuge wie EF-Core-Design oder Roslyn sind nicht darin), bei der Browser-App alle Produktionsabhängigkeiten laut `package-lock.json` (eine Obermenge des tatsächlich gebündelten Codes). Gleiche Lizenztexte stehen nur einmal im Abschnitt „Lizenztexte“ und werden mit ihrer Kennung (L01, L02, ...) angegeben. Diese Datei wird erzeugt (`scripts/build-notices.ps1`) und nicht von Hand geändert.',
    '',
  )
  out.push(
    'English: PlatinVault contains third-party open-source components and freely usable media. Their licences require that the copyright notice and licence text accompany every copy; this file provides them. The components remain under their own licences; PlatinVault itself is free software under the GNU Affero General Public License, version 3 (AGPL-3.0-only), licence text in the file `LICENSE`, source code at https://github.com/sushiflux/PlatinVault. The component descriptions are German, the licence texts are the original (mostly English) texts. Identical texts are printed once in the section "Lizenztexte" and referenced by id (L01, L02, ...).',
    '',
  )

  out.push('## Überblick', '')
  out.push('| Bereich | Komponenten | Enthalten in |', '| --- | --- | --- |')
  out.push(`| Browser-App (npm) | ${npm.length} | Docker-Image (Oberfläche) |`)
  out.push(`| Server (NuGet) | ${universe(dotnet.server.length, dotnet.serverUniverse)} | Docker-Image |`)
  out.push(`| Bridge (NuGet) | ${universe(dotnet.bridge.length, dotnet.bridgeUniverse)} | Bridge-Programm (ZIP-Download, auch im Docker-Image) |`)
  out.push(`| Windows-Programm (NuGet, zusätzlich) | ${dotnet.desktop.length} (von ${dotnet.desktopTotal} enthaltenen; die übrigen stehen beim Server) | Windows-Programm \`PlatinVault.exe\` |`)
  out.push(`| .NET-Laufzeiten | ${dotnet.runtimes.length} | Windows-Programm, Installer, Bridge und Docker-Image |`)
  out.push(`| Schriften, Medien und mitgelieferte Programme | ${assets.length} | Handbuch-PDFs, Browser-App |`)
  out.push(`| Verschiedene Lizenztexte | ${licenseTexts} | - |`)
  out.push(`| Weitere Drittanbieter-Hinweise | ${noticeTexts} | - |`)
  out.push('')

  out.push(
    renderSection(
      'Browser-App (npm-Pakete)',
      'Quelle: `frontend/package-lock.json`, nur Produktionsabhängigkeiten. Lizenzart und Lizenzdatei stammen aus den installierten Paketen.',
      npm,
    ),
  )
  out.push(renderSection('Server (NuGet-Pakete)', 'Quelle: Abhängigkeitsliste (`deps.json`) des veröffentlichten Servers `Companion.Api`.', dotnet.server))
  out.push(
    renderSection(
      'Bridge (NuGet-Pakete)',
      'Quelle: Abhängigkeitsliste (`deps.json`) der veröffentlichten, eigenständigen Bridge `PlatinVault.Bridge`.',
      dotnet.bridge,
    ),
  )
  out.push(
    renderSection(
      'Windows-Programm (NuGet-Pakete)',
      'Quelle: Abhängigkeitslisten (`deps.json`) des veröffentlichten Windows-Programms `PlatinVault.Desktop` (enthält den Server) und des Installers `PlatinVault.Setup`. Aufgeführt sind nur die Pakete, die nicht schon beim Server oder bei der Bridge stehen.',
      dotnet.desktop,
    ),
  )
  out.push(
    '### Microsoft Edge WebView2 Runtime',
    '',
    '- Das Programmfenster des Windows-Programms zeigt die Oberfläche mit der „Microsoft Edge WebView2 Runtime“. Sie ist **nicht** Teil des Installers: Windows 11 bringt sie mit, auf Windows 10 installiert sie bei Bedarf Microsofts eigener Installer (Evergreen Bootstrapper, von Microsoft heruntergeladen) unter Microsofts eigenen Bedingungen.',
    '',
  )
  out.push(
    renderSection(
      '.NET-Laufzeiten',
      'Windows-Programm, Installer und Bridge sind eigenständig veröffentlicht (die Laufzeit steckt in ihren Dateien), der Server läuft im Docker-Image auf der offiziellen Laufzeit. Lizenz und Drittanbieter-Hinweise stammen aus den offiziellen Laufzeitpaketen von Microsoft.',
      dotnet.runtimes,
    ),
  )
  out.push(
    '### Linux-Basis-Image des Docker-Images',
    '',
    '- Das Docker-Image baut auf dem offiziellen Image `mcr.microsoft.com/dotnet/aspnet:10.0` auf (zuletzt geprüft: Ubuntu 24.04). Die Betriebssystem-Pakete dieses Images bringen ihre eigenen Lizenzhinweise mit; sie liegen im Image unter `/usr/share/doc/<Paket>/copyright`, die der .NET-Laufzeit unter `/usr/share/dotnet/`.',
    '- Die Datenbank ist SQLite (gemeinfrei) und steckt in der App selbst (NuGet-Paket SQLitePCLRaw.lib.e_sqlite3, siehe oben); es gibt keinen eigenen Datenbank-Container.',
    '',
  )
  out.push(
    renderSection(
      'Schriften, Medien und mitgelieferte Programme',
      'Dateien, die als Teil des Produkts ausgeliefert werden und keine Pakete sind.',
      assets,
    ),
  )

  out.push('## Lizenztexte', '', 'Die Lizenztexte im Original. Die Kennung steht bei jeder Komponente oben.', '')
  out.push(renderTexts('license'))
  out.push(
    '## Weitere Hinweise von Drittanbietern',
    '',
    'Zusätzliche Hinweise zu Code Dritter innerhalb der Laufzeiten und Pakete, unverändert aus den Paketen übernommen.',
    '',
  )
  out.push(renderTexts('notices'))

  return out.join('\n').replace(/\n{3,}/g, '\n\n').replace(/\s+$/, '') + '\n'
}

// ---------------------------------------------------------------------------------------------------------------------------
// Licence policy (project rule "Dependency licences: free and permissive only")
// ---------------------------------------------------------------------------------------------------------------------------

interface LicencePolicy {
  allowed: string[]
  askFirst: string[]
  approved: Record<string, string>
}

// "A OR B" is fine when one alternative is allowed (we may choose it); "A AND B" needs every part allowed
function licenceAllowed(expression: string, allowed: Set<string>): boolean {
  const normalized = expression.replace(/[()]/g, ' ').trim()
  if (/\s+AND\s+/i.test(normalized)) return normalized.split(/\s+AND\s+/i).every((part) => licenceAllowed(part, allowed))
  if (/\s+OR\s+/i.test(normalized)) return normalized.split(/\s+OR\s+/i).some((part) => licenceAllowed(part, allowed))
  return allowed.has(normalized)
}

function checkLicencePolicy(components: Component[]): string[] {
  const policy = JSON.parse(readFileSync(join(dataDir, 'licence-policy.json'), 'utf8')) as LicencePolicy
  const allowed = new Set(policy.allowed)
  const askFirst = new Set(policy.askFirst)
  const violations: string[] = []
  for (const component of components) {
    const key = `${component.name}@${component.version}`
    if (policy.approved[key] || licenceAllowed(component.license, allowed)) continue
    const parts = component.license.replace(/[()]/g, ' ').split(/\s+(?:AND|OR)\s+/i).map((p) => p.trim())
    const kind = parts.some((p) => askFirst.has(p)) ? 'nur nach Rueckfrage beim Nutzer' : 'nicht erlaubt (Copyleft, eingeschraenkt oder unklar)'
    violations.push(`${key} (${component.usedIn}): Lizenz "${component.license}" - ${kind}`)
  }
  return violations
}

// Main
// ---------------------------------------------------------------------------------------------------------------------------

function main(): void {
  const scratch = mkdtempSync(join(tmpdir(), 'pv-notices-'))
  try {
    log('-> Lese die Browser-App-Abhaengigkeiten (frontend/package-lock.json) ...')
    const npm = collectNpm()
    const dotnet = collectDotnet(scratch)
    log('-> Lese Schriften und Medien ...')
    const assets = collectAssets()

    if (problems.length > 0) {
      console.error('')
      console.error(`FEHLER: ${problems.length} Komponente(n) ohne bestimmbare Lizenz bzw. ohne Lizenztext - THIRD-PARTY-NOTICES.md wurde NICHT geschrieben:`)
      for (const problem of problems) console.error(`  - ${problem}`)
      console.error('Abhilfe: Lizenz pruefen und Text in scripts/notices-data/texts/ ablegen + in scripts/notices-data/sources.json eintragen.')
      process.exitCode = 1
      return
    }

    const violations = checkLicencePolicy([...npm, ...dotnet.server, ...dotnet.bridge, ...dotnet.desktop, ...dotnet.runtimes, ...assets])
    if (violations.length > 0) {
      console.error('')
      console.error(`FEHLER: ${violations.length} Komponente(n) mit einer Lizenz ausserhalb der Freigabeliste - THIRD-PARTY-NOTICES.md wurde NICHT geschrieben:`)
      for (const violation of violations) console.error(`  - ${violation}`)
      console.error('Abhilfe: freie Alternative mit freizuegiger Lizenz waehlen, oder - nur nach Entscheidung des Nutzers - in')
      console.error('scripts/notices-data/licence-policy.json unter "approved" mit Grund und Datum eintragen.')
      process.exitCode = 1
      return
    }
    log('   Lizenz-Freigabeliste:  alle Komponenten erlaubt')

    const document = buildDocument(npm, dotnet, assets)
    log('')
    log(`   Browser-App (npm):     ${npm.length} Pakete`)
    log(`   Server (NuGet):        ${dotnet.server.length} Pakete (Abhaengigkeitsbaum: ${dotnet.serverUniverse ?? 'unbekannt'})`)
    log(`   Bridge (NuGet):        ${dotnet.bridge.length} Pakete (Abhaengigkeitsbaum: ${dotnet.bridgeUniverse ?? 'unbekannt'})`)
    log(`   Windows-Programm:      ${dotnet.desktop.length} zusaetzliche Pakete (enthalten: ${dotnet.desktopTotal})`)
    log(`   .NET-Laufzeiten:       ${dotnet.runtimes.length}`)
    log(`   Schriften und Medien:  ${assets.length}`)
    log(`   Lizenztexte:           ${[...registry.values()].filter((t) => t.kind === 'license').length} verschiedene, ${[...registry.values()].filter((t) => t.kind === 'notices').length} weitere Hinweise`)

    if (checkOnly) {
      const current = existsSync(outputFile) ? normalizeText(readTextFile(outputFile)) : ''
      if (current === normalizeText(document)) {
        log('OK: THIRD-PARTY-NOTICES.md ist aktuell.')
      } else {
        console.error('VERALTET: THIRD-PARTY-NOTICES.md passt nicht mehr zu den Abhaengigkeiten - bitte .\\scripts\\build-notices.ps1 ausfuehren.')
        process.exitCode = 1
      }
      return
    }

    writeFileSync(outputFile, document, 'utf8')
    log(`OK: ${outputFile} geschrieben (${Math.round(document.length / 1024)} KB).`)
  } finally {
    rmSync(scratch, { recursive: true, force: true })
  }
}

try {
  main()
} catch (error) {
  console.error(`FEHLER: ${(error as Error).message}`)
  process.exitCode = 1
}
