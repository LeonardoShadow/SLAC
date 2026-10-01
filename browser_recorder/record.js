const { chromium } = require('playwright-core');
const path = require('path');
const fs = require('fs');

(async () => {
    console.log('=== INICIANDO GRABACIÓN DE VIDEO DE PRUEBAS EN VIVO ===');
    const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
    const videoDir = path.resolve(__dirname, 'videos');
    if (!fs.existsSync(videoDir)) {
        fs.mkdirSync(videoDir, { recursive: true });
    }

    const browser = await chromium.launch({
        executablePath: edgePath,
        headless: true
    });

    const context = await browser.newContext({
        viewport: { width: 1280, height: 720 },
        recordVideo: {
            dir: videoDir,
            size: { width: 1280, height: 720 }
        }
    });

    const page = await context.newPage();

    console.log('[1/4] Abriendo http://slac-asistencia.runasp.net/login ...');
    await page.goto('http://slac-asistencia.runasp.net/login', { waitUntil: 'networkidle', timeout: 35000 });
    await page.waitForTimeout(2500);

    console.log('[2/4] Probando pestañas de Docente, Estudiante y Admin...');
    const docenteTab = await page.$('text=Docente');
    if (docenteTab) {
        await docenteTab.click();
        await page.waitForTimeout(2000);
    }

    const inputDocente = await page.$('input');
    if (inputDocente) {
        await inputDocente.fill('87438');
        await page.waitForTimeout(2000);
    }

    const estudianteTab = await page.$('text=Estudiante');
    if (estudianteTab) {
        await estudianteTab.click();
        await page.waitForTimeout(2000);
    }

    console.log('[3/4] Navegando a pantalla de inicio SLAC...');
    await page.goto('http://slac-asistencia.runasp.net/', { waitUntil: 'networkidle', timeout: 35000 });
    await page.waitForTimeout(2500);

    console.log('[4/4] Probando endpoint de escaneo de estudiante /a/...');
    await page.goto('http://slac-asistencia.runasp.net/a/28cd8338-d087-4b34-9d32-22553a26784a', { waitUntil: 'networkidle', timeout: 35000 });
    await page.waitForTimeout(3000);

    console.log('Finalizando sesión y guardando archivo de video...');
    const videoObj = page.video();
    await page.close();
    await context.close();
    await browser.close();

    if (videoObj) {
        const videoPath = await videoObj.path();
        const finalPath = path.join(videoDir, 'grabacion_pruebas_slac.webm');
        if (fs.existsSync(videoPath)) {
            fs.copyFileSync(videoPath, finalPath);
            console.log('>>> VIDEO GUARDADO CON ÉXITO EN:', finalPath);
            console.log('>>> Tamaño del video:', (fs.statSync(finalPath).size / 1024).toFixed(1), 'KB');
        }
    }
})();
