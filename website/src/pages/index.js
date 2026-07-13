import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import Link from '@docusaurus/Link';

const CSHARP = `using RobloxCS;
using static Roblox.Globals;

[Server, Script]
public static class Greeter
{
    static int Add(int a, int b) => a + b;

    public static void Main() =>
        print($"sum = {Add(1, 2)}");
}`;

const LUAU = `local RBXCS = require(game:GetService("ReplicatedStorage").rbxcs.runtime.RBXCS)
local Greeter = RBXCS.class("Greeter", nil)
function Greeter.Add(a, b)
	return a + b
end
function Greeter.Main()
	print(\`sum = {Greeter.Add(1, 2)}\`)
end
Greeter.Main()`;

const FEATURES = [
  {
    title: 'Faithful semantics, not a subset',
    body: (
      <>
        C# OOP lowers to Luau metatables. Structs copy by value, exceptions dispatch, <code>async</code>{' '}
        becomes a Promise. The real language, lowered.
      </>
    ),
  },
  {
    title: 'One NuGet, zero setup',
    body: (
      <>
        Ships as a single <code>RobloxCS.Sdk</code> package. <code>dotnet build</code> emits a Rojo tree
        of <code>.luau</code>. No config, no generated code to commit.
      </>
    ),
  },
  {
    title: 'The whole Roblox API, typed',
    body: (
      <>
        Every class, enum and datatype, generated at build from the live API dump. <code>Instance.new</code>,{' '}
        <code>Vector3</code>, services, all with IntelliSense.
      </>
    ),
  },
  {
    title: 'Parallel Luau, checked',
    body: (
      <>
        <code>[Actor]</code> and <code>ParallelLuau.For</code> compile to real Actor pools. The compiler
        rejects any parallel body that touches an unsafe member.
      </>
    ),
  },
];

const STEPS = [
  { n: '01', title: 'Install', body: <>Add the SDK to a C# project: <code>dotnet add package RobloxCS.Sdk</code>.</> },
  { n: '02', title: 'Write', body: <>Write typed C# with full IntelliSense. <code>[Server]</code>/<code>[Client]</code>/<code>[Shared]</code> pick the mount.</> },
  { n: '03', title: 'Build', body: <><code>dotnet build</code> emits idiomatic <code>.luau</code> into a Rojo tree, ready to sync.</> },
];

const EDGES = [
  { k: 'long', v: '64-bit integers lose precision past 2^53, because Luau numbers are f64.' },
  { k: 'Thread', v: 'No shared-memory threads on Roblox. Use [Actor] and ParallelLuau.For instead.' },
  { k: 'parallel', v: "The parallel phase can't write the DataModel. The compiler enforces it." },
];

export default function Home() {
  return (
    <Layout
      title="C# → Luau for Roblox"
      description="rbxcs is a from-scratch C#-to-Luau transpiler for Roblox with faithful semantics, full IntelliSense, and zero setup.">
      <main className="rbx-main">
        <section className="rbx-hero">
          <div className="rbx-wrap rbx-hero-grid">
            <div className="rbx-fade">
              <span className="rbx-eyebrow">C# → Luau for Roblox</span>
              <h1 className="rbx-h1">Write C#. <em>Ship Luau.</em></h1>
              <p className="rbx-lead">
                A from-scratch transpiler with faithful C# semantics, full IntelliSense, and one{' '}
                <code>dotnet build</code>.
              </p>
              <div className="rbx-cta-row">
                <Link className="rbx-btn rbx-btn-primary" to="/docs">Get started</Link>
                <Link className="rbx-btn rbx-btn-ghost" to="/blog">Read the tutorials</Link>
              </div>
            </div>

            <div className="rbx-showcase rbx-fade rbx-fade-2">
              <div className="rbx-panel">
                <div className="rbx-panel-bar"><span className="rbx-tag rbx-tag-cs">C#</span> Greeter.cs</div>
                <CodeBlock language="csharp">{CSHARP}</CodeBlock>
              </div>
              <div className="rbx-arrow" style={{ textAlign: 'center', fontSize: 12 }}>dotnet build ↓</div>
              <div className="rbx-panel">
                <div className="rbx-panel-bar"><span className="rbx-tag rbx-tag-luau">Luau</span> Greeter.server.luau</div>
                <CodeBlock language="lua">{LUAU}</CodeBlock>
              </div>
            </div>
          </div>
        </section>

        <section className="rbx-section">
          <div className="rbx-wrap">
            <p className="rbx-kicker">Why rbxcs</p>
            <h2 className="rbx-h2">C# that becomes real Luau, not a lookalike.</h2>
            <div className="rbx-features">
              {FEATURES.map((f) => (
                <div className="rbx-feature" key={f.title}>
                  <h3>{f.title}</h3>
                  <p>{f.body}</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section className="rbx-section">
          <div className="rbx-wrap">
            <h2 className="rbx-h2">From C# project to Roblox, in three moves.</h2>
            <div className="rbx-steps">
              {STEPS.map((s) => (
                <div className="rbx-step" key={s.n}>
                  <div className="rbx-step-n">{s.n}</div>
                  <h3>{s.title}</h3>
                  <p>{s.body}</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section className="rbx-section">
          <div className="rbx-wrap">
            <div className="rbx-edges">
              <div>
                <h2 className="rbx-h2" style={{ marginBottom: 14 }}>Faithful, not magic.</h2>
                <p className="rbx-sub">
                  Roblox is not .NET. rbxcs is honest about the edges, and the compiler keeps you inside
                  them. Every limit has a tutorial.
                </p>
                <div style={{ marginTop: 22 }}>
                  <Link className="rbx-btn rbx-btn-primary" to="/blog">Read the tutorials</Link>
                </div>
              </div>
              <ul className="rbx-edge-list">
                {EDGES.map((e) => (
                  <li key={e.k}><b>{e.k}</b><span>{e.v}</span></li>
                ))}
              </ul>
            </div>
          </div>
        </section>

        <section className="rbx-final">
          <div className="rbx-wrap">
            <h2 className="rbx-h2">Write your next Roblox game in C#.</h2>
            <Link className="rbx-btn rbx-btn-primary" to="/docs">Get started</Link>
          </div>
        </section>
      </main>
    </Layout>
  );
}
