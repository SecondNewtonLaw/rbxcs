// Generate the rbxcs API reference (markdown) from the XML doc comments on the curated public surface.
// Runs DefaultDocumentation over RobloxCS.Defs, then keeps only the rbxcs-specific types (the generated
// Roblox API mirrors Roblox's own docs and is intentionally excluded). Output: website/docs/api/.
// Cross-platform (Node). Usage: node scripts/gen-api-docs.mjs [Debug|Release]
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..');
const config = process.argv[2] ?? 'Debug';
const out = join(repo, 'website', 'docs', 'api');

const run = (cmd, args) => execFileSync(cmd, args, { stdio: 'inherit', shell: true, cwd: repo });
const find = (dir, pred) => readdirSync(dir, { withFileTypes: true })
  .flatMap(e => e.isDirectory() ? find(join(dir, e.name), pred) : (pred(e.name) ? [join(dir, e.name)] : []));

run('dotnet', ['build', 'src/RobloxCS.Defs/RobloxCS.Defs.csproj', '-c', config, '-v', 'q']);
const binDir = join(repo, 'src', 'RobloxCS.Defs', 'bin', config);
const dll = find(binDir, n => n === 'RobloxCS.Defs.dll')[0];
const xml = find(binDir, n => n === 'RobloxCS.Defs.xml')[0];
if (!dll || !xml) throw new Error('RobloxCS.Defs.dll/.xml not found — build first');

const tmp = mkdtempSync(join(tmpdir(), 'rbxcs-api-'));
run('defaultdocumentation', ['--AssemblyFilePath', dll, '--DocumentationFilePath', xml, '--OutputDirectoryPath', tmp]);

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
// Keep the rbxcs-authored surface only (drops the ~5700 generated Roblox API stub pages), and only
// the TYPE-level page per type (not DefaultDocumentation's per-member pages) — members are already
// tabulated inline on the type page, so a flat page-per-member sidebar is just noise.
const surface = n => n.startsWith('RobloxCS.') || n.startsWith('Roblox.Globals') || n.startsWith('Roblox.SharedTableOps');
const typeLevel = n => {
  const base = n.replace(/\.md$/, '');
  if (base.includes('(')) return false;             // methods / constructors
  return base.split('.').length <= 2;               // namespace (RobloxCS) or namespace.Type
};
const keep = n => n.endsWith('.md') && surface(n) && typeLevel(n);
const keptNames = new Set(readdirSync(tmp).filter(keep));

// A link to a page we didn't keep would 404; render those as plain text (the label) instead.
// DefaultDocumentation always emits `[label](target#anchor 'title')`, and target filenames contain
// parens/commas, so split on the ` 'title'` suffix rather than trying to balance parens.
const stripDeadLinks = md => md.replace(
  /\[((?:[^\]\\]|\\.)*)\]\(([^']*?)\s'[^']*'\)/g,
  (m, label, target) => {
    const file = target.split('#')[0].trim();
    if (!file.endsWith('.md')) return m;          // external URL — keep
    return keptNames.has(file) ? m : label;       // dropped page — plain text
  });

let kept = 0;
for (const f of keptNames) {
  writeFileSync(join(out, f), stripDeadLinks(readFileSync(join(tmp, f), 'utf8')));
  kept++;
}
rmSync(tmp, { recursive: true, force: true });

writeFileSync(join(out, '_category_.json'), JSON.stringify({
  label: 'API Reference',
  position: 3,
  link: { type: 'generated-index', slug: '/api', title: 'API Reference', description: 'The rbxcs-specific C# surface: context/entry attributes, parallel primitives, interop, and project config. Generated from XML doc comments.' },
}, null, 2));

console.log(`rbxcs: wrote ${kept} API pages to website/docs/api`);
