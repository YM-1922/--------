/**
 * DOCTOR (دكتور) - Main Client JavaScript Application
 * Responsive Arabic RTL & Interactive Engine
 */

// Global App State
window.DoctorApp = {
    cameraScanner: null,
    onBarcodeScannedCallback: null,
    keystrokeBuffer: '',
    lastKeyTime: 0
};

document.addEventListener('DOMContentLoaded', function () {
    initSidebar();
    initGlobalSearch();
    initHardwareScanner();
});

/* ==========================================================================
   1. SIDEBAR TOGGLE & MOBILE NAVIGATION
   ========================================================================== */
function initSidebar() {
    const toggleBtn = document.getElementById('sidebarToggleBtn');
    const closeBtn = document.getElementById('sidebarCloseBtn');
    const sidebar = document.getElementById('appSidebar');
    const overlay = document.getElementById('sidebarOverlay');

    if (toggleBtn && sidebar) {
        toggleBtn.addEventListener('click', function () {
            if (window.innerWidth < 992) {
                sidebar.classList.toggle('show-mobile');
                if (overlay) overlay.classList.toggle('d-none');
            } else {
                sidebar.classList.toggle('collapsed');
                document.querySelector('.app-main')?.classList.toggle('expanded');
            }
        });
    }

    function closeMobileSidebar() {
        if (sidebar) sidebar.classList.remove('show-mobile');
        if (overlay) overlay.classList.add('d-none');
    }

    if (closeBtn) {
        closeBtn.addEventListener('click', closeMobileSidebar);
    }

    if (overlay) {
        overlay.addEventListener('click', closeMobileSidebar);
    }

    // Auto close sidebar when clicking a menu link on mobile
    document.querySelectorAll('.sidebar-menu a').forEach(link => {
        link.addEventListener('click', function () {
            if (window.innerWidth < 992) {
                closeMobileSidebar();
            }
        });
    });

    // Close on Escape key
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && sidebar && sidebar.classList.contains('show-mobile')) {
            closeMobileSidebar();
        }
    });
}

/* ==========================================================================
   2. NOTIFICATIONS POLLER & MANAGER
   ========================================================================== */
function initNotifications() {
    loadUnreadNotifications();
    // Poll every 45 seconds
    setInterval(loadUnreadNotifications, 45000);
}

