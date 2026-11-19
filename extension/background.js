// Bluetooth Battery Ring - background service worker.
// Polls the native host and caches the latest status in chrome.storage.local so
// the popup can show it instantly (no re-fetch on open). Also paints the ring
// toolbar icon (green / red < 20%) and shows the % on hover.
const HOST = 'com.shokz.battery';
const STALE_MS = 15000;

function colorFor(pct, connected) {
  if (pct == null) return '#8e8e93';
  if (connected === false) return '#8e8e93';
  if (pct < 20) return '#ef4444';
  if (pct < 50) return '#f59e0b';
  return '#22c55e';
}

async function drawRing(pct, connected) {
  const size = 32, radius = size / 2 - 3.5, cx = size / 2, cy = size / 2;
  const color = colorFor(pct, connected);
  const p = pct == null ? 0 : Math.max(0, Math.min(100, pct));
  const canvas = new OffscreenCanvas(size, size);
  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, size, size);
  ctx.beginPath(); ctx.arc(cx, cy, radius, 0, Math.PI * 2); ctx.lineWidth = 4;
  ctx.strokeStyle = 'rgba(128,128,128,0.35)'; ctx.stroke();
  if (p > 0) {
    ctx.beginPath(); ctx.arc(cx, cy, radius, -Math.PI / 2, -Math.PI / 2 + (Math.PI * 2 * p) / 100);
    ctx.lineWidth = 4; ctx.lineCap = 'round'; ctx.strokeStyle = color; ctx.stroke();
  }
  const imageData = ctx.getImageData(0, 0, size, size);
  try { await chrome.action.setIcon({ imageData: { 32: imageData } }); } catch (e) {}
  try { await chrome.action.setBadgeText({ text: '' }); } catch (e) {}
  let label = 'Bluetooth Battery';
  try { label = chrome.i18n.getMessage('batteryLabel') || label; } catch (e) {}
  let dis = 'disconnected';
  try { dis = chrome.i18n.getMessage('disconnected') || dis; } catch (e) {}
  let title;
  if (pct == null) title = label + ' \u2014';
  else if (connected === false) title = label + ' ' + pct + '% (' + dis + ')';
  else title = label + ' ' + pct + '%';
  try { await chrome.action.setTitle({ title }); } catch (e) {}
}

function refresh(force) {
  chrome.storage.local.get('status', (cachedObj) => {
    const cached = cachedObj && cachedObj.status;
    const last = cached && cached.ts ? cached.ts : 0;
    if (!force && Date.now() - last < STALE_MS) return; // fresh enough; keep cache
    chrome.runtime.sendNativeMessage(HOST, { cmd: 'get' }, (resp) => {
      let s;
      if (chrome.runtime.lastError || !resp) {
        s = { ok: false, error: (chrome.runtime.lastError && chrome.runtime.lastError.message) || 'no response', ts: Date.now() };
      } else {
        s = { ok: resp.ok !== false, percent: resp.percent, device: resp.device, connected: resp.connected, note: resp.note, ts: Date.now() };
      }
      chrome.storage.local.set({ status: s });
      drawRing(s.ok ? s.percent : null, s.ok ? s.connected : false);
    });
  });
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create('refresh', { periodInMinutes: 1 });
  refresh(true);
});
chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create('refresh', { periodInMinutes: 1 });
  refresh(true);
});
chrome.alarms.onAlarm.addListener((a) => { if (a.name === 'refresh') refresh(true); });
chrome.runtime.onMessage.addListener((m) => {
  if (m && m.type === 'refresh') refresh(m.force === true);
});

// Populate the cache as soon as the worker starts (e.g. right after a reload),
// so the first popup open already has data to show instantly.
refresh(true);
