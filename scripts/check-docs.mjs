#!/usr/bin/env node
// Documentation gate: every relative Markdown link resolves to a file in the
// repository, and no Markdown file contains an em dash (the house style uses a
// plain hyphen). Run from anywhere: `node scripts/check-docs.mjs`.
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const skipped = new Set([".git", "node_modules", ".next", "bin", "obj", "TestResults"]);
const linkPattern = /\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;
const emDash = "—";

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
  for (const [, target] of prose.matchAll(linkPattern)) {
    if (/^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith("#")) continue;
    const path = decodeURIComponent(target.split("#")[0]);
    if (!existsSync(resolve(dirname(file), path))) problems.push(`${where}: broken link ${target}`);
  }
  text.split("\n").forEach((line, index) => {
    if (line.includes(emDash)) problems.push(`${where}:${index + 1}: em dash`);
  });
}

if (problems.length > 0) {
  console.error(problems.join("\n"));
  process.exit(1);
}
console.log("docs: all relative links resolve, no em dashes");
