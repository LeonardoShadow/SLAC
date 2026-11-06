/**
 * slac-gps-alpha-challenge.js
 * Empirical Challenge Harness for SLAC Client GPS Alpha Engine
 *
 * Verifies:
 * 1. Cold warmup discard (first 3 samples dropped)
 * 2. Rayleigh 2-sigma stochastic gate (anomalous speed jump rejection)
 * 3. Desktop PC fallback detection (acc > 100m)
 * 4. BLUE centroid numerical stability (zero NaN, extreme coordinates, identical points)
 */

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

console.log("=== INITIATING CLIENT GPS ALPHA EMPIRICAL CHALLENGE ===");

// 1. Load slac-gps-alpha.js in sandbox
const scriptPath = path.resolve(__dirname, '../SLAC/wwwroot/js/slac-gps-alpha.js');
const scriptCode = fs.readFileSync(scriptPath, 'utf8');

function createSandbox(mockGeolocation) {
    const sandbox = {
        console: console,
        Date: Date,
        Math: Math,
        Array: Array,
        Object: Object,
        Number: Number,
        setTimeout: setTimeout,
        clearTimeout: clearTimeout,
        navigator: {
            geolocation: mockGeolocation
        },
        window: {
            isSecureContext: true,
            location: { hostname: 'localhost' }
        }
    };
    vm.createContext(sandbox);
    vm.runInContext(scriptCode, sandbox);
    sandbox.slacGpsAlpha = sandbox.window.slacGpsAlpha;
    return sandbox;
}

let testsPassed = 0;
let testsFailed = 0;

function runTest(testName, fn) {
    try {
        fn();
        console.log(`[PASS] ${testName}`);
        testsPassed++;
    } catch (err) {
        console.error(`[FAIL] ${testName}: ${err.message}`);
        if (err.stack) console.error(err.stack);
        testsFailed++;
    }
}

async function runAsyncTest(testName, fn) {
    try {
        await fn();
        console.log(`[PASS] ${testName}`);
        testsPassed++;
    } catch (err) {
        console.error(`[FAIL] ${testName}: ${err.message}`);
        if (err.stack) console.error(err.stack);
        testsFailed++;
    }
}

// ---------------------------------------------------------
// TEST 1: Cold warmup buffer discard (3 samples)
// ---------------------------------------------------------
async function testColdWarmupDiscard() {
    let watchSuccessCb = null;
    let watchId = 101;

    const mockGeo = {
        watchPosition: (success, error, opts) => {
            watchSuccessCb = success;
            return watchId;
        },
        clearWatch: (id) => {}
    };

    const sb = createSandbox(mockGeo);
    const alpha = sb.slacGpsAlpha;

    const calPromise = alpha.startDocenteCalibration(null, 3.0, 8, 5000);

    // Feed 3 warmup samples with decreasing accuracy
    // Sample 1 (acc=45m)
    watchSuccessCb({
        coords: { latitude: -17.839209, longitude: -63.194244, accuracy: 45.0, speed: 0 },
        timestamp: 1000
    });
    // Sample 2 (acc=15m)
    watchSuccessCb({
        coords: { latitude: -17.839210, longitude: -63.194245, accuracy: 15.0, speed: 0 },
        timestamp: 2000
    });
    // Sample 3 (acc=8m)
    watchSuccessCb({
        coords: { latitude: -17.839211, longitude: -63.194246, accuracy: 8.0, speed: 0 },
        timestamp: 3000
    });

    // Feed 8 valid post-warmup samples to complete calibration
    for (let i = 1; i <= 8; i++) {
        watchSuccessCb({
            coords: {
                latitude: -17.839209 + (i * 0.000001),
                longitude: -63.194244 + (i * 0.000001),
                accuracy: 3.0,
                speed: 0
            },
            timestamp: 3000 + (i * 1000)
        });
    }

    const result = await calPromise;

    assert(result, "Calibration result must not be null");
    assert.strictEqual(result.isDesktopFallback, false, "Must not trigger desktop fallback");
    assert.strictEqual(result.rawSamples.length, 8, `Expected exactly 8 post-warmup samples, got ${result.rawSamples.length}`);

    // Verify first ingested sample is post-warmup (timestamp 4000)
    assert.strictEqual(result.rawSamples[0].timestampMs, 4000, "First retained sample must be post-warmup sample 4");
    assert.strictEqual(result.rawSamples[0].acc, 3.0, "Accuracy of first sample must match sample 4");

    // Verify none of the warmup samples (acc 45, 15, 8) exist in rawSamples
    const accs = result.rawSamples.map(s => s.acc);
    assert(!accs.includes(45.0), "Sample with acc=45m must be discarded");
    assert(!accs.includes(15.0), "Sample with acc=15m must be discarded");
    assert(!accs.includes(8.0), "Sample with acc=8m must be discarded");
}

