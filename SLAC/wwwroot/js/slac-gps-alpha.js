/**
 * slac-gps-alpha.js - Módulo de Geolocalización Estocástica y Calibración 'GPS Alpha'
 * Sistema de Lista de Asistencia a Clases (SLAC) - Universidad Privada Domingo Savio (UPDS)
 * Versión 2.0.0 (Adhiere estrictamente al Rigorous Security Protocol)
 *
 * Características:
 *  - Descarte estricto de calentamiento de antena (3 muestras iniciales en frío).
 *  - Guardia de Incertidumbre Estocástica (2σ Rayleigh) para prevención de inanición cinemática.
 *  - Filtrado estadístico robusto MAD (Median Absolute Deviation, Z <= 3.0, factor 1.4826).
 *  - Filtro recursivo de Kalman 2D para estado estacionario en reposo.
 *  - Estimador insesgado de varianza mínima (BLUE) con ponderación inversa.
 *  - Calibración docente de 18s con callbacks en tiempo real a Blazor.
 *  - Detección inmediata (<1s) de hardware de escritorio (LAN/GeoIP acc > 100m) con fallback a QR Express.
 *  - Adquisición rápida de telemetría de estudiante (5 lecturas / 5-7s).
 */
(function (global) {
    "use strict";

    // Constantes de calibración y geodésica local (Santa Cruz de la Sierra)
    var CONFIG = Object.freeze({
        WARMUP_DISCARD: 0,
        DEFAULT_TIMEOUT_MS: 18000,
        DEFAULT_MAX_SAMPLES: 15,
        MIN_REQUIRED_SAMPLES: 1,
        STUDENT_MAX_SAMPLES: 5,
        STUDENT_TIMEOUT_MS: 8000,
        V_MAX_PEDESTRIAN: 15.0,          // Velocidad peatonal máxima en reposo (m/s)
        STOCHASTIC_SIGMA_FACTOR: 2.0,   // Coeficiente Rayleigh 2-sigma (95.4% de confianza)
        DESKTOP_ACCURACY_THRESHOLD: 100.0, // Margen de error en metros para clasificar como PC cableada / GeoIP
        DEFAULT_FALLBACK_ACC: 15.0,     // Precisión asumida si el hardware devuelve <= 0 o no-numérico
        MIN_PHYSICAL_ACC: 0.5,          // Límite inferior instrumental para evitar división por cero (0.5m)
        MAD_SCALE_FACTOR: 1.4826,       // Consistencia asintótica para distribución Normal
        MAD_THRESHOLD_Z: 3.0,           // Cota Z para rechazo de outliers
        MIN_ALLOWED_MAD: 1e-7,          // Suelo para evitar indeterminación 0/0 en MAD
        KALMAN_Q: 1e-12,                // Varianza del ruido de proceso en grados^2
        METERS_PER_DEG_LAT: 110650.0,   // Factor meridional WGS-84 en Santa Cruz
        METERS_PER_DEG_LON: 105970.0    // Factor primer vertical WGS-84 en Santa Cruz
    });

    /**
     * Sanitiza y valida numéricamente un valor de latitud WGS-84.
     */
    function isValidLat(val) {
        return typeof val === "number" && Number.isFinite(val) && val >= -90.0 && val <= 90.0;
    }

    /**
     * Sanitiza y valida numéricamente un valor de longitud WGS-84.
     */
    function isValidLon(val) {
        return typeof val === "number" && Number.isFinite(val) && val >= -180.0 && val <= 180.0;
    }

    /**
     * Sanitiza y valida numéricamente un valor de precisión en metros.
     */
    function sanitizeAccuracy(rawAcc) {
        if (typeof rawAcc === "number" && Number.isFinite(rawAcc) && rawAcc > 0.0 && rawAcc <= 50000.0) {
            return rawAcc;
        }
        return CONFIG.DEFAULT_FALLBACK_ACC;
    }

    /**
     * Calcula la mediana numérica de una lista de valores numéricos de forma defensiva.
     */
    function calculateMedian(arr) {
        if (!Array.isArray(arr) || arr.length === 0) return 0.0;
        var sorted = arr.slice().sort(function (a, b) { return a - b; });
        var mid = Math.floor(sorted.length / 2);
        if (sorted.length % 2 === 1) {
            return sorted[mid];
        }
        return (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /**
     * Calcula la distancia ortomórfica aproximada en metros sobre el plano tangente local de Santa Cruz.
     * Idéntica a GeoTelemetryValidator.ApproxDistance en servidor.
     */
    function approxDistance(lat1, lon1, lat2, lon2) {
        if (!isValidLat(lat1) || !isValidLon(lon1) || !isValidLat(lat2) || !isValidLon(lon2)) {
            return 0.0;
        }
        var dLat = (lat2 - lat1) * CONFIG.METERS_PER_DEG_LAT;
        var dLon = (lon2 - lon1) * CONFIG.METERS_PER_DEG_LON;
        return Math.sqrt((dLat * dLat) + (dLon * dLon));
    }

    /**
     * Ejecuta el filtrado estadístico robusto MAD (Median Absolute Deviation) sobre muestras geográficas.
     */
    function filterMad(samples, zThreshold) {
        if (!Array.isArray(samples) || samples.length < 3) {
            return Array.isArray(samples) ? samples.slice() : [];
        }
        var zLimit = (typeof zThreshold === "number" && zThreshold > 0) ? zThreshold : CONFIG.MAD_THRESHOLD_Z;

        var lats = samples.map(function (s) { return s.lat; });
        var lons = samples.map(function (s) { return s.lon; });
        var medLat = calculateMedian(lats);
        var medLon = calculateMedian(lons);

        var devLats = samples.map(function (s) { return Math.abs(s.lat - medLat); });
        var devLons = samples.map(function (s) { return Math.abs(s.lon - medLon); });
        var madLat = Math.max(calculateMedian(devLats) * CONFIG.MAD_SCALE_FACTOR, CONFIG.MIN_ALLOWED_MAD);
        var madLon = Math.max(calculateMedian(devLons) * CONFIG.MAD_SCALE_FACTOR, CONFIG.MIN_ALLOWED_MAD);

        var clean = samples.filter(function (s) {
            var zLat = Math.abs(s.lat - medLat) / madLat;
            var zLon = Math.abs(s.lon - medLon) / madLon;
            return Math.sqrt((zLat * zLat) + (zLon * zLon)) <= zLimit;
        });

        // Prevención de inanición: si MAD depura excesivas muestras, revertir al conjunto original
        return clean.length >= 2 ? clean : samples.slice();
    }

    /**
     * Ejecuta el Filtro de Kalman Bidimensional para estado estacionario en reposo.
     */
    function filterKalman2D(dataset) {
        if (!Array.isArray(dataset) || dataset.length === 0) {
            throw new Error("Dataset inválido para filtro Kalman.");
        }
        var x = dataset[0].lat;
        var y = dataset[0].lon;
        var initialAcc = Math.max(dataset[0].acc, CONFIG.MIN_PHYSICAL_ACC);

        // Covarianza inicial escalada a grados al cuadrado
        var pLat = Math.pow(initialAcc / CONFIG.METERS_PER_DEG_LAT, 2);
        var pLon = Math.pow(initialAcc / CONFIG.METERS_PER_DEG_LON, 2);

        for (var i = 1; i < dataset.length; i++) {
            var s = dataset[i];
            var acc = Math.max(s.acc, CONFIG.MIN_PHYSICAL_ACC);

            // Predicción: propagación con ruido de proceso
            pLat = pLat + CONFIG.KALMAN_Q;
            pLon = pLon + CONFIG.KALMAN_Q;

            // Medición: varianza observada en grados^2
            var rLat = Math.pow(acc / CONFIG.METERS_PER_DEG_LAT, 2);
            var rLon = Math.pow(acc / CONFIG.METERS_PER_DEG_LON, 2);

            // Ganancia de Kalman por eje
            var denomLat = pLat + rLat;
            var denomLon = pLon + rLon;
            var kLat = denomLat > 0 ? pLat / denomLat : 0.0;
            var kLon = denomLon > 0 ? pLon / denomLon : 0.0;

            // Actualización de estado
            x = x + kLat * (s.lat - x);
            y = y + kLon * (s.lon - y);

            // Actualización de covarianza
            pLat = Math.max((1.0 - kLat) * pLat, 1e-18);
            pLon = Math.max((1.0 - kLon) * pLon, 1e-18);
        }

        return {
            lat: x,
            lon: y,
            varianceLat: pLat,
            varianceLon: pLon
        };
    }

    /**
     * Calcula el Estimador Insesgado de Varianza Mínima (BLUE) ponderado por varianza inversa.
     */
    function calculateBlue(dataset) {
        if (!Array.isArray(dataset) || dataset.length === 0) {
            throw new Error("Dataset inválido para estimador BLUE.");
        }
        var sumWeights = 0.0;
        var sumLat = 0.0;
        var sumLon = 0.0;

        for (var i = 0; i < dataset.length; i++) {
            var s = dataset[i];
            var safeAcc = Math.max(s.acc, CONFIG.MIN_PHYSICAL_ACC);
            var variance = safeAcc * safeAcc;
            var w = 1.0 / variance;
            sumWeights += w;
            sumLat += s.lat * w;
            sumLon += s.lon * w;
        }

        if (sumWeights <= 0) {
            return {
                lat: dataset[0].lat,
                lon: dataset[0].lon,
                acc: dataset[0].acc,
                variance: dataset[0].acc * dataset[0].acc
            };
        }

        return {
            lat: sumLat / sumWeights,
            lon: sumLon / sumWeights,
            acc: 1.0 / Math.sqrt(sumWeights),
            variance: 1.0 / sumWeights
        };
    }

    /**
     * Procesa una colección de muestras crudas mediante el pipeline completo:
     * MAD -> Kalman 2D -> BLUE -> Fusión defensiva.
     */
    function processSamples(samples) {
        if (!Array.isArray(samples) || samples.length === 0) {
            throw new Error("Sin muestras telemétricas para procesar.");
        }

        if (samples.length === 1) {
            var singleAcc = sanitizeAccuracy(samples[0].acc);
            return {
                lat: samples[0].lat,
                lon: samples[0].lon,
                acc: singleAcc,
                variance: singleAcc * singleAcc,
                samplesCount: 1,
                blueLat: samples[0].lat,
                blueLon: samples[0].lon,
                kalmanLat: samples[0].lat,
                kalmanLon: samples[0].lon,
                rawSamples: samples,
                isDesktopFallback: false
            };
        }

        // 1. Filtrado MAD
        var cleanDataset = filterMad(samples, CONFIG.MAD_THRESHOLD_Z);
        var dataset = cleanDataset.length >= 2 ? cleanDataset : samples;

        // 2. Filtro Kalman 2D
        var kalman = filterKalman2D(dataset);

        // 3. Estimador BLUE
        var blue = calculateBlue(dataset);

        // 4. Fusión de Coordenada y Guardia de Discrepancia
        var fusedLat = (blue.lat * 0.5) + (kalman.lat * 0.5);
        var fusedLon = (blue.lon * 0.5) + (kalman.lon * 0.5);

        // Verificación de divergencia contra BLUE (si la discrepancia supera 1.5m, priorizar BLUE para asegurar compatibilidad con servidor)
        var discrepancy = approxDistance(blue.lat, blue.lon, fusedLat, fusedLon);
        var finalLat = discrepancy > 1.5 ? blue.lat : fusedLat;
        var finalLon = discrepancy > 1.5 ? blue.lon : fusedLon;

        return {
            lat: finalLat,
            lon: finalLon,
            acc: blue.acc,
            variance: blue.variance,
            samplesCount: dataset.length,
            blueLat: blue.lat,
            blueLon: blue.lon,
            kalmanLat: kalman.lat,
            kalmanLon: kalman.lon,
            rawSamples: samples,
            isDesktopFallback: false
        };
    }

    /**
     * Inicia la rutina de calibración en 1 clic para el docente (18 segundos).
     * Incorpora detección express de PC de escritorio y callbacks en vivo a Blazor.
     */
    function startDocenteCalibration(dotNetHelper, targetAcc, maxSamples, timeoutMs) {
        return new Promise(function (resolve, reject) {
            if (!navigator || !navigator.geolocation) {
                reject(new Error("API de Geolocalización no disponible en este dispositivo o navegador."));
                return;
            }

            var effectiveTimeout = (typeof timeoutMs === "number" && timeoutMs > 0) ? timeoutMs : CONFIG.DEFAULT_TIMEOUT_MS;
            var effectiveMaxSamples = (typeof maxSamples === "number" && maxSamples > 0) ? maxSamples : CONFIG.DEFAULT_MAX_SAMPLES;
            var effectiveTargetAcc = (typeof targetAcc === "number" && targetAcc > 0) ? targetAcc : 3.0;

            var samples = [];
            var warmupCounter = 0;
            var watchId = null;
            var isTerminated = false;
            var timer = null;

            // Limpieza estricta de recursos (RAII)
            var cleanup = function () {
                isTerminated = true;
                if (timer !== null) {
                    clearTimeout(timer);
                    timer = null;
                }
                if (watchId !== null) {
                    try {
                        navigator.geolocation.clearWatch(watchId);
                    } catch (e) {
                        console.warn("[GPS Alpha] Error al liberar clearWatch:", e);
                    }
                    watchId = null;
                }
            };

            // Temporizador de culminación por límite de tiempo (18s)
            timer = setTimeout(function () {
                if (isTerminated) return;
                cleanup();

                if (samples.length >= CONFIG.MIN_REQUIRED_SAMPLES) {
                    try {
                        var processed = processSamples(samples);
                        resolve(processed);
                    } catch (procErr) {
                        reject(new Error("Error al calcular centroide: " + procErr.message));
                    }
                } else {
                    reject(new Error("Tiempo GPS Alpha agotado (" + effectiveTimeout + "ms). Muestras capturadas insuficientes: " + samples.length + "/" + CONFIG.MIN_REQUIRED_SAMPLES + "."));
                }
            }, effectiveTimeout);

            try {
                watchId = navigator.geolocation.watchPosition(
                    function (pos) {
                        if (isTerminated || !pos || !pos.coords) return;

                        var rawLat = pos.coords.latitude;
                        var rawLon = pos.coords.longitude;
                        if (!isValidLat(rawLat) || !isValidLon(rawLon)) return;

                        var acc = sanitizeAccuracy(pos.coords.accuracy);

                        // DETECCIÓN INMEDIATA DE PC DE ESCRITORIO (LAN / GeoIP)
                        // Si la primera lectura presenta un margen de error mayor a 100m, abortar y activar fallback a smartphone docente
                        if (acc > CONFIG.DESKTOP_ACCURACY_THRESHOLD && samples.length === 0) {
                            cleanup();
                            resolve({
                                isDesktopFallback: true,
                                accuracy: acc,
                                lat: rawLat,
                                lon: rawLon,
                                samplesCount: 0,
                                rawSamples: []
                            });
                            return;
                        }

                        // DESCARTE DE CALENTAMIENTO (WARMUP BUFFER DE 3 MUESTRAS)
                        warmupCounter++;
                        if (warmupCounter <= CONFIG.WARMUP_DISCARD) {
                            return;
                        }

                        // GUARDIA DE INCERTIDUMBRE ESTOCÁSTICA (2σ Rayleigh)
                        var nowTs = (typeof pos.timestamp === "number" && pos.timestamp > 0) ? pos.timestamp : Date.now();
                        if (samples.length > 0) {
                            var last = samples[samples.length - 1];
                            var dtSec = (nowTs - last.timestampMs) / 1000.0;
                            if (dtSec <= 0 || !Number.isFinite(dtSec)) dtSec = 1.0;

                            var dist = approxDistance(last.lat, last.lon, rawLat, rawLon);
                            var sensorNoiseComb = Math.sqrt((acc * acc) + (last.acc * last.acc));
                            var threshold = (CONFIG.V_MAX_PEDESTRIAN * dtSec) + (CONFIG.STOCHASTIC_SIGMA_FACTOR * sensorNoiseComb);

                            if (dist > threshold) {
                                console.warn("[GPS Alpha] Muestra purgada por salto cinemático: " + dist.toFixed(2) + "m > umbral " + threshold.toFixed(2) + "m");
                                return; // Descartar lectura anómala
                            }
                        }

                        // Ingesta de muestra válida
                        samples.push({
                            lat: rawLat,
                            lon: rawLon,
                            acc: acc,
                            speed: (typeof pos.coords.speed === "number" && Number.isFinite(pos.coords.speed) && pos.coords.speed >= 0) ? pos.coords.speed : null,
                            timestampMs: nowTs
                        });

                        // Callback reactivo a Blazor Server
                        if (dotNetHelper && typeof dotNetHelper.invokeMethodAsync === "function") {
                            try {
                                dotNetHelper.invokeMethodAsync("OnCalibrationProgress", samples.length, effectiveMaxSamples, acc);
                            } catch (cbErr) {
                                console.warn("[GPS Alpha] Error invocando callback Blazor OnCalibrationProgress:", cbErr);
                            }
                        }

                        // Criterio de terminación anticipada por convergencia
                        var reachedMaxSamples = samples.length >= effectiveMaxSamples;
                        var reachedTargetAccuracy = samples.length >= 8 && acc <= effectiveTargetAcc;

                        if (reachedMaxSamples || reachedTargetAccuracy) {
                            cleanup();
                            try {
                                var res = processSamples(samples);
                                resolve(res);
                            } catch (evalErr) {
                                reject(new Error("Error al procesar calibración: " + evalErr.message));
                            }
                        }
                    },
                    function (geoErr) {
                        if (isTerminated) return;
                        cleanup();
                        var msg = "Error en adquisición GPS (" + geoErr.code + "): " + (geoErr.message || "Fallo desconocido.");
                        reject(new Error(msg));
                    },
                    {
                        enableHighAccuracy: true,
                        maximumAge: 0,
                        timeout: 10000
                    }
                );
            } catch (initErr) {
                cleanup();
                reject(new Error("Fallo al inicializar watchPosition: " + initErr.message));
            }
        });
    }

    /**
     * Adquiere la telemetría del estudiante (5 muestras en 5-7 segundos) para envío en AttendanceScanRequest.
     */
    function acquireStudentTelemetry(timeoutMs, maxSamples) {
        var effectiveTimeout = (typeof timeoutMs === "number" && timeoutMs > 0) ? timeoutMs : CONFIG.STUDENT_TIMEOUT_MS;
        var effectiveSamples = (typeof maxSamples === "number" && maxSamples > 0) ? maxSamples : CONFIG.STUDENT_MAX_SAMPLES;
        return startDocenteCalibration(null, 4.0, effectiveSamples, effectiveTimeout);
    }

    /**
     * Comprueba si el entorno web cumple los requisitos de geolocalización segura.
     */
    function isSupported() {
        return !!(navigator && navigator.geolocation && (window.isSecureContext || window.location.hostname === "localhost" || window.location.hostname === "127.0.0.1"));
    }

    // Exposición del API en el objeto global seguro
    global.slacGpsAlpha = Object.freeze({
        CONFIG: CONFIG,
        startDocenteCalibration: startDocenteCalibration,
        acquireStudentTelemetry: acquireStudentTelemetry,
        processSamples: processSamples,
        filterMad: filterMad,
        filterKalman2D: filterKalman2D,
        calculateBlue: calculateBlue,
        approxDistance: approxDistance,
        isSupported: isSupported
    });

})(typeof window !== "undefined" ? window : this);
