import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

// The design note (docs/design/phase1-look.md) declares mockups/tokens.css the source of truth
// and asks this ticket to read its values rather than retype them. These tests keep the app's
// token file and the design's token file from drifting apart, and keep every colour in the UI
// coming from that one file.

const here = dirname(fileURLToPath(import.meta.url));
const read = (path: string) => readFileSync(join(here, path), "utf8");

/** `--name: value` declarations in source order, comments and `@theme` blocks removed. */
function declarations(css: string): Map<string, string[]> {
  const plain = css.replace(/\/\*[\s\S]*?\*\//g, "").replace(/@theme[^{]*\{[^}]*\}/g, "");
  const found = new Map<string, string[]>();
  for (const [, name, value] of plain.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
    found.set(name, [...(found.get(name) ?? []), value.trim()]);
  }
  return found;
}

describe("the token file", () => {
  const design = declarations(read("../../docs/design/mockups/tokens.css"));
  const app = declarations(read("./tokens.css"));

  it("carries every token the design note declares, with the same values wherever it is declared", () => {
    // `--font` is the one exception: the design loads Archivo from Google Fonts, the app
    // self-hosts it, and the self-hosted face registers under a different family name.
    const differences = [...design]
      .filter(([name]) => name !== "--font")
      .filter(([name, values]) => JSON.stringify(app.get(name)) !== JSON.stringify(values))
      .map(([name, values]) => `${name}: design ${JSON.stringify(values)}, app ${JSON.stringify(app.get(name))}`);
    expect(differences).toEqual([]);
  });

  it("names the self-hosted Archivo face", () => {
    expect(app.get("--font")?.[0]).toMatch(/^"Archivo Variable"/);
  });
});

describe("the rest of the UI", () => {
  const sources = (dir: string): string[] =>
    readdirSync(dir).flatMap((entry) => {
      const path = join(dir, entry);
      if (statSync(path).isDirectory()) return sources(path);
      return /\.(css|tsx?)$/.test(entry) && !/\.test\.tsx?$/.test(entry) && entry !== "tokens.css" ? [path] : [];
    });

  // Every way CSS can spell a colour: hex, the colour functions, and the named colours, the
  // last matched only inside a declaration's value so prose in a comment cannot trip it. In
  // TSX the same applies to Tailwind arbitrary values on the colour utilities (`text-[red]`).
  const hex = /#[0-9a-f]{3,8}\b/gi;
  const functions = /\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color|color-mix|light-dark)\(/gi;
  const named =
    /\b(?:aqua|black|blue|brown|coral|crimson|cyan|fuchsia|gold|gray|grey|green|indigo|ivory|khaki|lime|magenta|maroon|navy|olive|orange|orchid|pink|plum|purple|red|salmon|silver|tan|teal|tomato|violet|white|yellow|(?:dark|light|medium|pale|deep|dim)[a-z]+)\b/gi;
  const cssValue = /:\s*([^;{}]+)/g;
  const arbitraryColourClass =
    /\b(?:text|bg|border|outline|ring|fill|stroke|caret|accent|decoration|shadow|from|via|to)-\[([^\]]+)\]/g;

  it("hard-codes no colour outside the token file", () => {
    const offenders = sources(here).flatMap((path) => {
      const text = readFileSync(path, "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
      const hits = [...(text.match(hex) ?? []), ...(text.match(functions) ?? [])];
      const valueHolders = path.endsWith(".css")
        ? [...text.matchAll(cssValue)].map((m) => m[1])
        : [...text.matchAll(arbitraryColourClass)].map((m) => m[1]);
      for (const value of valueHolders) hits.push(...(value.match(named) ?? []));
      return hits.length ? [`${path.slice(here.length)}: ${hits.join(", ")}`] : [];
    });
    expect(offenders).toEqual([]);
  });
});
