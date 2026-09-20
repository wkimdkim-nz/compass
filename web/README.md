# Compass web

The Vite + React + TypeScript SPA. How to run it, and how it fits with the API, is in the
[root README](../README.md#running-it-locally); this file covers what is specific to the front end.

## Styling and the design tokens

Styling is Tailwind through its Vite plugin (`@tailwindcss/vite` in `vite.config.ts`), with no
component library. Every colour, size, face and rule the UI uses comes from **one file**,
[`src/tokens.css`](src/tokens.css):

- Its custom properties are the design's own, under the names the design note uses
  (`--paper`, `--ink-2`, `--pencil`, `--text-total`, `--s-4`, `--row`, `--measure`, …), copied
  from [`docs/design/mockups/tokens.css`](../docs/design/mockups/tokens.css). Light is the
  statement page; dark is its negative and follows the system colour scheme (with
  `data-theme="light"|"dark"` on `<html>` as an override); the type and column sizes step up at
  the tablet and desktop breakpoints. `src/tokens.test.ts` fails if the app file and the design
  file drift apart, and if any other file under `src/` names a colour of its own.
- The `@theme inline reference` block at the end maps those properties into Tailwind's
  namespaces, which is what turns them into utilities: `bg-paper`, `text-ink-2`,
  `border-hairline`, `text-total`, `max-w-measure`, `h-row`. Tailwind's own palette, type scale,
  radii and shadows are cleared there, so a utility for a colour outside the design does not
  exist. Spacing is the design's eight steps and its row rhythm (`p-4` reads `--s-4`, `min-h-row`
  reads `--row`), so a step the design lacks, such as `p-5`, has no utility either.

**To add a token:** declare the custom property in the `:root` block of `src/tokens.css`, give it
its dark value in both dark blocks and its wider values in the breakpoint blocks if it has them,
then map it in the `@theme` block under the Tailwind namespace that gives it the utility you want
(`--color-*` for colours, `--text-*` for font sizes, `--spacing-*` for lengths, `--container-*`
for widths). The Phase 0 auth pages keep their own 40rem column (`.phase0-column` in
`styles.css`) until the design covers them. If it is a design decision rather than an implementation detail, add it to the
design's file too, so the parity test keeps the two in step. A token derived from one that already
has dark values (`--scrim`, from `--ink`) follows the theme through it and is declared once.

[`src/styles.css`](src/styles.css) is the app stylesheet: it imports Tailwind and the tokens,
sets the page defaults (paper, ink, Archivo, tabular figures) in the base layer, and holds the
statement vocabulary the pages use (`.sheet`, `.masthead`, `.field`, `.entry`, `.btn`, `.note`,
`.statement-note`, and for the landing page and the calculator `.caption`, `.statement-table`,
`.fig`, `.basis`, the output rows' `.total` / `.double-rule` / `.changed`, `.footnote`, `.modal`),
ported from [`docs/design/mockups/statement.css`](../docs/design/mockups/statement.css), plus the `condensed` utility that narrows the output
totals to the design's width (`text-total font-semibold condensed`). Archivo is self-hosted from
`@fontsource-variable/archivo`, imported in `main.tsx`.

## The pages and the units line

`/` is the landing page (`features/landing`), public; `/projections/{ticker}` is the calculator
(`features/projection`), behind `ProtectedRoute`, which hands the address to sign in so that a
signed-out visitor lands on their projection afterwards. The calculator asks the ticker lookup and
the saved projection side by side: the lookup gives fresh base facts and the seeded defaults, the
projection (when there is one) the assumptions that go over them.

The form holds **text** in the UI's units, percents (8, not 0.08, to one decimal: ADR 0011) and
millions, negatives in parentheses. The current net margin is printed, not typed: no formula takes it. It crosses to the wire's decimals and raw values in exactly one module,
`features/projection/figures.ts`, used by `projectionForm.ts` both for the live preview (the
TypeScript calculator in `src/projection`) and for the body of a save. After a save the output rows
show the .NET calculator's figures from the response until the next keystroke. The disclaimer is
one component, `app/Disclaimer.tsx`, used beside the outputs and in every page's footer.