// ---------------------------------------------------------
// TEST 2: Rayleigh 2-sigma stochastic gate (anomalous speed jump)
// ---------------------------------------------------------
async function testRayleighStochasticGate() {
    let watchSuccessCb = null;

    const mockGeo = {
        watchPosition: (success, error, opts) => {
            watchSuccessCb = success;
            return 202;
        },
        clearWatch: (id) => {}
    };

    const sb = createSandbox(mockGeo);
    const alpha = sb.slacGpsAlpha;

    const calPromise = alpha.startDocenteCalibration(null, 3.0, 5, 5000);

    // 3 warmup samples
    for (let w = 1; w <= 3; w++) {
        watchSuccessCb({
            coords: { latitude: -17.839209, longitude: -63.194244, accuracy: 4.0 },
            timestamp: w * 1000
        });
    }

    // First valid sample (t = 4000, acc = 1.0)
    watchSuccessCb({
        coords: { latitude: -17.839209, longitude: -63.194244, accuracy: 1.0 },
        timestamp: 4000
    });

    // Anomalous speed jump 1: 50m jump in 1 second (speed = 50 m/s > 4.5 m/s)
    // Threshold with dt=1s, acc=1.0: (2.5*1.0) + 2.0*sqrt(1.0^2 + 1.0^2) = 5.33m
    // At Santa Cruz latitude: 0.00045 deg lat ~ 49.8 meters > 5.33m threshold
    watchSuccessCb({
        coords: { latitude: -17.839209 + 0.00045, longitude: -63.194244, accuracy: 1.0 },
        timestamp: 5000
    });

    // Valid stationary sample (t = 6000, dt=2s from sample 1, distance ~ 0.88m <= threshold 7.83m)
    watchSuccessCb({
        coords: { latitude: -17.839209 + 0.000008, longitude: -63.194244, accuracy: 1.0 },
        timestamp: 6000
    });

    // Anomalous speed jump 2: 8.8m jump in 1 second (speed = 8.8 m/s > 4.5 m/s)
    // Threshold with dt=1s, acc=1.0: 5.33m
    // 0.000080 deg lat ~ 8.85 meters > 5.33m threshold
    watchSuccessCb({
        coords: { latitude: -17.839209 + 0.000088, longitude: -63.194244, accuracy: 1.0 },
        timestamp: 7000
    });

    // Valid stationary sample (t = 8000, dt=2s from sample 2, distance ~ 0.22m)
    watchSuccessCb({
        coords: { latitude: -17.839209 + 0.000010, longitude: -63.194244, accuracy: 1.0 },
        timestamp: 8000
    });

    // Complete calibration with remaining valid samples
    for (let k = 1; k <= 2; k++) {
        watchSuccessCb({
            coords: { latitude: -17.839209 + (k * 0.000005), longitude: -63.194244, accuracy: 2.0 },
            timestamp: 8000 + (k * 1000)
        });
    }

    const result = await calPromise;

    // Verify anomalous jumps were purged
    const lats = result.rawSamples.map(s => s.lat);
    assert(!lats.some(lat => Math.abs(lat - (-17.839209 + 0.00045)) < 1e-6), "50m anomalous jump must be rejected by Rayleigh gate");
    assert(!lats.some(lat => Math.abs(lat - (-17.839209 + 0.000088)) < 1e-6), "8.8m anomalous jump must be rejected by Rayleigh gate");
    assert.strictEqual(result.rawSamples.length, 5, `Expected 5 clean samples, got ${result.rawSamples.length}`);
}

// ---------------------------------------------------------
// TEST 3: Desktop PC detection (accuracy > 100m)
// ---------------------------------------------------------
async function testDesktopPcDetection() {
    let watchSuccessCb = null;

    const mockGeo = {
        watchPosition: (success, error, opts) => {
            watchSuccessCb = success;
            return 303;
        },
        clearWatch: (id) => {}
    };

    const sb = createSandbox(mockGeo);
    const alpha = sb.slacGpsAlpha;

    const calPromise = alpha.startDocenteCalibration(null, 3.0, 5, 5000);

    // Initial sample reported by wired desktop / GeoIP (acc = 3500m > 100m)
    watchSuccessCb({
        coords: { latitude: -17.839209, longitude: -63.194244, accuracy: 3500.0 },
        timestamp: 1000
    });

    const result = await calPromise;
    assert.strictEqual(result.isDesktopFallback, true, "Must flag desktop fallback");
    assert.strictEqual(result.accuracy, 3500.0, "Must record desktop accuracy");
    assert.strictEqual(result.samplesCount, 0, "Must abort calibration immediately without accumulating samples");
}

