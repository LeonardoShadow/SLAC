const { chromium } = require('playwright-core');
const path = require('path');
const fs = require('fs');

(async () => {
    console.log('=== TESTEANDO PÁGINA DE ESTUDIANTE CON EL NUEVO BOTÓN ===');
    const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
    const browser = await chromium.launch({ executablePath: edgePath, headless: true });
    const page = await browser.newPage();

    // Obtener HTML renderizado localmente desde el proyecto
    const dllPath = path.resolve(__dirname, '../SLAC/bin/Publish');
    console.log('Ruta Publish:', dllPath);

    await browser.close();
    console.log('Prueba lista.');
})();
