// Build/minify JS & CSS using esbuild
// Outputs to wwwroot/dist with content-hash filenames; Razor uses asp-append-version for cache busting
import { build } from 'esbuild';
import { promises as fs } from 'fs';
import path from 'path';

const root = path.resolve(process.cwd(), 'wwwroot');
const outDir = path.join(root, 'dist');

async function rimraf(dir){ try { await fs.rm(dir, { recursive:true, force:true }); } catch {} }
async function ensure(dir){ await fs.mkdir(dir, { recursive:true }); }

// Entry points (add more as needed)
const jsEntries = [
  'js/site.js',
  'js/antiforgery.js',
  'js/index-clear-details.js',
  'js/index-form-state.js',
  'js/date-mask.js',
  'js/gpiv-viewer.js',
  'js/gpiv-helpers.js',
  'js/logs-viewer.js',
  'js/validation-dob.js',
  'js/validation-name.js',
  'js/validation-ssn-or-name-dob.js'
];

const cssEntries = [
  'css/site.css',
  'css/logs.css',
  'css/person-details.css'
];

async function buildAll(){
  await rimraf(outDir); await ensure(outDir);

  // JS bundle (code splitting) – produces hashed filenames
  await build({
    entryPoints: jsEntries.map(f => path.join(root, f)),
    outdir: outDir,
    bundle: true,
    minify: true,
    sourcemap: false,
    splitting: true,
    format: 'esm',
    target: 'es2020',
    logLevel: 'info'
  });

  // CSS: esbuild can process but keep separate (no inlining) for cache bust
  await build({
    entryPoints: cssEntries.map(f => path.join(root, f)),
    outdir: outDir,
    bundle: true,
    minify: true,
    sourcemap: false,
    loader: { '.css':'css' },
    logLevel: 'info'
  });

  console.log('Assets built to dist/');
}

buildAll().catch(e => { console.error(e); process.exit(1); });