function loadUnreadNotifications() {
    fetch('/api/notifications/unread')
        .then(res => {
            if (!res.ok) throw new Error('Not logged in or error');
            return res.json();
        })
        .then(data => {
            const badge = document.getElementById('notifBadge');
            const listContainer = document.getElementById('notifDropdownList');

            if (!badge || !listContainer) return;

            if (data.count > 0) {
                badge.innerText = data.count > 9 ? '+9' : data.count;
                badge.classList.remove('d-none');
            } else {
                badge.classList.add('d-none');
            }

            if (data.notifications && data.notifications.length > 0) {
                let html = '';
                data.notifications.forEach(n => {
                    let icon = 'bi-bell';
                    let bg = 'text-primary';
                    if (n.type && n.type.includes('Stock')) { icon = 'bi-exclamation-triangle'; bg = 'text-warning'; }
                    if (n.type && n.type.includes('Repair')) { icon = 'bi-tools'; bg = 'text-info'; }
                    if (n.type && n.type.includes('Sale')) { icon = 'bi-cart-check'; bg = 'text-success'; }

                    const safeTargetUrl = (n.targetUrl || '/Admin').replace(/'/g, "\\'");

                    html += `
                        <div class="p-2 border-bottom notif-item" style="cursor: pointer; transition: background 0.15s;" onclick="openNotification(${n.id}, '${safeTargetUrl}')">
                            <div class="d-flex align-items-start">
                                <i class="bi ${icon} ${bg} fs-5 ms-2"></i>
                                <div class="flex-grow-1">
                                    <div class="fw-bold small text-dark">${escapeHtml(n.title)}</div>
                                    <div class="text-muted small">${escapeHtml(n.message)}</div>
                                    <div class="d-flex justify-content-between align-items-center mt-1">
                                        <span class="text-secondary" style="font-size: 0.7rem;">${n.createdAt}</span>
                                        <span class="text-primary small" style="font-size: 0.75rem;"><i class="bi bi-arrow-left"></i> فتح</span>
                                    </div>
                                </div>
                            </div>
                        </div>
                    `;
                });
                listContainer.innerHTML = html;
            } else {
                listContainer.innerHTML = '<div class="p-3 text-center text-muted small">لا توجد تنبيهات جديدة حالياً</div>';
            }
        })
        .catch(() => {});
}

function openNotification(id, targetUrl) {
    fetch(`/api/notifications/read/${id}`, { method: 'POST' })
        .then(() => {
            window.location.href = targetUrl || '/Admin';
        })
        .catch(() => {
            window.location.href = targetUrl || '/Admin';
        });
}

function markNotifRead(id) {
    fetch(`/api/notifications/read/${id}`, { method: 'POST' })
        .then(() => loadUnreadNotifications())
        .catch(() => {});
}

function markAllNotificationsRead() {
    fetch('/api/notifications/read-all', { method: 'POST' })
        .then(() => {
            loadUnreadNotifications();
            showToast('تم تحديد جميع التنبيهات كمقروءة', 'success');
        })
        .catch(() => {});
}

/* ==========================================================================
   3. GLOBAL SEARCH (IMEI, Barcode, Phone, Customer, Repair Code, Invoices)
   ========================================================================== */
function initGlobalSearch() {
    const input = document.getElementById('globalSearchInput');
    const dropdown = document.getElementById('globalSearchResults');
    let timeout = null;

    if (!input || !dropdown) return;

    // Ctrl + K shortcut
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
            e.preventDefault();
            input.focus();
            input.select();
        }
    });

    // Enter key navigation
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            const query = input.value.trim();
            if (!query) return;

            const firstLink = dropdown.querySelector('a.dropdown-item');
            if (firstLink && !dropdown.classList.contains('d-none')) {
                window.location.href = firstLink.getAttribute('href');
            } else {
                window.location.href = `/Sales/Products?search=${encodeURIComponent(query)}`;
            }
        }
    });

    input.addEventListener('input', function () {
        clearTimeout(timeout);
        const query = input.value.trim();

        if (query.length < 2) {
            dropdown.classList.add('d-none');
            return;
        }

        timeout = setTimeout(() => {
            fetch(`/api/global-search?query=${encodeURIComponent(query)}`)
                .then(res => res.json())
                .then(data => {
                    renderGlobalSearchResults(data, dropdown);
                })
                .catch(() => {});
        }, 250);
    });

    // Close on click outside
    document.addEventListener('click', function (e) {
        if (!input.contains(e.target) && !dropdown.contains(e.target)) {
            dropdown.classList.add('d-none');
        }
    });
}

function renderGlobalSearchResults(data, container) {
    let hasResults = false;
    let html = '';

    if (data.products && data.products.length > 0) {
        hasResults = true;
        html += '<div class="dropdown-header text-primary fw-bold small"><i class="bi bi-box-seam ms-1"></i>الأجهزة والمنتجات</div>';
        data.products.forEach(p => {
            html += `
                <a href="/Sales/Products?search=${encodeURIComponent(p.barcode || p.name)}" class="dropdown-item py-2 border-bottom">
                    <div class="fw-bold">${escapeHtml(p.name)}</div>
                    <div class="text-muted small">باركود: ${p.barcode || '-'} &bull; السعر: ${p.sellingPrice} ج.م &bull; المخزون: ${p.stockQuantity}</div>
                </a>
            `;
        });
    }

    if (data.invoices && data.invoices.length > 0) {
        hasResults = true;
        html += '<div class="dropdown-header text-success fw-bold small mt-2"><i class="bi bi-receipt ms-1"></i>فواتير المبيعات</div>';
        data.invoices.forEach(inv => {
            html += `
                <a href="/Sales/Invoice/${inv.id}" class="dropdown-item py-2 border-bottom">
                    <div class="d-flex justify-content-between">
                        <span class="fw-bold font-monospace text-success">${inv.invoiceNumber}</span>
                        <span>${inv.totalAmount} ج.م</span>
                    </div>
                    <div class="text-muted small">${escapeHtml(inv.customerName)} &bull; ${inv.date}</div>
                </a>
            `;
        });
    }

    if (data.customers && data.customers.length > 0) {
        hasResults = true;
        html += '<div class="dropdown-header text-info fw-bold small mt-2"><i class="bi bi-person ms-1"></i>العملاء</div>';
        data.customers.forEach(c => {
            html += `
                <a href="/Customers/Details/${c.id}" class="dropdown-item py-2 border-bottom">
                    <div class="fw-bold">${escapeHtml(c.name)}</div>
                    <div class="text-muted small"><i class="bi bi-telephone ms-1"></i>${c.phoneNumber}</div>
                </a>
            `;
        });
    }

    if (hasResults) {
        container.innerHTML = html;
        container.classList.remove('d-none');
    } else {
        container.innerHTML = '<div class="p-3 text-center text-muted small">لم يتم العثور على أي نتائج مطابقة</div>';
        container.classList.remove('d-none');
    }
}

