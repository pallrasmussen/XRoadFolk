import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const PAGES = ['/', '/Logs/Http'];

test.describe('Accessibility (axe) smoke', () => {
  for (const path of PAGES) {
    test(`axe on ${path}`, async ({ page, baseURL }) => {
      await page.goto(path);

      // Wait for main landmarks to render
      await page.waitForSelector('main');

      // Ignore known false positives (adjust selectors as needed)
      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        .disableRules([
          // Example: color-contrast on low-importance muted text
          // 'color-contrast'
        ])
        .analyze();

      if (results.violations.length) {
        const details = results.violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.map(n => n.target) }));
        console.log(JSON.stringify({ page: path, violations: details }, null, 2));
      }

      expect(results.violations, 'No axe-core violations expected').toEqual([]);
    });
  }
});
