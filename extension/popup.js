// Bluetooth Battery Ring - popup logic.
// Shows the cached status from chrome.storage instantly (no re-fetch on open),
// then a background refresh updates it live.
const CIRC = 2 * Math.PI * 80; // r = 80 in the SVG
const GREEN = '#22c55e';
const AMBER = '#f59e0b';
const RED = '#ef4444';
const GRAY = '#8e8e93';

function t(key) {
  try { return chrome.i18n.getMessage(key) || key; } catch (e) { return key; }
}
function colorFor(pct, connected) {
  if (pct == null) return GRAY;
  if (connected === false) return GRAY;
  if (pct < 20) return RED;
  if (pct < 50) return AMBER;
  return GREEN;
}
function setText(id, v) {
  const el = document.getElementById(id);
  if (el) el.textContent = v;
}
function paint(pct, connected) {
  const prog = document.getElementById('prog');
  const num = document.getElementById('num');
  const p = pct == null ? 0 : Math.max(0, Math.min(100, pct));
  prog.style.strokeDasharray = String(CIRC);
  prog.style.strokeDashoffset = String(CIRC * (1 - p / 100));
  const color = colorFor(pct, connected);
  prog.style.stroke = color;
  num.style.color = color;
  num.textContent = pct == null ? '--' : String(pct);
}
function render(s) {
  if (!s) { paint(null, true); setText('status', t('reading')); return; }
  setText('model', s.device || t('modelUnknown'));
  const connected = s.connected !== false;
  if (s.ok && s.percent != null) {
    paint(s.percent, connected);
    setText('status', connected ? t('connected') : t('disconnected'));
  } else if (s.ok) {
    paint(null, connected);
    setText('status', s.note || t('notConnected'));
  } else {
    paint(null, false);
    setText('status', t('readFailed') + ': ' + (s.error || t('unknown')));
  }
}

document.title = t('extName');
const btn = document.getElementById('refresh');
btn.textContent = t('refresh');
btn.addEventListener('click', () => chrome.runtime.sendMessage({ type: 'refresh', force: true }));

// live-update when the background writes a new value
chrome.storage.onChanged.addListener((ch, area) => {
  if (area === 'local' && ch.status) render(ch.status.newValue);
});

// instant display from cache, then ask the background to refresh if stale
chrome.storage.local.get('status', (o) => render(o && o.status));
chrome.runtime.sendMessage({ type: 'refresh' });
