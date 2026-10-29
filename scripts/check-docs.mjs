#!/usr/bin/env node
// Documentation gate: every relative Markdown link, and every image an HTML
// `src` or `srcset` names, resolves to a file in the repository, and no Markdown
// file contains an em dash (the house style uses a plain hyphen). Run from
// anywhere: `node scripts/check-docs.mjs`.
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const skipped = new Set([".git", "node_modules", ".next", "bin", "obj", "TestResults"]);
// Generated files are checked for links only: their wording belongs to the tool that writes them.
const generated = new Set(["frontend/AGENTS.md"]);
const linkPattern = /\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;
// `<img src>` and `<source srcset>`, as a README uses for light and dark screenshots.
const htmlSourcePattern = /\b(?:src|srcset)="([^"\s]+)"/g;
// Written as an escape so the character itself never appears in the repository.
const emDash = String.fromCharCode(0x2014);

function* markdownFiles(dir) {
  for (const name of readdirSync(dir)) {
    if (skipped.has(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* markdownFiles(path);
    else if (name.endsWith(".md")) yield path;
  }
}

const problems = [];
for (const file of markdownFiles(root)) {
  const text = readFileSync(file, "utf8");
  const where = relative(root, file).replaceAll("\\", "/");
  // Links inside fenced code blocks are examples, not links.
  const prose = text.replace(/```[\s\S]*?```/g, "");
  for (const [, target] of [...prose.matchAll(linkPattern), ...prose.matchAll(htmlSourcePattern)]) {
    if (/^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith("#")) continue;
    const path = decodeURIComponent(target.split("#")[0]);
    if (!existsSync(resolve(dirname(file), path))) problems.push(`${where}: broken link ${target}`);
  }
  if (generated.has(where)) continue;
  text.split("\n").forEach((line, index) => {
    if (line.includes(emDash)) problems.push(`${where}:${index + 1}: em dash`);
  });
}

if (problems.length > 0) {
  console.error(problems.join("\n"));
  process.exit(1);
}
console.log("docs: all relative links and images resolve, no em dashes");
