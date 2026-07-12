// @ts-check
const { themes } = require('prism-react-renderer');

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'rbxcs',
  tagline: 'C# → Luau for Roblox. Faithful semantics, zero setup.',
  favicon: 'img/favicon.svg',
  url: 'https://SecondNewtonLaw.github.io',
  baseUrl: '/rbxcs/',
  organizationName: 'SecondNewtonLaw',
  projectName: 'rbxcs',
  // API pages cross-link to Roblox API type pages that are intentionally not generated.
  onBrokenLinks: 'warn',
  onBrokenMarkdownLinks: 'warn',
  // DefaultDocumentation emits per-parameter anchors whose slugs Docusaurus normalizes differently;
  // the info is still present inline on the type page, so these are non-fatal.
  onBrokenAnchors: 'warn',
  markdown: { mermaid: false },
  i18n: { defaultLocale: 'en', locales: ['en'] },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: require.resolve('./sidebars.js'),
          editUrl: 'https://github.com/SecondNewtonLaw/rbxcs/tree/main/website/',
        },
        blog: {
          showReadingTime: true,
          blogTitle: 'rbxcs tutorials',
          blogDescription: 'Deep-dive tutorials on the sharp edges: what rbxcs supports, what it does not, and why.',
          postsPerPage: 10,
          blogSidebarTitle: 'All tutorials',
          blogSidebarCount: 'ALL',
        },
        theme: { customCss: require.resolve('./src/css/custom.css') },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      navbar: {
        title: 'rbxcs',
        items: [
          { type: 'docSidebar', sidebarId: 'docs', position: 'left', label: 'Docs' },
          { to: '/docs/api', label: 'API', position: 'left' },
          { to: '/blog', label: 'Tutorials', position: 'left' },
          { href: 'https://github.com/SecondNewtonLaw/rbxcs', label: 'GitHub', position: 'right' },
        ],
      },
      footer: {
        style: 'dark',
        copyright: 'Built from the rbxcs source with Docusaurus.',
      },
      prism: {
        theme: themes.github,
        darkTheme: themes.dracula,
        additionalLanguages: ['csharp', 'lua'],
      },
    }),
};

module.exports = config;