/* ==========================================================================
   4. HARDWARE BARCODE SCANNER DETECTOR (USB / Bluetooth Scanners)
   ========================================================================== */
function initHardwareScanner() {
    window.addEventListener('keydown', function (e) {
        if (!e || !e.key) return;
        if (!window.DoctorApp) {
            window.DoctorApp = { keystrokeBuffer: '', lastKeyTime: 0 };
        }
        if (typeof window.DoctorApp.keystrokeBuffer !== 'string') {
            window.DoctorApp.keystrokeBuffer = '';
        }
        const now = Date.now();
        // Hardware scanners type extremely fast (< 60ms between strokes)
        if (now - (window.DoctorApp.lastKeyTime || 0) > 75) {
            window.DoctorApp.keystrokeBuffer = '';
        }
        window.DoctorApp.lastKeyTime = now;

        if (e.key === 'Enter') {
            if (window.DoctorApp.keystrokeBuffer && window.DoctorApp.keystrokeBuffer.length >= 4) {
                const scannedBarcode = window.DoctorApp.keystrokeBuffer.trim();
                window.DoctorApp.keystrokeBuffer = '';

                // If on POS screen, automatically add to cart!
                if (window.handlePosBarcode) {
                    e.preventDefault();
                    playBeep();
                    window.handlePosBarcode(scannedBarcode);
                    return;
                }

                if (window.DoctorApp.onBarcodeScannedCallback) {
                    e.preventDefault();
                    playBeep();
                    window.DoctorApp.onBarcodeScannedCallback(scannedBarcode);
                    return;
                }

                // Fallback: Global search or product view
                const searchInput = document.getElementById('globalSearchInput');
                if (searchInput) {
                    searchInput.value = scannedBarcode;
                    searchInput.dispatchEvent(new Event('input'));
                }
            }
            window.DoctorApp.keystrokeBuffer = '';
        } else if (typeof e.key === 'string' && e.key.length === 1) {
            window.DoctorApp.keystrokeBuffer += e.key;
        }
    });
}

/* ==========================================================================
   5. MOBILE & WEB CAMERA SCANNER (Universal Camera Stream + Quagga2 Engine)
   ========================================================================== */
let activeCameraStream = null;
let currentCameraId = null;
let isScanningActive = false;
let frameScanInterval = null;
let isFrameProcessing = false;
let zxingCodeReader = null;
let lastDetectedCode = null;
let lastDetectedTime = 0;
let isTorchActive = false;

function openCameraScanner(callback) {
    window.DoctorApp.onBarcodeScannedCallback = callback;
    const modalEl = document.getElementById('cameraScannerModal');
    if (!modalEl) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    modal.show();

    setTimeout(() => {
        startScanner();
    }, 300);

    modalEl.addEventListener('hidden.bs.modal', function () {
        stopScanner();
    }, { once: true });
}

