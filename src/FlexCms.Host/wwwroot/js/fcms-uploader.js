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

  async function compressIfImage(file) {
    if (!file.type.startsWith('image/')) return file;
    if (file.size < COMPRESS_SKIP_BELOW) return file;
    // GIF compression usually breaks animation; keep as-is.
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
          // Use original name with .jpg extension if compressed
          const baseName = file.name.replace(/\.[^/.]+$/, '');
          const compressed = new File([blob], baseName + '.jpg', { type: 'image/jpeg' });
          resolve(compressed);
        }, 'image/jpeg', COMPRESS_QUALITY);
      };
      img.onerror = () => resolve(file);
      img.src = URL.createObjectURL(file);
    });
  }

  function fmtKb(n) {
    return (n / 1024).toFixed(1) + ' KB';
  }

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

  async function startCamera(root) {
    const stage = root.querySelector('.fcms-uploader-camera-stage');
    const video = root.querySelector('.fcms-uploader-camera-video');
    if (!stage || !video) return;

    // Modern browsers with getUserMedia → live preview + canvas capture.
    if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: 'environment' },
          audio: false
        });
        video.srcObject = stream;
        stage.classList.remove('d-none');
        root._fcmsCamStream = stream;
        return;
      } catch (e) {
        console.warn('getUserMedia denied, falling back to file input', e);
      }
    }
    // Fallback — trigger the native file picker with `capture` attribute.
    const fallback = root.querySelector('.fcms-uploader-camera-input');
    if (fallback) fallback.click();
  }

  function stopCamera(root) {
    const stage = root.querySelector('.fcms-uploader-camera-stage');
    const video = root.querySelector('.fcms-uploader-camera-video');
    if (root._fcmsCamStream) {
      root._fcmsCamStream.getTracks().forEach(t => t.stop());
      root._fcmsCamStream = null;
    }
    if (video) video.srcObject = null;
    if (stage) stage.classList.add('d-none');
  }

  async function snapCamera(root) {
    const video = root.querySelector('.fcms-uploader-camera-video');
    if (!video || !video.videoWidth) return;

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(video, 0, 0);
    canvas.toBlob(async (blob) => {
      if (!blob) return;
      const file = new File([blob], 'capture-' + Date.now() + '.jpg', { type: 'image/jpeg' });
      stopCamera(root);
      await uploadOne(root, file);
    }, 'image/jpeg', COMPRESS_QUALITY);
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

    const camBtn = root.querySelector('.fcms-uploader-camera-btn');
    const camSnap = root.querySelector('.fcms-uploader-camera-snap');
    const camCancel = root.querySelector('.fcms-uploader-camera-cancel');
    const camFallback = root.querySelector('.fcms-uploader-camera-input');
    if (camBtn) camBtn.addEventListener('click', () => startCamera(root));
    if (camSnap) camSnap.addEventListener('click', () => snapCamera(root));
    if (camCancel) camCancel.addEventListener('click', () => stopCamera(root));
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
  }

  function init() {
    document.querySelectorAll('.fcms-uploader').forEach(wire);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  window.fcmsUploader = { init: init };
})();
