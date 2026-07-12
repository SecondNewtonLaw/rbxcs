// Pair each sample C# file with its transpiled Luau output into a Docs "Examples" section, shown as a
// C# / Luau tab toggle. This is the transpiler's own output — the samples are built before docs.
// Cross-platform (Node). Usage: node scripts/gen-examples.mjs
import { readdirSync, readFileSync, writeFileSync, rmSync, mkdirSync, statSync } from 'node:fs';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..');
const sampleRoot = join(repo, 'samples', 'HelloWorld');
const luauRoot = join(sampleRoot, 'out-luau');
const out = join(repo, 'website', 'docs', 'examples');

const walk = dir => { try { return readdirSync(dir, { withFileTypes: true }).flatMap(e =>
  e.isDirectory() ? walk(join(dir, e.name)) : [join(dir, e.name)]); } catch { return []; } };

const luauFiles = walk(luauRoot).filter(f => f.endsWith('.luau'));
const stem = f => basename(f).replace(/\.(server|client)?\.?luau$/, '').replace(/\.cs$/, '');
const luauByStem = new Map();
for (const f of luauFiles) if (!luauByStem.has(stem(f))) luauByStem.set(stem(f), f);

const csFiles = ['Shared', 'Server', 'Client']
  .flatMap(d => walk(join(sampleRoot, d)))
  .filter(f => f.endsWith('.cs') && !f.endsWith('.g.cs') && !f.includes(`${'obj'}`) && !f.includes('bin'));

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
writeFileSync(join(out, '_category_.json'), JSON.stringify({
  label: 'Examples', position: 2,
  link: { type: 'generated-index', slug: '/examples', title: 'Examples', description: 'Real sample C# and the exact Luau rbxcs emits for it. Generated from the samples + transpiler output.' },
}, null, 2));

let n = 0;
for (const cs of csFiles.sort()) {
  const name = stem(cs);
  const luau = luauByStem.get(name);
  if (!luau) continue;
  const csCode = readFileSync(cs, 'utf8').trimEnd();
  const luauCode = readFileSync(luau, 'utf8').trimEnd();
  const rel = p => p.slice(repo.length + 1).replace(/\\/g, '/');
  const md = `---
title: ${name}
---

import Tabs from '@theme/Tabs';
import TabItem from '@theme/TabItem';

# ${name}

C# source \`${rel(cs)}\` and the Luau rbxcs emits for it (\`${rel(luau)}\`).

<Tabs groupId="cs-luau">
<TabItem value="cs" label="C#">

\`\`\`csharp
${csCode}
\`\`\`

</TabItem>
<TabItem value="luau" label="Luau">

\`\`\`lua
${luauCode}
\`\`\`

</TabItem>
</Tabs>
`;
  writeFileSync(join(out, `${name}.mdx`), md);
  n++;
}
console.log(`rbxcs: wrote ${n} example pages to website/docs/examples`);