async function startScanner(deviceId = null) {
    stopScanner();
    const statusMsg = document.getElementById('scannerStatusMsg');
    const videoEl = document.getElementById('scannerVideo');

    if (!videoEl) return;

    if (statusMsg) {
        statusMsg.className = "alert alert-info py-1 px-2 small text-center mb-2";
        statusMsg.innerHTML = '<div class="spinner-border spinner-border-sm ms-1"></div> جاري تشغيل الكاميرا...';
    }

    let constraints = {
        audio: false,
        video: {
            facingMode: { ideal: "environment" },
            width: { ideal: 1280 },
            height: { ideal: 720 }
        }
    };

    if (deviceId) {
        constraints.video = {
            deviceId: { exact: deviceId },
            width: { ideal: 1280 },
            height: { ideal: 720 }
        };
    }

    try {
        let stream;
        try {
            stream = await navigator.mediaDevices.getUserMedia(constraints);
        } catch (e1) {
            console.warn('Initial camera constraints failed, trying basic video:', e1);
            stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
        }

        activeCameraStream = stream;
        videoEl.srcObject = stream;
        await videoEl.play();

        isScanningActive = true;
        if (statusMsg) {
            statusMsg.className = "alert alert-success py-1 px-2 small text-center mb-2";
            statusMsg.innerHTML = '<i class="bi bi-check2-circle ms-1"></i> الكاميرا نشطة، وجّه الباركود داخل الإطار';
        }

        checkTorchAvailability();
        loadCameraDevices(stream);
        startFrameScanLoop(videoEl);

    } catch (err) {
        console.error('Camera access error:', err);
        showCameraError(err);
    }
}

async function loadCameraDevices(stream) {
    const camSelect = document.getElementById('scannerCameraSelect');
    if (!camSelect || !navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return;

    try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        const videoDevs = devices.filter(d => d.kind === 'videoinput');

        if (videoDevs.length > 0) {
            camSelect.innerHTML = '';
            let currentTrack = stream ? stream.getVideoTracks()[0] : null;
            let currentTrackSettings = currentTrack && currentTrack.getSettings ? currentTrack.getSettings() : {};

            videoDevs.forEach((dev, idx) => {
                const opt = document.createElement('option');
                opt.value = dev.deviceId;
                let label = dev.label || `كاميرا ${idx + 1}`;
                const low = label.toLowerCase();
                if (low.includes('back') || low.includes('rear') || low.includes('environment') || low.includes('خلف')) {
                    label = `📷 الكاميرا الخلفية (${label})`;
                } else if (low.includes('front') || low.includes('user') || low.includes('أمام')) {
                    label = `🤳 الكاميرا الأمامية (${label})`;
                }
                opt.text = label;
                if (currentTrackSettings.deviceId && dev.deviceId === currentTrackSettings.deviceId) {
                    opt.selected = true;
                }
                camSelect.appendChild(opt);
            });
        }
    } catch (e) {
        console.warn('Device enumeration error:', e);
    }
}

function onScannerCameraChange(deviceId) {
    if (deviceId) {
        currentCameraId = deviceId;
        startScanner(deviceId);
    }
}

function restartScanner() {
    startScanner(currentCameraId);
}

