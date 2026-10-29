#!/usr/bin/env node
// WCAG 2.x contrast and sRGB gamut check for the dashboard's design tokens.
//
//   node scripts/check-contrast.mjs
//
// The tokens are read from frontend/src/app/globals.css, so the stylesheet stays the only source of
// truth; this file holds only the policy (which pairs must meet which ratio). Exits non-zero when a
// token is not light-dark(oklch(...), oklch(...)), leaves the sRGB gamut, or a pair misses its target.
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const css = readFileSync(resolve(root, "frontend/src/app/globals.css"), "utf8");

// [foreground, background, minimum ratio, purpose]
const PAIRS = [
  ["fg", "bg", 7, "body text on the page"],
  ["fg", "surface", 7, "body text on cards"],
  ["fg", "surface-2", 7, "body text on raised rows"],
  ["muted", "bg", 4.5, "secondary text on the page"],
  ["muted", "surface", 4.5, "secondary text on cards"],
  ["muted", "surface-2", 4.5, "secondary text on raised rows"],
  ["primary-fg", "primary", 4.5, "primary button label"],
  ["primary-fg", "primary-hover", 4.5, "primary button label, hovered"],
  ["primary", "bg", 3, "focus ring and links on the page"],
  ["primary", "surface", 4.5, "links on cards"],
  ["primary", "primary-soft", 4.5, "captured pill"],
  ["border-strong", "surface", 3, "input borders"],
  ["success", "success-soft", 4.5, "settled pill"],
  ["warning", "warning-soft", 4.5, "pending pill"],
  ["danger", "danger-soft", 4.5, "failed pill"],
  ["info", "info-soft", 4.5, "authorized pill"],
  ["muted", "surface", 4.5, "refunded pill and the neutral tone"],
  ["danger", "surface", 4.5, "error text on cards"],
];

// The status pills, which sit side by side: within a scheme their ratios may differ by at most this
// factor, so equal statuses look equally weighted and none reads louder than another by accident.
const PILLS = [
  ["primary", "primary-soft"],
  ["success", "success-soft"],
  ["warning", "warning-soft"],
  ["danger", "danger-soft"],
  ["info", "info-soft"],
  ["muted", "surface"],
];
const PILL_SPREAD = 1.25;

const tokenPattern = /^\s*--([a-z0-9-]+):\s*light-dark\(\s*(oklch\([^)]*\))\s*,\s*(oklch\([^)]*\))\s*\);/gm;
const tokens = new Map();
for (const [, name, light, dark] of css.matchAll(tokenPattern)) {
  tokens.set(name, { light: parseOklch(light), dark: parseOklch(dark) });
}

function parseOklch(text) {
  const [l, c, h] = text.slice(6, -1).trim().split(/\s+/).map(Number);
  return { l, c, h };
}

// OKLCH -> linear sRGB (Björn Ottosson's OKLab matrices).
function toLinearSrgb({ l, c, h }) {
  const a = c * Math.cos((h * Math.PI) / 180);
  const b = c * Math.sin((h * Math.PI) / 180);
  const l_ = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m_ = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s_ = (l - 0.0894841775 * a - 1.291485548 * b) ** 3;
  return [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ];
}

const inGamut = (rgb) => rgb.every((channel) => channel >= -0.0005 && channel <= 1.0005);
const luminance = ([r, g, b]) =>
  0.2126 * clamp(r) + 0.7152 * clamp(g) + 0.0722 * clamp(b);
const clamp = (value) => Math.min(1, Math.max(0, value));
const ratio = (x, y) => {
  const [hi, lo] = [luminance(x), luminance(y)].sort((p, q) => q - p);
  return (hi + 0.05) / (lo + 0.05);
};

const problems = [];
const lines = [];
for (const scheme of ["light", "dark"]) {
  for (const [name, value] of tokens) {
    if (!inGamut(toLinearSrgb(value[scheme]))) problems.push(`${scheme} --${name} is outside sRGB`);
  }
  for (const [fg, bg, minimum, purpose] of PAIRS) {
    if (!tokens.has(fg) || !tokens.has(bg)) {
      problems.push(`unknown token in pair ${fg} on ${bg}`);
      continue;
    }
    const value = ratio(toLinearSrgb(tokens.get(fg)[scheme]), toLinearSrgb(tokens.get(bg)[scheme]));
    const verdict = value >= minimum ? "ok  " : "FAIL";
    lines.push(`${verdict} ${scheme.padEnd(5)} ${`${fg} on ${bg}`.padEnd(30)} ${value.toFixed(2).padStart(5)} >= ${minimum}  ${purpose}`);
    if (value < minimum) problems.push(`${scheme} ${fg} on ${bg}: ${value.toFixed(2)} < ${minimum}`);
  }
  const pills = PILLS.filter(([fg, bg]) => tokens.has(fg) && tokens.has(bg)).map(([fg, bg]) =>
    ratio(toLinearSrgb(tokens.get(fg)[scheme]), toLinearSrgb(tokens.get(bg)[scheme])),
  );
  const spread = Math.max(...pills) / Math.min(...pills);
  const verdict = spread <= PILL_SPREAD ? "ok  " : "FAIL";
  lines.push(`${verdict} ${scheme.padEnd(5)} ${"pill balance".padEnd(30)} ${spread.toFixed(2).padStart(5)} <= ${PILL_SPREAD}  strongest pill over the weakest`);
  if (spread > PILL_SPREAD) problems.push(`${scheme} pills spread ${spread.toFixed(2)} > ${PILL_SPREAD}`);
}

if (tokens.size === 0) problems.push("no light-dark(oklch(...), oklch(...)) tokens found in globals.css");
console.log(lines.join("\n"));
if (problems.length > 0) {
  console.error(`\n${problems.join("\n")}`);
  process.exit(1);
}
console.log(`\ncontrast: ${tokens.size} tokens in gamut, ${PAIRS.length * 2} pairs meet their targets, pills balanced`);
