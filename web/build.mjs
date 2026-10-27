import * as esbuild from 'esbuild';

const watch = process.argv.includes('--watch');

const options = {
  entryPoints: {
    homepage: 'src/main.tsx',
    admin: 'src/admin/main.tsx',
  },
  bundle: true,
  minify: !watch,
  sourcemap: false,
  format: 'iife',
  target: 'es2020',
  outdir: 'dist',
  jsx: 'automatic',
  jsxImportSource: 'preact',
  legalComments: 'none',
  logLevel: 'info',
  loader: { '.svg': 'text' },
  define: { __HP_VERSION__: JSON.stringify(process.env.VERSION || 'dev') },
};

if (watch) {
  const ctx = await esbuild.context(options);
  await ctx.watch();
} else {
  await esbuild.build(options);
}