function startFrameScanLoop(videoEl) {
    if (frameScanInterval) clearInterval(frameScanInterval);

    let canvas = document.getElementById('scannerFrameCanvas');
    if (!canvas) {
        canvas = document.createElement('canvas');
        canvas.id = 'scannerFrameCanvas';
        canvas.style.display = 'none';
        document.body.appendChild(canvas);
    }
    const ctx = canvas.getContext('2d', { willReadFrequently: true });

    frameScanInterval = setInterval(async () => {
        if (!isScanningActive || isFrameProcessing || videoEl.paused || videoEl.ended || videoEl.readyState < 2) {
            return;
        }

        isFrameProcessing = true;

        try {
            const vw = videoEl.videoWidth || 640;
            const vh = videoEl.videoHeight || 480;
            const scale = Math.min(1, 800 / Math.max(vw, vh));
            const tw = Math.round(vw * scale);
            const th = Math.round(vh * scale);

            canvas.width = tw;
            canvas.height = th;
            ctx.drawImage(videoEl, 0, 0, tw, th);

            const frameDataUrl = canvas.toDataURL('image/jpeg', 0.8);

            // Pass 1: Quagga (Best for 1D barcodes like EAN-13, EAN-8, Code 128)
            if (typeof Quagga !== 'undefined') {
                const code = await new Promise(resolve => {
                    Quagga.decodeSingle({
                        src: frameDataUrl,
                        numOfWorkers: 0,
                        inputStream: { size: 800 },
                        decoder: {
                            readers: ["ean_reader", "ean_8_reader", "code_128_reader", "code_39_reader", "upc_reader"]
                        },
                        locator: { patchSize: "medium", halfSample: true },
                        locate: true
                    }, res => {
                        if (res && res.codeResult && res.codeResult.code) {
                            resolve(res.codeResult.code);
                        } else {
                            resolve(null);
                        }
                    });
                });

                if (code && isScanningActive) {
                    onCodeDetected(code);
                    isFrameProcessing = false;
                    return;
                }
            }

            // Pass 2: ZXing (QR & 2D fallback)
            ensureZxingReader();
            if (zxingCodeReader) {
                try {
                    const zxingRes = await zxingCodeReader.decodeFromImageUrl(frameDataUrl);
                    if (zxingRes && zxingRes.getText() && isScanningActive) {
                        onCodeDetected(zxingRes.getText());
                        isFrameProcessing = false;
                        return;
                    }
                } catch (e) {}
            }

            // Pass 3: Native BarcodeDetector (Chrome Android)
            if ('BarcodeDetector' in window) {
                try {
                    const detector = new BarcodeDetector();
                    const codes = await detector.detect(videoEl);
                    if (codes && codes.length > 0 && codes[0].rawValue && isScanningActive) {
                        onCodeDetected(codes[0].rawValue);
                        isFrameProcessing = false;
                        return;
                    }
                } catch (e) {}
            }

        } catch (err) {
        } finally {
            isFrameProcessing = false;
        }
    }, 180);
}

function onCodeDetected(code) {
    if (!code) return;
    const clean = code.trim();
    if (clean.length < 4) return;

    const now = Date.now();
    if (clean === lastDetectedCode && (now - lastDetectedTime) < 1500) {
        return;
    }
    lastDetectedCode = clean;
    lastDetectedTime = now;

    handleScanSuccess(clean);
}

function stopScanner() {
    isScanningActive = false;
    isFrameProcessing = false;
    isTorchActive = false;

    if (frameScanInterval) {
        clearInterval(frameScanInterval);
        frameScanInterval = null;
    }

    const torchBtn = document.getElementById('scannerTorchBtn');
    if (torchBtn) torchBtn.classList.add('d-none');

    const videoEl = document.getElementById('scannerVideo');
    if (videoEl) {
        videoEl.pause();
        videoEl.srcObject = null;
    }

    if (activeCameraStream) {
        try {
            activeCameraStream.getTracks().forEach(t => t.stop());
        } catch (e) {}
        activeCameraStream = null;
    }
}

function ensureZxingReader() {
    if (!zxingCodeReader && typeof ZXing !== 'undefined') {
        const hints = new Map();
        hints.set(ZXing.DecodeHintType.POSSIBLE_FORMATS, [
            ZXing.BarcodeFormat.QR_CODE,
            ZXing.BarcodeFormat.DATA_MATRIX,
            ZXing.BarcodeFormat.EAN_13,
            ZXing.BarcodeFormat.EAN_8,
            ZXing.BarcodeFormat.CODE_128,
            ZXing.BarcodeFormat.CODE_39,
            ZXing.BarcodeFormat.UPC_A
        ]);
        hints.set(ZXing.DecodeHintType.TRY_HARDER, true);
        zxingCodeReader = new ZXing.BrowserMultiFormatReader(hints);
    }
}

function showCameraError(err) {
    const statusMsg = document.getElementById('scannerStatusMsg');
    if (statusMsg) {
        statusMsg.className = "alert alert-warning py-2 px-2 small text-center mb-2";
        statusMsg.innerHTML = `<div><strong>تعذر فتح الكاميرا مباشرة</strong></div><div class="small text-muted mt-1">تأكد من السماح بالوصول للكاميرا من إعدادات المتصفح، أو اختر صورة من المعرض أدناه.</div>`;
    }
}

