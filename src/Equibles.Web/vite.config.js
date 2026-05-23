import { defineConfig } from 'vite'
import { resolve } from 'path'

export default defineConfig({
    build: {
        outDir: 'wwwroot/dist',
        emptyOutDir: true,
        rollupOptions: {
            input: {
                bundle: resolve(__dirname, 'src/index.js'),
                chart: resolve(__dirname, 'src/chart.js'),
            },
            output: {
                entryFileNames: '[name].js',
                intro: '(function() {',
                outro: '})();',
                assetFileNames: (assetInfo) => {
                    if (assetInfo.name?.endsWith('.css')) {
                        return 'main.css'
                    }
                    return '[name][extname]'
                }
            }
        }
    }
})
