/**
 * lpmScanner — Multi-page document scanner with OpenCV.js.
 *
 * Pipeline per captured frame:
 *   1. Downscale (longer edge ≤ 2400 px)
 *   2. Force portrait (rotate 90° CW if landscape)
 *   3. Detect page boundary (Canny → findContours → largest quad)
 *   4. Perspective warp — crop & flatten to the detected document
 *   5. CLAHE on L channel (colour-preserving contrast enhancement)
 *   6. White-balance normalisation
 *   7. Unsharp-mask sharpening
 *   8. Export as JPEG 0.93
 *
 * Lazy-loads OpenCV.js 4.9.0 on first use.
 */
window.lpmScanner = (function () {
    'use strict';

    var _stream = null;
    var _pages = [];             // [{ full: dataUrl, thumb: dataUrl }]
    var _dotNetRef = null;
    var _mode = 'session';
    var _cvReady = false;
    var _cvLoadPromise = null;
    var _processing = false;

    var MAX_DIM   = 2400;        // downscale target (longer edge)
    var JPG_QUAL  = 0.93;        // JPEG export quality

    // ── Helpers: safe Mat cleanup ────────────────────────────────
    function deleteMats(arr) {
        for (var i = 0; i < arr.length; i++) {
            try { arr[i].delete(); } catch (_) {}
        }
    }

    // ── OpenCV lazy loader ──────────────────────────────────────
    function ensureOpenCv() {
        if (_cvReady) return Promise.resolve();
        if (_cvLoadPromise) return _cvLoadPromise;

        _cvLoadPromise = new Promise(function (resolve, reject) {
            if (typeof cv !== 'undefined' && typeof cv.Mat === 'function') {
                _cvReady = true; resolve(); return;
            }

            var script = document.createElement('script');
            script.async = true;
            script.src = 'https://docs.opencv.org/4.9.0/opencv.js';

            var timer = setTimeout(function () {
                _cvLoadPromise = null;
                reject(new Error('OpenCV load timeout (25 s)'));
            }, 25000);

            script.onload = function () {
                (function check() {
                    if (typeof cv !== 'undefined' && typeof cv.Mat === 'function') {
                        clearTimeout(timer); _cvReady = true; resolve();
                    } else if (typeof cv !== 'undefined') {
                        cv['onRuntimeInitialized'] = function () {
                            clearTimeout(timer); _cvReady = true; resolve();
                        };
                    } else { setTimeout(check, 50); }
                })();
            };
            script.onerror = function () {
                clearTimeout(timer); _cvLoadPromise = null;
                reject(new Error('Failed to load OpenCV.js'));
            };
            document.head.appendChild(script);
        });
        return _cvLoadPromise;
    }

    // ── Get output canvas (reusable hidden element) ─────────────
    function getOutputCanvas() {
        var c = document.getElementById('scan-output');
        if (!c) {
            c = document.createElement('canvas');
            c.id = 'scan-output';
            c.style.display = 'none';
            document.body.appendChild(c);
        }
        return c;
    }

    // ════════════════════════════════════════════════════════════
    //  STEP 3 — Robust page boundary detection + perspective warp
    //
    //  Tries multiple strategies to find the document page:
    //    A. Canny edge detection with several threshold pairs
    //    B. Adaptive threshold (handles uneven lighting)
    //    C. Colour-distance from dominant border colour
    //  For each strategy, finds contours, looks for the best
    //  quadrilateral that covers ≥ 10% of the image.
    // ════════════════════════════════════════════════════════════

    function detectAndWarp(src) {
        // src is CV_8UC4 (RGBA).  Returns new RGBA Mat or null.
        var mats = [], vectors = [];
        try {
            var gray = new cv.Mat(); mats.push(gray);
            cv.cvtColor(src, gray, cv.COLOR_RGBA2GRAY);

            var imgArea = src.rows * src.cols;
            var minArea = imgArea * 0.10;  // page must be ≥ 10% of frame

            // Strategy A — Canny with multiple thresholds
            var cannyPairs = [[30, 100], [50, 150], [75, 200], [20, 80]];
            for (var ci = 0; ci < cannyPairs.length; ci++) {
                var quad = tryCannyStrategy(gray, cannyPairs[ci][0], cannyPairs[ci][1], minArea, mats, vectors);
                if (quad) {
                    var w = warpQuad(src, quad, mats);
                    if (w) {
                        console.log('[scanner] Boundary found via Canny(' + cannyPairs[ci][0] + ',' + cannyPairs[ci][1] + ')');
                        return w;
                    }
                }
            }
            console.log('[scanner] Canny strategies exhausted');

            // Strategy B — Adaptive threshold (works when page is lighter than bg)
            var quadB = tryAdaptiveThreshStrategy(gray, minArea, mats, vectors);
            if (quadB) {
                var w2 = warpQuad(src, quadB, mats);
                if (w2) {
                    console.log('[scanner] Boundary found via adaptive threshold');
                    return w2;
                }
            }
            console.log('[scanner] Adaptive threshold strategy failed');

            // Strategy C — Colour distance from border (assumes bg is different from page)
            var quadC = tryBorderColourStrategy(src, minArea, mats, vectors);
            if (quadC) {
                var w3 = warpQuad(src, quadC, mats);
                if (w3) {
                    console.log('[scanner] Boundary found via border colour distance');
                    return w3;
                }
            }
            console.log('[scanner] Border colour strategy failed');

            console.warn('[scanner] No page boundary detected by any strategy');
            return null;
        } catch (ex) {
            console.warn('[scanner] Page detection failed:', ex.message);
            return null;
        } finally {
            deleteMats(mats);
            for (var v = 0; v < vectors.length; v++) {
                try { vectors[v].delete(); } catch (_) {}
            }
        }
    }

    // ── Strategy A: Canny edge + contour ────────────────────────
    function tryCannyStrategy(gray, lo, hi, minArea, mats, vectors) {
        var blurred = new cv.Mat(); mats.push(blurred);
        cv.GaussianBlur(gray, blurred, new cv.Size(5, 5), 0);

        var edges = new cv.Mat(); mats.push(edges);
        cv.Canny(blurred, edges, lo, hi);

        var kernel = cv.getStructuringElement(cv.MORPH_RECT, new cv.Size(3, 3));
        mats.push(kernel);
        var dilated = new cv.Mat(); mats.push(dilated);
        cv.dilate(edges, dilated, kernel, new cv.Point(-1, -1), 2);

        // Also close to bridge larger gaps
        var closed = new cv.Mat(); mats.push(closed);
        var kClose = cv.getStructuringElement(cv.MORPH_RECT, new cv.Size(7, 7));
        mats.push(kClose);
        cv.morphologyEx(dilated, closed, cv.MORPH_CLOSE, kClose);

        return findBestQuad(closed, minArea, mats, vectors);
    }

    // ── Strategy B: Adaptive threshold ──────────────────────────
    function tryAdaptiveThreshStrategy(gray, minArea, mats, vectors) {
        var blurred = new cv.Mat(); mats.push(blurred);
        cv.GaussianBlur(gray, blurred, new cv.Size(11, 11), 0);

        var thresh = new cv.Mat(); mats.push(thresh);
        cv.adaptiveThreshold(blurred, thresh, 255,
            cv.ADAPTIVE_THRESH_GAUSSIAN_C, cv.THRESH_BINARY, 51, 5);

        // Invert: page is white (255), background is dark (0) after threshold;
        // we want contour around the white region
        var inv = new cv.Mat(); mats.push(inv);
        cv.bitwise_not(thresh, inv);

        var kernel = cv.getStructuringElement(cv.MORPH_RECT, new cv.Size(5, 5));
        mats.push(kernel);
        var morphed = new cv.Mat(); mats.push(morphed);
        cv.morphologyEx(inv, morphed, cv.MORPH_CLOSE, kernel, new cv.Point(-1, -1), 3);

        return findBestQuad(morphed, minArea, mats, vectors);
    }

    // ── Strategy C: Border colour distance ──────────────────────
    function tryBorderColourStrategy(src, minArea, mats, vectors) {
        // Sample border pixels to get the dominant background colour,
        // then threshold pixels that are far from that colour.
        var rgb = new cv.Mat(); mats.push(rgb);
        cv.cvtColor(src, rgb, cv.COLOR_RGBA2RGB);

        var h = src.rows, w = src.cols;
        var strip = Math.max(5, Math.round(Math.min(w, h) * 0.03));
        var sumR = 0, sumG = 0, sumB = 0, cnt = 0;

        // Top, bottom, left, right strips
        for (var y = 0; y < h; y++) {
            for (var x = 0; x < w; x++) {
                if (y < strip || y >= h - strip || x < strip || x >= w - strip) {
                    var px = rgb.ucharPtr(y, x);
                    sumR += px[0]; sumG += px[1]; sumB += px[2]; cnt++;
                }
            }
        }
        if (cnt === 0) return null;
        var bgR = sumR / cnt, bgG = sumG / cnt, bgB = sumB / cnt;

        // Create distance mask: pixels far from background → white
        var mask = new cv.Mat(h, w, cv.CV_8UC1); mats.push(mask);
        for (var y2 = 0; y2 < h; y2++) {
            for (var x2 = 0; x2 < w; x2++) {
                var px2 = rgb.ucharPtr(y2, x2);
                var dr = px2[0] - bgR, dg = px2[1] - bgG, db = px2[2] - bgB;
                var dist = Math.sqrt(dr * dr + dg * dg + db * db);
                mask.ucharPtr(y2, x2)[0] = dist > 40 ? 255 : 0;
            }
        }

        var kernel = cv.getStructuringElement(cv.MORPH_RECT, new cv.Size(9, 9));
        mats.push(kernel);
        var morphed = new cv.Mat(); mats.push(morphed);
        cv.morphologyEx(mask, morphed, cv.MORPH_CLOSE, kernel, new cv.Point(-1, -1), 3);

        return findBestQuad(morphed, minArea, mats, vectors);
    }

    // ── Shared: find best quadrilateral from binary image ───────
    function findBestQuad(binaryImg, minArea, mats, vectors) {
        var contours = new cv.MatVector(); vectors.push(contours);
        var hierarchy = new cv.Mat(); mats.push(hierarchy);
        cv.findContours(binaryImg, contours, hierarchy,
            cv.RETR_EXTERNAL, cv.CHAIN_APPROX_SIMPLE);

        // Collect all contours above minArea, sorted largest first
        var candidates = [];
        for (var i = 0; i < contours.size(); i++) {
            var area = cv.contourArea(contours.get(i));
            if (area >= minArea) candidates.push({ idx: i, area: area });
        }
        candidates.sort(function (a, b) { return b.area - a.area; });

        // Try each candidate with multiple epsilon values
        var epsilons = [0.015, 0.02, 0.03, 0.04, 0.05, 0.07, 0.10];
        for (var c = 0; c < Math.min(candidates.length, 5); c++) {
            var cnt = contours.get(candidates[c].idx);
            var peri = cv.arcLength(cnt, true);

            for (var e = 0; e < epsilons.length; e++) {
                var approx = new cv.Mat(); mats.push(approx);
                cv.approxPolyDP(cnt, approx, epsilons[e] * peri, true);

                if (approx.rows === 4 && cv.isContourConvex(approx)) {
                    var pts = [];
                    for (var j = 0; j < 4; j++) {
                        pts.push({ x: approx.intAt(j, 0), y: approx.intAt(j, 1) });
                    }
                    var ordered = orderQuadPoints(pts);
                    // Verify it's a reasonable rectangle (no extremely acute angles)
                    if (isReasonableQuad(ordered)) return ordered;
                }
            }
        }
        return null;
    }

    // ── Order 4 points: TL, TR, BR, BL ─────────────────────────
    function orderQuadPoints(pts) {
        pts.sort(function (a, b) { return a.y - b.y; });
        var top = pts.slice(0, 2).sort(function (a, b) { return a.x - b.x; });
        var bot = pts.slice(2, 4).sort(function (a, b) { return a.x - b.x; });
        return [top[0], top[1], bot[1], bot[0]]; // TL TR BR BL
    }

    // ── Validate the quad is roughly rectangular ────────────────
    function isReasonableQuad(pts) {
        // Check that no side is less than 5% of the longest side
        var sides = [
            Math.hypot(pts[1].x - pts[0].x, pts[1].y - pts[0].y),
            Math.hypot(pts[2].x - pts[1].x, pts[2].y - pts[1].y),
            Math.hypot(pts[3].x - pts[2].x, pts[3].y - pts[2].y),
            Math.hypot(pts[0].x - pts[3].x, pts[0].y - pts[3].y),
        ];
        var maxSide = Math.max.apply(null, sides);
        var minSide = Math.min.apply(null, sides);
        if (minSide < maxSide * 0.15) return false;   // one side way too short
        // Check aspect ratio is plausible for a document (between 1:4 and 4:1)
        var w = (sides[0] + sides[2]) / 2;
        var h = (sides[1] + sides[3]) / 2;
        var ratio = Math.max(w, h) / Math.min(w, h);
        return ratio < 4;
    }

    // ── Perspective warp from 4 ordered points ──────────────────
    function warpQuad(src, pts, mats) {
        try {
            var wTop = Math.hypot(pts[1].x - pts[0].x, pts[1].y - pts[0].y);
            var wBot = Math.hypot(pts[2].x - pts[3].x, pts[2].y - pts[3].y);
            var hLeft = Math.hypot(pts[3].x - pts[0].x, pts[3].y - pts[0].y);
            var hRight = Math.hypot(pts[2].x - pts[1].x, pts[2].y - pts[1].y);
            var dstW = Math.round(Math.max(wTop, wBot));
            var dstH = Math.round(Math.max(hLeft, hRight));
            if (dstW < 100 || dstH < 100) return null;

            var srcPts = cv.matFromArray(4, 1, cv.CV_32FC2, [
                pts[0].x, pts[0].y, pts[1].x, pts[1].y,
                pts[2].x, pts[2].y, pts[3].x, pts[3].y
            ]); mats.push(srcPts);

            var dstPts = cv.matFromArray(4, 1, cv.CV_32FC2, [
                0, 0, dstW, 0, dstW, dstH, 0, dstH
            ]); mats.push(dstPts);

            var M = cv.getPerspectiveTransform(srcPts, dstPts); mats.push(M);
            var warped = new cv.Mat(); // caller puts in toDelete
            cv.warpPerspective(src, warped, M, new cv.Size(dstW, dstH),
                cv.INTER_LINEAR, cv.BORDER_REPLICATE);
            return warped;
        } catch (ex) {
            console.warn('[scanner] Warp failed:', ex.message);
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  STEP 5 — CLAHE colour enhancement (LAB, L-channel only)
    // ════════════════════════════════════════════════════════════
    function applyClahe(rgbMat) {
        // rgbMat is CV_8UC3 (RGB).  Modifies in-place via returned Mat.
        var mats = [], vectors = [];
        var clahe = null;
        try {
            var lab = new cv.Mat(); mats.push(lab);
            cv.cvtColor(rgbMat, lab, cv.COLOR_RGB2Lab);

            var channels = new cv.MatVector(); vectors.push(channels);
            cv.split(lab, channels);

            var L = channels.get(0);

            // CLAHE — try both constructor forms
            try {
                clahe = new cv.CLAHE(3.0, new cv.Size(8, 8));
            } catch (_) {
                try { clahe = cv.createCLAHE(3.0, new cv.Size(8, 8)); } catch (__) {
                    return rgbMat; // CLAHE unavailable, return as-is
                }
            }

            var enhancedL = new cv.Mat(); mats.push(enhancedL);
            clahe.apply(L, enhancedL);

            var mergeVec = new cv.MatVector(); vectors.push(mergeVec);
            mergeVec.push_back(enhancedL);
            mergeVec.push_back(channels.get(1));
            mergeVec.push_back(channels.get(2));

            var merged = new cv.Mat(); mats.push(merged);
            cv.merge(mergeVec, merged);

            var result = new cv.Mat();
            cv.cvtColor(merged, result, cv.COLOR_Lab2RGB);
            return result; // caller must delete
        } catch (ex) {
            console.warn('[scanner] CLAHE failed:', ex.message);
            return null;
        } finally {
            if (clahe) try { clahe.delete(); } catch (_) {}
            deleteMats(mats);
            for (var v = 0; v < vectors.length; v++) {
                try { vectors[v].delete(); } catch (_) {}
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  STEP 6 — White-balance normalisation
    // ════════════════════════════════════════════════════════════
    function whiteBalance(rgbMat) {
        // Simple grey-world white balance: scale each channel so its mean = 128
        var mats = [], vectors = [];
        try {
            var channels = new cv.MatVector(); vectors.push(channels);
            cv.split(rgbMat, channels);

            var result = new cv.Mat();
            for (var c = 0; c < 3; c++) {
                var ch = channels.get(c);
                var mean = cv.mean(ch);
                var avg = mean[0];
                if (avg < 1) avg = 1;
                var scale = 128.0 / avg;
                // Clamp scale so we don't blow out already-bright images
                if (scale > 1.6) scale = 1.6;
                if (scale < 0.7) scale = 0.7;
                var scaled = new cv.Mat(); mats.push(scaled);
                ch.convertTo(scaled, -1, scale, 0);
                // Replace channel in-place via put back
                var tmp = new cv.MatVector(); vectors.push(tmp);
                // Rebuild channels — push the scaled one and keep others
            }

            // Rebuild cleanly
            var r = channels.get(0), g = channels.get(1), b = channels.get(2);
            var meanR = cv.mean(r)[0] || 1;
            var meanG = cv.mean(g)[0] || 1;
            var meanB = cv.mean(b)[0] || 1;
            var grayAvg = (meanR + meanG + meanB) / 3.0;
            var scaleR = Math.min(1.5, Math.max(0.7, grayAvg / meanR));
            var scaleG = Math.min(1.5, Math.max(0.7, grayAvg / meanG));
            var scaleB = Math.min(1.5, Math.max(0.7, grayAvg / meanB));

            var adjR = new cv.Mat(); mats.push(adjR);
            var adjG = new cv.Mat(); mats.push(adjG);
            var adjB = new cv.Mat(); mats.push(adjB);
            r.convertTo(adjR, -1, scaleR, 0);
            g.convertTo(adjG, -1, scaleG, 0);
            b.convertTo(adjB, -1, scaleB, 0);

            var mergeVec = new cv.MatVector(); vectors.push(mergeVec);
            mergeVec.push_back(adjR);
            mergeVec.push_back(adjG);
            mergeVec.push_back(adjB);
            cv.merge(mergeVec, result);
            return result; // caller must delete
        } catch (ex) {
            console.warn('[scanner] White balance failed:', ex.message);
            return null;
        } finally {
            deleteMats(mats);
            for (var v = 0; v < vectors.length; v++) {
                try { vectors[v].delete(); } catch (_) {}
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  STEP 7 — Unsharp mask (sharpening)
    // ════════════════════════════════════════════════════════════
    function sharpen(rgbMat) {
        // Unsharp mask: result = src + amount * (src - blur)
        var mats = [];
        try {
            var blurred = new cv.Mat(); mats.push(blurred);
            cv.GaussianBlur(rgbMat, blurred, new cv.Size(0, 0), 2.0);

            var result = new cv.Mat();
            // addWeighted: dst = src1*alpha + src2*beta + gamma
            // Sharpened = 1.5*original - 0.5*blurred
            cv.addWeighted(rgbMat, 1.5, blurred, -0.5, 0, result);
            return result; // caller must delete
        } catch (ex) {
            console.warn('[scanner] Sharpen failed:', ex.message);
            return null;
        } finally {
            deleteMats(mats);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Full OpenCV pipeline
    //  Returns { canvas, boundaryFound }
    // ════════════════════════════════════════════════════════════
    function processWithOpenCv(canvas) {
        var ctx = canvas.getContext('2d');
        var imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
        var toDelete = [];
        var boundaryFound = false;

        try {
            var src = cv.matFromImageData(imageData); toDelete.push(src);
            console.log('[scanner] Input: ' + src.cols + 'x' + src.rows);

            // Step 3: detect page boundary + perspective warp
            var t0 = performance.now();
            var warped = detectAndWarp(src);
            console.log('[scanner] Boundary detection: ' + (performance.now() - t0).toFixed(0) + ' ms → ' +
                (warped ? 'FOUND (' + warped.cols + 'x' + warped.rows + ')' : 'NOT FOUND'));

            var working = warped || src;
            if (warped) { toDelete.push(warped); boundaryFound = true; }

            // Convert RGBA → RGB
            var rgb = new cv.Mat(); toDelete.push(rgb);
            cv.cvtColor(working, rgb, cv.COLOR_RGBA2RGB);

            // Step 5: CLAHE
            var t1 = performance.now();
            var enhanced = applyClahe(rgb);
            var afterClahe = enhanced || rgb;
            if (enhanced && enhanced !== rgb) toDelete.push(enhanced);
            console.log('[scanner] CLAHE: ' + (performance.now() - t1).toFixed(0) + ' ms');

            // Step 6: white balance
            var t2 = performance.now();
            var balanced = whiteBalance(afterClahe);
            var afterWb = balanced || afterClahe;
            if (balanced && balanced !== afterClahe) toDelete.push(balanced);
            console.log('[scanner] White balance: ' + (performance.now() - t2).toFixed(0) + ' ms');

            // Step 7: sharpen
            var t3 = performance.now();
            var sharpened = sharpen(afterWb);
            var final_ = sharpened || afterWb;
            if (sharpened && sharpened !== afterWb) toDelete.push(sharpened);
            console.log('[scanner] Sharpen: ' + (performance.now() - t3).toFixed(0) + ' ms');

            var outCanvas = getOutputCanvas();
            cv.imshow(outCanvas, final_);
            console.log('[scanner] Output: ' + outCanvas.width + 'x' + outCanvas.height +
                ' | boundary=' + boundaryFound);
            return { canvas: outCanvas, boundaryFound: boundaryFound };
        } catch (ex) {
            console.error('[scanner] OpenCV pipeline failed:', ex);
            return null;
        } finally {
            deleteMats(toDelete);
        }
    }

    // ── Fallback: canvas-only colour contrast/brightness boost ──
    function fallbackFilter(canvas) {
        var ctx = canvas.getContext('2d');
        var imgData = ctx.getImageData(0, 0, canvas.width, canvas.height);
        var d = imgData.data;
        var contrast = 1.35, brightness = 12;
        for (var i = 0; i < d.length; i += 4) {
            d[i]     = Math.min(255, Math.max(0, (d[i]     - 128) * contrast + 128 + brightness));
            d[i + 1] = Math.min(255, Math.max(0, (d[i + 1] - 128) * contrast + 128 + brightness));
            d[i + 2] = Math.min(255, Math.max(0, (d[i + 2] - 128) * contrast + 128 + brightness));
        }
        ctx.putImageData(imgData, 0, 0);
        return canvas;
    }

    // ── Downscale + force portrait ──────────────────────────────
    function prepareCanvas(sourceCanvas) {
        var w = sourceCanvas.width, h = sourceCanvas.height;
        if (w <= 0 || h <= 0) return sourceCanvas;

        var longer = Math.max(w, h);
        var scale = longer > MAX_DIM ? MAX_DIM / longer : 1;
        var nw = Math.round(w * scale);
        var nh = Math.round(h * scale);

        var prep = document.createElement('canvas');
        prep.width = nw;
        prep.height = nh;
        prep.getContext('2d').drawImage(sourceCanvas, 0, 0, nw, nh);

        // Force portrait
        if (prep.width > prep.height) {
            var rotated = document.createElement('canvas');
            rotated.width = prep.height;
            rotated.height = prep.width;
            var rCtx = rotated.getContext('2d');
            rCtx.translate(rotated.width, 0);
            rCtx.rotate(Math.PI / 2);
            rCtx.drawImage(prep, 0, 0);
            return rotated;
        }
        return prep;
    }

    // ── Thumbnail ───────────────────────────────────────────────
    function makeThumbnail(fullDataUrl) {
        return new Promise(function (resolve) {
            var img = new Image();
            img.onload = function () {
                var aspect = img.height / img.width;
                var tw = 72, th = Math.round(tw * aspect);
                var tc = document.createElement('canvas');
                tc.width = tw; tc.height = th;
                tc.getContext('2d').drawImage(img, 0, 0, tw, th);
                resolve(tc.toDataURL('image/jpeg', 0.6));
            };
            img.onerror = function () { resolve(''); };
            img.src = fullDataUrl;
        });
    }

    // ── Core capture pipeline ───────────────────────────────────
    async function runPipeline(sourceCanvas) {
        if (_processing) return;
        _processing = true;

        try {
            var t0 = performance.now();
            // Steps 1-2: downscale + portrait
            var prepared = prepareCanvas(sourceCanvas);
            console.log('[scanner] Prepared: ' + prepared.width + 'x' + prepared.height +
                ' (' + (performance.now() - t0).toFixed(0) + ' ms)');

            // Double rAF to guarantee a paint before heavy work
            await new Promise(function (r) {
                requestAnimationFrame(function () { requestAnimationFrame(r); });
            });

            var resultCanvas;
            var boundaryFound = false;

            if (_cvReady) {
                var result = processWithOpenCv(prepared);
                if (result) {
                    resultCanvas = result.canvas;
                    boundaryFound = result.boundaryFound;
                } else {
                    resultCanvas = fallbackFilter(prepared);
                }
            } else {
                console.log('[scanner] OpenCV not ready, using fallback filter');
                resultCanvas = fallbackFilter(prepared);
            }

            var fullUrl = resultCanvas.toDataURL('image/jpeg', JPG_QUAL);

            // Sanity: not all-black
            var sCtx = resultCanvas.getContext('2d');
            var sample = sCtx.getImageData(0, 0,
                Math.min(100, resultCanvas.width),
                Math.min(100, resultCanvas.height));
            var sum = 0;
            for (var i = 0; i < sample.data.length; i += 4)
                sum += sample.data[i] + sample.data[i+1] + sample.data[i+2];
            if (sum / (sample.data.length / 4 * 3) < 10) {
                console.warn('[scanner] Output too dark, using unprocessed');
                fullUrl = prepared.toDataURL('image/jpeg', JPG_QUAL);
            }

            var thumbUrl = await makeThumbnail(fullUrl);
            _pages.push({ full: fullUrl, thumb: thumbUrl });

            console.log('[scanner] Total pipeline: ' + (performance.now() - t0).toFixed(0) + ' ms | ' +
                'pages=' + _pages.length + ' | boundary=' + boundaryFound);

            if (_dotNetRef) {
                try {
                    _dotNetRef.invokeMethodAsync('OnScanPageAdded', thumbUrl, _pages.length, boundaryFound);
                } catch (e) { console.warn('[scanner] Blazor callback failed:', e); }
            }
        } finally {
            _processing = false;
        }
    }

    // ── Public API ──────────────────────────────────────────────
    return {
        open: async function (dotNetRef, mode) {
            this.close();
            _dotNetRef = dotNetRef;
            _mode = mode || 'session';
            _pages = [];

            // Start OpenCV load in background
            ensureOpenCv().catch(function (e) {
                console.warn('[scanner] OpenCV load issue:', e.message);
            });

            var video = document.getElementById('scan-video');
            if (!video) return false;

            try {
                _stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: 'environment' }, audio: false
                });
            } catch (_) {
                try {
                    _stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
                } catch (e2) {
                    console.warn('[scanner] Camera unavailable:', e2);
                    return false;
                }
            }

            video.srcObject = _stream;
            video.setAttribute('playsinline', '');
            video.setAttribute('autoplay', '');
            try { await video.play(); } catch (_) {}

            ensureOpenCv().then(function () {
                if (_dotNetRef) try { _dotNetRef.invokeMethodAsync('OnOpenCvReady'); } catch (_) {}
            }).catch(function () {
                if (_dotNetRef) try { _dotNetRef.invokeMethodAsync('OnOpenCvFailed'); } catch (_) {}
            });

            return true;
        },

        capture: async function () {
            if (_processing) return;
            var video = document.getElementById('scan-video');
            var canvas = document.getElementById('scan-canvas');
            if (!video || !canvas || video.videoWidth === 0) return;

            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
            await runPipeline(canvas);
        },

        onGalleryFile: function (input) {
            var file = input.files && input.files[0];
            if (!file) return;
            var reader = new FileReader();
            reader.onload = function (ev) {
                var img = new Image();
                img.onload = function () {
                    var c = document.createElement('canvas');
                    c.width = img.naturalWidth;
                    c.height = img.naturalHeight;
                    c.getContext('2d').drawImage(img, 0, 0);
                    runPipeline(c);
                };
                img.src = ev.target.result;
            };
            reader.readAsDataURL(file);
            input.value = '';
        },

        removePage: function (index) {
            if (index >= 0 && index < _pages.length) _pages.splice(index, 1);
            if (_dotNetRef) {
                try { _dotNetRef.invokeMethodAsync('OnScanPageRemoved', index, _pages.length); }
                catch (_) {}
            }
        },

        getPages: function () { return _pages.map(function (p) { return p.full; }); },
        getPageCount: function () { return _pages.length; },
        isOpenCvReady: function () { return _cvReady; },
        isProcessing: function () { return _processing; },

        close: function () {
            if (_stream) {
                _stream.getTracks().forEach(function (t) { t.stop(); });
                _stream = null;
            }
            var video = document.getElementById('scan-video');
            if (video) video.srcObject = null;
            _pages = [];
            _dotNetRef = null;
            _processing = false;
        },

        reconnect: async function () {
            if (!_stream) return false;
            var tracks = _stream.getVideoTracks();
            if (tracks.length > 0 && tracks[0].readyState === 'live') return true;
            try {
                _stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: 'environment' }, audio: false
                });
                var video = document.getElementById('scan-video');
                if (video) { video.srcObject = _stream; try { await video.play(); } catch (_) {} }
                return true;
            } catch (e) {
                console.warn('[scanner] Reconnect failed:', e);
                return false;
            }
        }
    };
})();

document.addEventListener('visibilitychange', function () {
    if (!document.hidden && window.lpmScanner) window.lpmScanner.reconnect();
});