function handleScanSuccess(decodedText) {
    if (!decodedText) return;
    const cleanCode = decodedText.trim();
    if (!cleanCode) return;

    playBeep();
    if (navigator.vibrate) {
        try { navigator.vibrate([80, 40, 80]); } catch (e) {}
    }

    stopScanner();

    const modalEl = document.getElementById('cameraScannerModal');
    if (modalEl) {
        const modal = bootstrap.Modal.getInstance(modalEl);
        if (modal) modal.hide();
    }

    showToast(`تم مسح الباركود بنجاح: ${cleanCode}`, 'success');

    if (window.DoctorApp.onBarcodeScannedCallback) {
        window.DoctorApp.onBarcodeScannedCallback(cleanCode);
    }
}

function checkTorchAvailability() {
    const torchBtn = document.getElementById('scannerTorchBtn');
    if (!torchBtn || !activeCameraStream) return;

    try {
        const track = activeCameraStream.getVideoTracks()[0];
        if (track && typeof track.getCapabilities === 'function') {
            const caps = track.getCapabilities();
            if (caps && caps.torch) {
                torchBtn.classList.remove('d-none');
                return;
            }
        }
        torchBtn.classList.add('d-none');
    } catch (e) {
        torchBtn.classList.add('d-none');
    }
}

function toggleScannerTorch() {
    if (!activeCameraStream) return;
    try {
        const track = activeCameraStream.getVideoTracks()[0];
        if (track && typeof track.applyConstraints === 'function') {
            isTorchActive = !isTorchActive;
            track.applyConstraints({
                advanced: [{ torch: isTorchActive }]
            });
            const torchBtn = document.getElementById('scannerTorchBtn');
            if (torchBtn) {
                if (isTorchActive) {
                    torchBtn.classList.remove('btn-outline-warning');
                    torchBtn.classList.add('btn-warning');
                } else {
                    torchBtn.classList.remove('btn-warning');
                    torchBtn.classList.add('btn-outline-warning');
                }
            }
        }
    } catch (e) {
        console.warn('Torch toggle error:', e);
    }
}

async function handleImageFileScan(inputEl) {
    if (!inputEl || !inputEl.files || !inputEl.files[0]) return;
    const file = inputEl.files[0];
    const statusMsg = document.getElementById('scannerStatusMsg');
    if (statusMsg) {
        statusMsg.className = "alert alert-info py-1 px-2 small text-center mb-2";
        statusMsg.innerHTML = '<span class="spinner-border spinner-border-sm ms-1"></span> جاري فحص الصورة وقراءة الباركود...';
    }

    try {
        const res = await decodeBarcodeFromFile(file);
        if (res && res.success && res.code) {
            handleScanSuccess(res.code);
            inputEl.value = '';
        } else {
            if (statusMsg) {
                statusMsg.className = "alert alert-danger py-1 px-2 small text-center mb-2";
                statusMsg.innerHTML = '<i class="bi bi-x-circle ms-1"></i> لم يتم العثور على باركود واضح في هذه الصورة.';
            }
            inputEl.value = '';
        }
    } catch (e) {
        if (statusMsg) {
            statusMsg.className = "alert alert-danger py-1 px-2 small text-center mb-2";
            statusMsg.innerHTML = '<i class="bi bi-x-circle ms-1"></i> تعذر فحص الصورة: ' + (e.message || '');
        }
        inputEl.value = '';
    }
}

