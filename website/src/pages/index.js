import { Redirect } from '@docusaurus/router';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';

// The site is docs-first; send the root at / straight to the introduction.
export default function Home() {
  const { siteConfig } = useDocusaurusContext();
  return <Redirect to={`${siteConfig.baseUrl}docs`} />;
}
