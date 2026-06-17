// fcms-uploader.js — drag-drop, camera capture, client-side compression.
// Module's call site:
//   <div class="fcms-uploader" data-upload-url="..." data-multi="1" ...>
//     <input class="fcms-uploader-input" type="file">
//     <div class="fcms-uploader-list"></div>
//   </div>
// Each successful upload POSTs the file alone to data-upload-url with
// form field name "file" + the antiforgery header. Server returns
// { isSuccess, id, url, fileName, size }.
(function () {
  'use strict';

  const COMPRESS_MAX_DIM = 1920;
  const COMPRESS_QUALITY = 0.85;
  const COMPRESS_SKIP_BELOW = 500 * 1024;

  function getCsrf() {
    const t = document.querySelector('meta[name="csrf-token"]');
    return t ? t.getAttribute('content') : '';
  }

  function toast(msg, type) {
    type = type || 'success';
    if (window.fcms && fcms.toast && typeof fcms.toast[type] === 'function') {
      fcms.toast[type](msg);
    } else {
      console.log('[fcms-uploader]', type, msg);
    }
  }

  // ── Camera capability detection ─────────────────────────────────────
  // Phones, tablets, laptops with webcam → getUserMedia works.
  // Desktops with no webcam, locked-down browsers, FB/IG in-app browsers,
  // and any non-HTTPS page → fall back to the hidden <input capture>
  // file picker (mobile shows the camera, desktop shows file dialog).
  const cameraCapability = (function () {
    const hasApi = !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    const isSecure = window.isSecureContext === true
      || location.hostname === 'localhost'
      || location.hostname === '127.0.0.1';
    return { hasApi, isSecure, supported: hasApi && isSecure };
  })();

  async function enumerateVideoInputs() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return [];
    try {
      const devices = await navigator.mediaDevices.enumerateDevices();
      return devices.filter(d => d.kind === 'videoinput');
    } catch (_) { return []; }
  }

  async function compressIfImage(file) {
    if (!file.type.startsWith('image/')) return file;
    if (file.size < COMPRESS_SKIP_BELOW) return file;
    if (file.type === 'image/gif') return file;

    return new Promise((resolve) => {
      const img = new Image();
      img.onload = () => {
        const ratio = Math.min(COMPRESS_MAX_DIM / img.width, COMPRESS_MAX_DIM / img.height, 1);
        const w = Math.round(img.width * ratio);
        const h = Math.round(img.height * ratio);
        const canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(img, 0, 0, w, h);
        canvas.toBlob((blob) => {
          if (!blob) { resolve(file); return; }
          const baseName = file.name.replace(/\.[^/.]+$/, '');
          const compressed = new File([blob], baseName + '.jpg', { type: 'image/jpeg' });
          resolve(compressed);
        }, 'image/jpeg', COMPRESS_QUALITY);
      };
      img.onerror = () => resolve(file);
      img.src = URL.createObjectURL(file);
    });
  }

  function fmtKb(n) { return (n / 1024).toFixed(1) + ' KB'; }

  function rowEl(file, item) {
    const div = document.createElement('div');
    div.className = 'list-group-item d-flex justify-content-between align-items-center';
    div.setAttribute('data-fcms-uploader-row', item.id);
    div.innerHTML =
      '<div><a href="' + item.url + '" target="_blank" class="text-decoration-none">' +
      escapeHtml(item.fileName || file.name) + '</a>' +
      '<span class="text-muted ms-2">' + fmtKb(item.size || file.size) + '</span></div>' +
      '<button type="button" class="btn btn-sm btn-outline-danger fcms-uploader-remove" data-id="' + item.id + '">' +
      '<i class="bi bi-x-lg"></i></button>';
    return div;
  }

  function progressEl(name) {
    const div = document.createElement('div');
    div.className = 'list-group-item fcms-uploader-progress';
    div.innerHTML =
      '<div class="d-flex justify-content-between"><span>' + escapeHtml(name) + '</span><span class="text-muted small">uploading…</span></div>' +
      '<div class="progress mt-1" style="height:4px"><div class="progress-bar progress-bar-striped progress-bar-animated" style="width:100%"></div></div>';
    return div;
  }

  function escapeHtml(s) {
    return (s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  async function uploadOne(root, file) {
    const list = root.querySelector('.fcms-uploader-list');
    const progress = progressEl(file.name);
    list.appendChild(progress);

    try {
      const compress = root.getAttribute('data-compress') === '1';
      const finalFile = compress ? await compressIfImage(file) : file;

      const fd = new FormData();
      fd.append('file', finalFile, finalFile.name);

      const url = root.getAttribute('data-upload-url');
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'X-FlexCms-Csrf': getCsrf() },
        body: fd
      });
      const json = await res.json().catch(() => ({}));

      if (!res.ok || !json.isSuccess) {
        progress.remove();
        toast(json.message || ('Upload failed: ' + file.name), 'error');
        return;
      }
      progress.remove();
      list.appendChild(rowEl(file, json));
    } catch (e) {
      console.error(e);
      progress.remove();
      toast('Upload error: ' + file.name, 'error');
    }
  }

  async function handleFiles(root, fileList) {
    if (!fileList || fileList.length === 0) return;
    const maxMb = parseFloat(root.getAttribute('data-max-size-mb') || '10');
    const maxCount = parseInt(root.getAttribute('data-max-count') || '20', 10);
    const multi = root.getAttribute('data-multi') === '1';

    const existing = root.querySelectorAll('[data-fcms-uploader-row]').length;
    let files = Array.from(fileList);
    if (!multi) files = files.slice(0, 1);

    if (existing + files.length > maxCount) {
      toast('Maximum ' + maxCount + ' files allowed.', 'error');
      return;
    }
    for (const f of files) {
      if (f.size > maxMb * 1024 * 1024) {
        toast(f.name + ' exceeds ' + maxMb + ' MB.', 'error');
        continue;
      }
      await uploadOne(root, f);
    }
  }

  async function removeFile(root, id, rowEl) {
    const tpl = root.getAttribute('data-delete-url-tpl');
    if (!tpl) { rowEl.remove(); return; }
    const url = tpl.replace('{id}', id);
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'X-FlexCms-Csrf': getCsrf() }
      });
      const json = await res.json().catch(() => ({ isSuccess: res.ok }));
      if (json.isSuccess) {
        rowEl.remove();
        toast('Removed.', 'success');
      } else {
        toast(json.message || 'Delete failed.', 'error');
      }
    } catch (e) {
      console.error(e);
      toast('Delete error.', 'error');
    }
  }

  // ── Live camera (getUserMedia) ──────────────────────────────────────
  // Per uploader root we track:
  //   _fcmsCamStream   active MediaStream
  //   _fcmsCamFacing   'environment' | 'user'  (current active camera)
  //   _fcmsCamDevices  array of MediaDeviceInfo (videoinput)

  function setVideoMirror(video, isFront) {
    // Selfie cam looks natural mirrored — back cam should NOT mirror.
    video.style.transform = isFront ? 'scaleX(-1)' : 'none';
  }

  function nativeFallback(root) {
    const fallback = root.querySelector('.fcms-uploader-camera-input');
    if (fallback) fallback.click();
  }

  async function tryStream(facingMode) {
    // Tries the exact facing mode first, then degrades to a soft preference,
    // then to "any camera". Returns the MediaStream + the actual facing.
    const attempts = [
      { video: { facingMode: { exact: facingMode } }, audio: false },
      { video: { facingMode: facingMode }, audio: false },
      { video: true, audio: false },
    ];
    for (const constraints of attempts) {
      try {
        const stream = await navigator.mediaDevices.getUserMedia(constraints);
        return { stream, facing: facingMode };
      } catch (_) { /* try next */ }
    }
    return null;
  }

  async function startCamera(root, facing) {
    facing = facing || 'environment';
    const stage = root.querySelector('.fcms-uploader-camera-stage');
    const video = root.querySelector('.fcms-uploader-camera-video');
    if (!stage || !video) return;

    if (!cameraCapability.supported) {
      if (!cameraCapability.isSecure) {
        toast('Camera needs HTTPS. Using file picker instead.', 'info');
      }
      nativeFallback(root);
      return;
    }

    // Stop any existing stream before starting a new one.
    if (root._fcmsCamStream) stopCamera(root);

    let attempt = await tryStream(facing);
    if (!attempt && facing === 'environment') {
      attempt = await tryStream('user'); // laptops / front-cam only devices
    }

    if (!attempt) {
      toast('Could not start camera. Falling back to file picker.', 'info');
      nativeFallback(root);
      return;
    }

    video.srcObject = attempt.stream;
    setVideoMirror(video, attempt.facing === 'user');
    root._fcmsCamStream = attempt.stream;
    root._fcmsCamFacing = attempt.facing;
    stage.classList.remove('d-none');

    // Show/hide flip button based on whether a second camera exists.
    const devices = await enumerateVideoInputs();
    root._fcmsCamDevices = devices;
    const flipBtn = root.querySelector('.fcms-uploader-camera-flip');
    if (flipBtn) flipBtn.classList.toggle('d-none', devices.length < 2);
  }

  function stopCamera(root) {
    const stage = root.querySelector('.fcms-uploader-camera-stage');
    const video = root.querySelector('.fcms-uploader-camera-video');
    if (root._fcmsCamStream) {
      root._fcmsCamStream.getTracks().forEach(t => t.stop());
      root._fcmsCamStream = null;
    }
    if (video) { video.srcObject = null; video.style.transform = 'none'; }
    if (stage) stage.classList.add('d-none');
  }

  async function flipCamera(root) {
    const next = root._fcmsCamFacing === 'user' ? 'environment' : 'user';
    await startCamera(root, next);
  }

  async function snapCamera(root) {
    const video = root.querySelector('.fcms-uploader-camera-video');
    if (!video || !video.videoWidth) return;

    const multi = root.getAttribute('data-multi') === '1';
    const maxCount = parseInt(root.getAttribute('data-max-count') || '20', 10);
    const existing = root.querySelectorAll('[data-fcms-uploader-row]').length;

    // Single-mode guard: if the slot is already taken, refuse the capture
    // instead of silently overwriting. User has to remove the existing
    // file first.
    if (!multi && existing >= 1) {
      toast('Only one file allowed. Remove the existing one first.', 'error');
      return;
    }
    if (existing >= maxCount) {
      toast('Maximum ' + maxCount + ' files reached.', 'error');
      return;
    }

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d');
    // For a mirrored front-cam preview, the captured photo SHOULD still
    // be un-mirrored so the saved image matches reality (text reads correctly).
    ctx.drawImage(video, 0, 0);

    // Single mode: close the camera right after the snap. Multi mode: keep
    // the preview running so the user can shoot the next page / document /
    // photo in one session without re-clicking the Camera button each time.
    canvas.toBlob(async (blob) => {
      if (!blob) return;
      const file = new File([blob], 'capture-' + Date.now() + '.jpg', { type: 'image/jpeg' });
      if (!multi) stopCamera(root);
      await uploadOne(root, file);
      if (multi) flashCaptureFeedback(root);
    }, 'image/jpeg', COMPRESS_QUALITY);
  }

  // Brief visual flash on the video element so the user sees the snap
  // actually happened when the camera stage stays open in multi mode.
  function flashCaptureFeedback(root) {
    const video = root.querySelector('.fcms-uploader-camera-video');
    if (!video) return;
    video.style.outline = '4px solid #198754';
    setTimeout(() => { video.style.outline = ''; }, 200);
  }

  function setupCameraButtons(root) {
    const camBtn = root.querySelector('.fcms-uploader-camera-btn');
    if (!camBtn) return;

    // No live camera support? Still keep the button — clicking it triggers
    // the native <input capture>. On phones that opens the system camera;
    // on desktops it opens the file picker. Better than hiding the button
    // and confusing users who expected to see it.
    if (!cameraCapability.supported) {
      camBtn.title = cameraCapability.isSecure
        ? 'Live preview unavailable — opens file picker / system camera'
        : 'Live preview needs HTTPS — opens file picker / system camera';
    }
  }

  function wire(root) {
    if (root._fcmsUploaderBound) return;
    root._fcmsUploaderBound = true;

    const dropzone = root.querySelector('.fcms-uploader-dropzone');
    const input = root.querySelector('.fcms-uploader-input');

    if (dropzone && input) {
      dropzone.addEventListener('click', () => input.click());
      dropzone.addEventListener('dragover', (e) => { e.preventDefault(); dropzone.classList.add('border-primary'); });
      dropzone.addEventListener('dragleave', () => dropzone.classList.remove('border-primary'));
      dropzone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropzone.classList.remove('border-primary');
        handleFiles(root, e.dataTransfer.files);
      });
      input.addEventListener('change', (e) => {
        handleFiles(root, e.target.files);
        e.target.value = '';
      });
    }

    setupCameraButtons(root);
    const camBtn = root.querySelector('.fcms-uploader-camera-btn');
    const camSnap = root.querySelector('.fcms-uploader-camera-snap');
    const camCancel = root.querySelector('.fcms-uploader-camera-cancel');
    const camFlip = root.querySelector('.fcms-uploader-camera-flip');
    const camFallback = root.querySelector('.fcms-uploader-camera-input');

    if (camBtn) camBtn.addEventListener('click', () => startCamera(root, 'environment'));
    if (camSnap) camSnap.addEventListener('click', () => snapCamera(root));
    if (camCancel) camCancel.addEventListener('click', () => stopCamera(root));
    if (camFlip) camFlip.addEventListener('click', () => flipCamera(root));
    if (camFallback) camFallback.addEventListener('change', (e) => {
      handleFiles(root, e.target.files);
      e.target.value = '';
    });

    root.addEventListener('click', (e) => {
      const rmBtn = e.target.closest('.fcms-uploader-remove');
      if (rmBtn) {
        const id = rmBtn.getAttribute('data-id');
        const row = rmBtn.closest('[data-fcms-uploader-row]');
        if (id && row) removeFile(root, id, row);
      }
    });

    // Stop the stream if the user navigates away or hides the tab.
    window.addEventListener('pagehide', () => stopCamera(root));
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) stopCamera(root);
    });
  }

  function init() {
    document.querySelectorAll('.fcms-uploader').forEach(wire);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  window.fcmsUploader = { init: init, _capability: cameraCapability };
})();