async function decodeBarcodeFromFile(file) {
    let img;
    try {
        img = await createImageBitmap(file);
    } catch (e) {
        img = await new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => {
                const i = new Image();
                i.onload = () => resolve(i);
                i.onerror = reject;
                i.src = reader.result;
            };
            reader.onerror = reject;
            reader.readAsDataURL(file);
        });
    }

    const angles = [0, 90, 270];
    for (const angle of angles) {
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        const maxDim = 1200;
        let scale = 1;
        if (img.width > maxDim || img.height > maxDim) {
            scale = maxDim / Math.max(img.width, img.height);
        }
        const targetW = Math.round(img.width * scale);
        const targetH = Math.round(img.height * scale);

        if (angle === 90 || angle === 270) {
            canvas.width = targetH;
            canvas.height = targetW;
        } else {
            canvas.width = targetW;
            canvas.height = targetH;
        }

        ctx.save();
        if (angle === 90) {
            ctx.translate(canvas.width, 0);
            ctx.rotate(Math.PI / 2);
        } else if (angle === 270) {
            ctx.translate(0, canvas.height);
            ctx.rotate(-Math.PI / 2);
        }
        ctx.drawImage(img, 0, 0, targetW, targetH);
        ctx.restore();

        const dataUrl = canvas.toDataURL('image/jpeg', 0.9);

        // Pass 1: Quagga2
        if (typeof Quagga !== 'undefined') {
            const code = await new Promise(resolve => {
                Quagga.decodeSingle({
                    src: dataUrl,
                    numOfWorkers: 0,
                    inputStream: { size: 1000 },
                    decoder: {
                        readers: ["ean_reader", "ean_8_reader", "code_128_reader", "code_39_reader", "upc_reader"]
                    },
                    locator: { patchSize: "medium", halfSample: true },
                    locate: true
                }, res => {
                    if (res && res.codeResult && res.codeResult.code) {
                        resolve(res.codeResult.code);
                    } else {
                        resolve(null);
                    }
                });
            });

            if (code) {
                return { success: true, code: code, engine: 'Quagga2' };
            }
        }

        // Pass 2: ZXing
        ensureZxingReader();
        if (zxingCodeReader) {
            try {
                const zxRes = await zxingCodeReader.decodeFromImageUrl(dataUrl);
                if (zxRes && zxRes.getText()) {
                    return { success: true, code: zxRes.getText(), engine: 'ZXing' };
                }
            } catch (e) {}
        }

        // Pass 3: Native BarcodeDetector
        if ('BarcodeDetector' in window) {
            try {
                const detector = new BarcodeDetector();
                const codes = await detector.detect(canvas);
                if (codes && codes.length > 0 && codes[0].rawValue) {
                    return { success: true, code: codes[0].rawValue, engine: 'Native' };
                }
            } catch (e) {}
        }
    }

    return { success: false };
}

function submitManualScan() {
    const input = document.getElementById('scannerManualInput');
    if (!input) return;
    const val = input.value.trim();
    if (!val) {
        showToast('يرجى إدخال رقم الباركود أولاً', 'warning');
        return;
    }
    handleScanSuccess(val);
    input.value = '';
}

// Audio feedback on successful scan
function playBeep() {
    try {
        const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = audioCtx.createOscillator();
        const gain = audioCtx.createGain();
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        osc.type = 'sine';
        osc.frequency.value = 920; // 920 Hz pleasant beep
        gain.gain.setValueAtTime(0.18, audioCtx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + 0.12);
        osc.start();
        osc.stop(audioCtx.currentTime + 0.12);
    } catch (e) {
        // ignore audio errors if blocked
    }
}

/* ==========================================================================
   6. TOAST NOTIFICATIONS HELPER
   ========================================================================== */
function showToast(message, type = 'info') {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    let bgClass = 'bg-primary text-white';
    let icon = 'bi-info-circle-fill';

    if (type === 'success') { bgClass = 'bg-success text-white'; icon = 'bi-check-circle-fill'; }
    if (type === 'danger' || type === 'error') { bgClass = 'bg-danger text-white'; icon = 'bi-exclamation-triangle-fill'; }
    if (type === 'warning') { bgClass = 'bg-warning text-dark'; icon = 'bi-exclamation-circle-fill'; }

    const toastId = 'toast_' + Date.now();
    const html = `
        <div id="${toastId}" class="toast align-items-center ${bgClass} border-0 shadow-lg mb-2" role="alert" aria-live="assertive" aria-atomic="true">
            <div class="d-flex">
                <div class="toast-body d-flex align-items-center">
                    <i class="bi ${icon} fs-5 ms-2"></i>
                    <span>${escapeHtml(message)}</span>
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
        </div>
    `;

    container.insertAdjacentHTML('beforeend', html);
    const toastEl = document.getElementById(toastId);
    const bsToast = new bootstrap.Toast(toastEl, { delay: 3500 });
    bsToast.show();

    toastEl.addEventListener('hidden.bs.toast', function () {
        toastEl.remove();
    });
}

function escapeHtml(text) {
    if (!text) return '';
    return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
}
