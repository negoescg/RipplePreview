import { defineConfig } from 'vite';
import { version } from './package.json';

export default defineConfig({
  define: {
    __RIPPLE_VERSION__: JSON.stringify(version),
  },
  build: {
    lib: {
      entry: 'src/ripple-preview.ts',
      formats: ['es'],
      // Versioned file name: extension modules are cached by URL, so every release
      // must ship under a fresh URL or browsers keep running the previous build.
      fileName: `ripple-preview-${version}`,
    },
    outDir: '../wwwroot/App_Plugins/RipplePreview',
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      external: [/^@umbraco/],
    },
  },
});
