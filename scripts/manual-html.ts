// Builds the offline HTML of a manual from its Markdown.
//
// Usage: deno run --allow-read --allow-write scripts/manual-html.ts <manual dir> <out dir>
//   <manual dir>  has README.md (the manual) and images/ (PNG files it links to)
//   <out dir>     gets index.html and a copy of images/
//
// The Markdown is the source; GitHub shows it as it is. The HTML has the same text with
// heading ids named as GitHub names them, a table of contents from the h2 and h3 headings,
// and its own CSS (no files or network besides images/). Raw HTML in the Markdown is not
// rendered, so GitHub and the HTML show the same thing. The same input gives the same output.
// Exits 1 when a link to "#..." does not match a heading, 2 on usage or file errors.
//
// The same script is in kmnk/VRChat_FaceTrackingGenerator and kmnk/ft-measure; change both.
import MarkdownIt from "npm:markdown-it@14.1.0";

const [manualDir, outDir] = Deno.args;
if (!manualDir || !outDir) {
  console.error("usage: manual-html.ts <manual dir> <out dir>");
  Deno.exit(2);
}

let source: string;
try {
  source = await Deno.readTextFile(`${manualDir}/README.md`);
} catch (e) {
  console.error(`manual-html: cannot read ${manualDir}/README.md: ${e}`);
  Deno.exit(2);
}

// GitHub's heading ids: lower case, drop what is not a letter, a number, a space, "_" or "-",
// spaces to "-", and "-1", "-2" for the second and later headings of the same id.
function slug(text: string): string {
  return text.trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\s_-]/gu, "").replace(/\s/g, "-");
}

const md = new MarkdownIt({ html: false, linkify: false, typographer: false });
const headings: { level: number; id: string; text: string }[] = [];
md.core.ruler.push("heading_ids", (state) => {
  const used = new Map<string, number>();
  const tokens = state.tokens;
  for (let i = 0; i < tokens.length; i++) {
    if (tokens[i].type !== "heading_open") continue;
    const text = tokens[i + 1].children?.filter((t) => t.type === "text" || t.type === "code_inline")
      .map((t) => t.content).join("") ?? tokens[i + 1].content;
    const base = slug(text);
    const n = used.get(base) ?? 0;
    used.set(base, n + 1);
    const id = n === 0 ? base : `${base}-${n}`;
    tokens[i].attrSet("id", id);
    headings.push({ level: Number(tokens[i].tag.slice(1)), id, text });
  }
});

const body = md.render(source);
const ids = new Set(headings.map((h) => h.id));

// Links inside the page must match a heading.
const broken: string[] = [];
for (const m of body.matchAll(/href="#([^"]*)"/g)) {
  const id = decodeURIComponent(m[1]);
  if (!ids.has(id)) broken.push(`#${id}`);
}
if (broken.length > 0) {
  console.error(`manual-html: links that match no heading: ${[...new Set(broken)].join(", ")}`);
  Deno.exit(1);
}

const escape = (s: string) =>
  s.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
const title = headings.find((h) => h.level === 1)?.text ?? "Manual";
const toc = headings.filter((h) => h.level === 2 || h.level === 3)
  .map((h) => `<li class="toc-${h.level}"><a href="#${encodeURIComponent(h.id)}">${escape(h.text)}</a></li>`)
  .join("\n");

const css = `
:root { color-scheme: light dark; --fg: #1f2328; --bg: #ffffff; --muted: #59636e; --line: #d1d9e0; --code: #f6f8fa; --link: #0969da; }
@media (prefers-color-scheme: dark) { :root { --fg: #e6edf3; --bg: #0d1117; --muted: #9198a1; --line: #3d444d; --code: #151b23; --link: #4493f8; } }
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--fg); font: 16px/1.8 system-ui, -apple-system, "Segoe UI", "Hiragino Sans", "Yu Gothic UI", "Meiryo", sans-serif; }
.page { max-width: 880px; margin: 0 auto; padding: 24px 16px 64px; }
a { color: var(--link); }
h1, h2, h3, h4 { line-height: 1.4; margin: 2em 0 0.6em; }
h1 { margin-top: 0.5em; font-size: 1.9em; }
h2 { font-size: 1.5em; padding-bottom: 0.3em; border-bottom: 1px solid var(--line); }
h3 { font-size: 1.2em; }
nav.toc { border: 1px solid var(--line); border-radius: 8px; padding: 12px 20px; margin: 24px 0; }
nav.toc ul { margin: 0; padding-left: 1.2em; }
nav.toc .toc-3 { margin-left: 1.2em; font-size: 0.95em; }
img { max-width: 100%; height: auto; border: 1px solid var(--line); border-radius: 4px; }
table { border-collapse: collapse; display: block; overflow-x: auto; margin: 1em 0; }
th, td { border: 1px solid var(--line); padding: 6px 12px; text-align: left; vertical-align: top; }
th { background: var(--code); }
code { background: var(--code); border-radius: 4px; padding: 0.1em 0.35em; font-size: 0.9em; }
pre { background: var(--code); border-radius: 6px; padding: 12px 16px; overflow-x: auto; }
pre code { padding: 0; }
blockquote { margin: 1em 0; padding: 0 1em; color: var(--muted); border-left: 4px solid var(--line); }
`;

const html = `<!doctype html>
<html lang="ja">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escape(title)}</title>
<style>${css}</style>
</head>
<body>
<div class="page">
<nav class="toc" aria-label="目次">
<strong>目次</strong>
<ul>
${toc}
</ul>
</nav>
<main>
${body}</main>
</div>
</body>
</html>
`;

await Deno.mkdir(outDir, { recursive: true });
await Deno.writeTextFile(`${outDir}/index.html`, html);

// Copy images/ (sorted, so the copy does not depend on the directory order).
async function copyDir(from: string, to: string) {
  const entries: Deno.DirEntry[] = [];
  try {
    for await (const e of Deno.readDir(from)) entries.push(e);
  } catch (e) {
    if (e instanceof Deno.errors.NotFound) return;
    throw e;
  }
  entries.sort((a, b) => a.name < b.name ? -1 : 1);
  await Deno.mkdir(to, { recursive: true });
  for (const e of entries) {
    if (e.name.startsWith(".")) continue;
    if (e.isDirectory) await copyDir(`${from}/${e.name}`, `${to}/${e.name}`);
    else if (e.isFile) await Deno.copyFile(`${from}/${e.name}`, `${to}/${e.name}`);
  }
}
await copyDir(`${manualDir}/images`, `${outDir}/images`);
console.log(`manual-html: ${outDir}/index.html (${headings.length} headings)`);