// ---------------------------------------------------------
// TEST 4: BLUE centroid numerical stability & zero NaN
// ---------------------------------------------------------
function testBlueCentroidStability() {
    const sb = createSandbox({});
    const alpha = sb.slacGpsAlpha;

    // 4.1 Identical coordinates
    const identicalSamples = Array.from({ length: 15 }, () => ({
        lat: -17.83920987687,
        lon: -63.19424410558,
        acc: 3.5
    }));

    const blueIdentical = alpha.calculateBlue(identicalSamples);
    assert(!isNaN(blueIdentical.lat), "BLUE lat must not be NaN on identical points");
    assert(!isNaN(blueIdentical.lon), "BLUE lon must not be NaN on identical points");
    assert(!isNaN(blueIdentical.acc), "BLUE acc must not be NaN on identical points");
    assert(!isNaN(blueIdentical.variance), "BLUE variance must not be NaN on identical points");
    assert(Math.abs(blueIdentical.lat - (-17.83920987687)) < 1e-10, "BLUE lat must equal identical input lat");
    assert(Math.abs(blueIdentical.lon - (-63.19424410558)) < 1e-10, "BLUE lon must equal identical input lon");
    // Standard error for 15 identical samples with sigma=3.5: sigma / sqrt(15) ~ 0.9036m
    assert(Math.abs(blueIdentical.acc - (3.5 / Math.sqrt(15))) < 1e-4, "BLUE acc must match sigma / sqrt(N)");

    // 4.2 Extreme coordinates (North Pole, South Pole, Equator, Antimeridian)
    const extremeCases = [
        { lat: 90.0, lon: 0.0, acc: 2.0 },
        { lat: -90.0, lon: 0.0, acc: 2.0 },
        { lat: 0.0, lon: 180.0, acc: 1.5 },
        { lat: 0.0, lon: -180.0, acc: 1.5 }
    ];

    extremeCases.forEach((pt, idx) => {
        const samples = [pt, pt, pt];
        const res = alpha.calculateBlue(samples);
        assert(!isNaN(res.lat), `Extreme case ${idx} lat must not be NaN`);
        assert(!isNaN(res.lon), `Extreme case ${idx} lon must not be NaN`);
        assert(!isNaN(res.acc), `Extreme case ${idx} acc must not be NaN`);
        assert(isFinite(res.lat), `Extreme case ${idx} lat must be finite`);
        assert(isFinite(res.lon), `Extreme case ${idx} lon must be finite`);
    });

    // 4.3 Zero and negative accuracy resilience (instrumental division-by-zero protection)
    const zeroAccSamples = [
        { lat: -17.8392, lon: -63.1942, acc: 0.0 },
        { lat: -17.8392, lon: -63.1942, acc: -10.0 },
        { lat: -17.8392, lon: -63.1942, acc: 0.0001 }
    ];
    const resZero = alpha.calculateBlue(zeroAccSamples);
    assert(!isNaN(resZero.acc), "Accuracy must not be NaN when inputs have 0 or negative accuracy");
    assert(resZero.acc > 0, "BLUE accuracy must be positive");
    assert(isFinite(resZero.acc), "BLUE accuracy must be finite");

    // 4.4 Complete processSamples pipeline with identical points
    const fullProcess = alpha.processSamples(identicalSamples);
    assert(!isNaN(fullProcess.lat), "processSamples lat must not be NaN");
    assert(!isNaN(fullProcess.lon), "processSamples lon must not be NaN");
    assert(!isNaN(fullProcess.acc), "processSamples acc must not be NaN");
    assert(Math.abs(fullProcess.lat - (-17.83920987687)) < 1e-10, "processSamples lat must match exact coordinate");
    assert(Math.abs(fullProcess.lon - (-63.19424410558)) < 1e-10, "processSamples lon must match exact coordinate");
}

// ---------------------------------------------------------
// EXECUTION HARNESS
// ---------------------------------------------------------
async function main() {
    runTest("BLUE Centroid Numerical Stability & Zero-NaN Resistance", testBlueCentroidStability);
    await runAsyncTest("Cold Warmup Buffer Discard (First 3 Samples)", testColdWarmupDiscard);
    await runAsyncTest("Rayleigh 2-Sigma Stochastic Gate (Kinematic Outlier Rejection)", testRayleighStochasticGate);
    await runAsyncTest("Desktop PC Fallback Detection (Accuracy > 100m)", testDesktopPcDetection);

    console.log(`\n=== RESULTS: ${testsPassed} PASSED, ${testsFailed} FAILED ===`);
    if (testsFailed > 0) {
        process.exit(1);
    }
}

main().catch(err => {
    console.error("Fatal error:", err);
    process.exit(1);
});
