import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The build lands in ../ui, which build.bat copies next to BetterGlorious.exe.
export default defineConfig({
  base: './',
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../ui',
    emptyOutDir: true,
    assetsInlineLimit: 0,
  },
})
