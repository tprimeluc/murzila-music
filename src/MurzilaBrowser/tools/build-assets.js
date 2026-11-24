// Собирает манифест внутренних страниц Murzila Browser.
// Запуск: npm run build  (из папки tools)
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const resources = path.resolve(__dirname, '..', 'MurzilaBrowser.App', 'Resources');
const outDir = path.resolve(__dirname, '..', 'MurzilaBrowser.App', 'Assets');
const mime = {
  '.html': 'text/html',
  '.css': 'text/css',
  '.js': 'application/javascript',
  '.svg': 'image/svg+xml',
  '.png': 'image/png'
};

const pages = fs.readdirSync(resources)
  .filter(name => fs.statSync(path.join(resources, name)).isFile())
  .map(name => {
    const bytes = fs.readFileSync(path.join(resources, name));
    return {
      file: name,
      url: 'murzila://' + (name === 'style.css' ? 'res/style.css' : name.replace(/\.html$/, '')),
      mime: mime[path.extname(name).toLowerCase()] || 'text/plain',
      bytes: bytes.length,
      sha256: crypto.createHash('sha256').update(bytes).digest('hex')
    };
  });

fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(
  path.join(outDir, 'pages.manifest.json'),
  JSON.stringify({ version: 1, generatedBy: 'node', pages }, null, 2) + '\n'
);
console.log(`Manifest: ${pages.length} internal pages -> ${path.join(outDir, 'pages.manifest.json')}`);
