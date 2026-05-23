#!/usr/bin/env node
/*
 * build-html.js - render the project's README.md into the README-CONTENT
 * marker block of index.htm. Stays at marker boundaries; never touches
 * anything outside <!-- BEGIN README-CONTENT --> ... <!-- END README-CONTENT -->.
 *
 * Idempotent: re-running with no README changes produces a byte-stable
 * index.htm modulo any other sync.
 *
 * Usage:
 *   node scripts/cli/build-html.js
 *   node scripts/cli/build-html.js path/to/SOMETHING.md
 */

'use strict';

const fs   = require('fs');
const path = require('path');
const { marked } = require('marked');
const hljs = require('highlight.js');

const repoRoot   = path.resolve(__dirname, '..', '..');
const argv       = process.argv.slice(2);
const sourceArg  = argv.find(a => !a.startsWith('--'));

const BEGIN = '<!-- BEGIN README-CONTENT -->';
const END   = '<!-- END README-CONTENT -->';

function slugify(t) {
    return String(t).toLowerCase().replace(/[^a-z0-9\s-]/g, '').trim().replace(/\s+/g, '-').replace(/-+/g, '-');
}

marked.setOptions({
    gfm: true,
    breaks: false,
    headerIds: true,
    mangle: false,
    highlight(code, lang) {
        try {
            if (lang && hljs.getLanguage(lang)) {
                return hljs.highlight(code, { language: lang }).value;
            }
        } catch (_) {}
        return hljs.highlightAuto(code).value;
    },
});

const renderer = new marked.Renderer();
renderer.heading = function (text, level, raw) {
    const id = slugify(raw);
    const anchor = level >= 2 && level <= 4
        ? ` <a class="heading-anchor" href="#${id}" aria-label="link to this section">#</a>`
        : '';
    return `<h${level} id="${id}">${text}${anchor}</h${level}>\n`;
};

function pickSource() {
    if (sourceArg) return path.resolve(sourceArg);
    const p = path.join(repoRoot, 'README.md');
    if (!fs.existsSync(p)) throw new Error('README.md not found at ' + p);
    return p;
}

function spliceMarker(indexText, begin, end, block) {
    const startIdx = indexText.indexOf(begin);
    if (startIdx < 0) throw new Error("Marker '" + begin + "' not found in index.htm. Insert the marker pair before building.");
    const endIdx = indexText.indexOf(end, startIdx);
    if (endIdx < 0) throw new Error("End marker '" + end + "' not found after begin marker.");
    const after = endIdx + end.length;
    const combined = begin + '\n' + block + '\n' + end;
    return indexText.slice(0, startIdx) + combined + indexText.slice(after);
}

function main() {
    const indexPath = path.join(repoRoot, 'index.htm');
    if (!fs.existsSync(indexPath)) throw new Error('index.htm not found at ' + indexPath);

    const mdPath = pickSource();
    const md = fs.readFileSync(mdPath, 'utf8');
    const html = marked.parse(md, { renderer });

    const block = '<!-- Rendered from ' + path.basename(mdPath) + ' by scripts/cli/build-html.js. Do not edit by hand. -->\n' + html.trim();

    const original = fs.readFileSync(indexPath, 'utf8');
    const updated  = spliceMarker(original, BEGIN, END, block);

    if (updated === original) {
        process.stdout.write('build-html.js: no changes (README already rendered).\n');
        return;
    }
    fs.writeFileSync(indexPath, updated, 'utf8');
    const kb = (Buffer.byteLength(html, 'utf8') / 1024).toFixed(1);
    process.stdout.write('build-html.js: rendered ' + path.basename(mdPath) + ' (' + kb + ' KB) -> index.htm\n');
}

try { main(); }
catch (e) { process.stderr.write('build-html.js: ' + e.message + '\n'); process.exit(1); }
