const canvas = document.getElementById("radar");
const ctx = canvas.getContext("2d", { alpha: false });

let dpr = window.devicePixelRatio || 1;
let viewportW = 0;
let viewportH = 0;

let radarData = null;
let ws = null;
let wsRetryMs = 500;
let wsReconnectTimer = null;

const WS_RETRY_MAX_MS = 5000;
const imageCache = new Map();
const svgMetaCache = new Map();

const BASE_MARKER_SCALE = 0.3;
let markerScaleMultiplier = 1.0;
let autoFollowReference = false;
let uiAimLineLength = 1500;
let uiNonHumanAimLineLength = 1500;
let uiAimLineInitialized = false;
let selectedReferenceKey = "";
let selectedReferencePlayer = null;
let referencePlayers = [];
let referenceSignature = "";

const camera = {
  zoomPercent: 100,
  minZoomPercent: 1,
  maxZoomPercent: 200,
  centerX: 0,
  centerY: 0,
  initialized: false,
  dragging: false,
  lastX: 0,
  lastY: 0,
  mapUnitsW: 0,
  mapUnitsH: 0,
  mapKey: "",
  currentParams: null
};

function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}

function resizeCanvas() {
  dpr = window.devicePixelRatio || 1;
  viewportW = Math.max(1, window.innerWidth);
  viewportH = Math.max(1, window.innerHeight);

  const pixelW = Math.max(1, Math.round(viewportW * dpr));
  const pixelH = Math.max(1, Math.round(viewportH * dpr));

  if (canvas.width !== pixelW) canvas.width = pixelW;
  if (canvas.height !== pixelH) canvas.height = pixelH;

  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function withAuthQuery(url) {
  try {
    const cur = new URL(window.location.href);
    const pass = cur.searchParams.get("password");
    if (!pass) return url;

    const out = new URL(url, cur.origin);
    if (!out.searchParams.has("password")) out.searchParams.set("password", pass);
    return out.pathname + out.search;
  } catch {
    return url;
  }
}

function buildWsUrl(path) {
  const proto = window.location.protocol === "https:" ? "wss" : "ws";
  return `${proto}://${window.location.host}${withAuthQuery(path)}`;
}

function scheduleReconnect() {
  if (wsReconnectTimer) return;

  const delay = wsRetryMs;
  wsReconnectTimer = setTimeout(() => {
    wsReconnectTimer = null;
    connectWs();
  }, delay);

  wsRetryMs = Math.min(WS_RETRY_MAX_MS, wsRetryMs * 2);
}

function connectWs() {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;

  if (wsReconnectTimer) {
    clearTimeout(wsReconnectTimer);
    wsReconnectTimer = null;
  }

  let socket;
  try {
    socket = new WebSocket(buildWsUrl("/ws/radar"));
  } catch {
    scheduleReconnect();
    return;
  }

  ws = socket;

  socket.onopen = () => {
    wsRetryMs = 500;
  };

  socket.onmessage = (event) => {
    try {
      radarData = JSON.parse(event.data);
    } catch {
      // ignore malformed frame
    }
  };

  socket.onerror = () => {
    try { socket.close(); } catch {}
  };

  socket.onclose = () => {
    if (ws === socket) ws = null;
    scheduleReconnect();
  };
}

function readNumber(v, fallback = 0) {
  const n = Number(v);
  return Number.isFinite(n) ? n : fallback;
}

function getPlayers(data) {
  if (Array.isArray(data?.players)) return data.players;
  if (Array.isArray(data?.Players)) return data.Players;
  return [];
}

function getExfils(data) {
  if (Array.isArray(data?.exfils)) return data.exfils;
  if (Array.isArray(data?.Exfils)) return data.Exfils;
  return [];
}

function getTransits(data) {
  if (Array.isArray(data?.transits)) return data.transits;
  if (Array.isArray(data?.Transits)) return data.Transits;
  return [];
}

function getMap(data) {
  return data?.map || data?.Map || null;
}

function readWorldY(entity) {
  const y = entity?.heightY ?? entity?.HeightY ?? entity?.position?.y ?? entity?.position?.Y;
  return Number.isFinite(Number(y)) ? Number(y) : null;
}

function readPlayerMapXY(player) {
  const hasMapPos = !!(player?.hasMapPos ?? player?.HasMapPos);
  const mx = player?.mapX ?? player?.MapX;
  const my = player?.mapY ?? player?.MapY;
  if (hasMapPos && Number.isFinite(Number(mx)) && Number.isFinite(Number(my))) {
    return { x: Number(mx), y: Number(my), valid: true };
  }
  return { x: 0, y: 0, valid: false };
}

function readExfilMapXY(exfil) {
  const x = exfil?.x ?? exfil?.X;
  const y = exfil?.y ?? exfil?.Y;
  if (!Number.isFinite(Number(x)) || !Number.isFinite(Number(y))) {
    return { x: 0, y: 0, h: 0, valid: false };
  }
  const h = exfil?.height ?? exfil?.Height ?? exfil?.z ?? exfil?.Z ?? 0;
  return { x: Number(x), y: Number(y), h: Number.isFinite(Number(h)) ? Number(h) : 0, valid: true };
}

function readTransitMapXY(transit) {
  const x = transit?.x ?? transit?.X;
  const y = transit?.y ?? transit?.Y;
  if (!Number.isFinite(Number(x)) || !Number.isFinite(Number(y))) {
    return { x: 0, y: 0, h: 0, valid: false };
  }
  const h = transit?.height ?? transit?.Height ?? transit?.z ?? transit?.Z ?? 0;
  return { x: Number(x), y: Number(y), h: Number.isFinite(Number(h)) ? Number(h) : 0, valid: true };
}

function getMapLayers(map) {
  if (Array.isArray(map?.layers)) return map.layers;
  if (Array.isArray(map?.Layers)) return map.Layers;
  return [];
}

function getVisibleLayers(map, localWorldY) {
  const layers = getMapLayers(map);
  if (!layers.length) return [];

  const visible = layers.filter((layer) => {
    const min = layer?.minHeight ?? layer?.MinHeight;
    const max = layer?.maxHeight ?? layer?.MaxHeight;
    const minOk = (min == null) || localWorldY === null || (localWorldY >= Number(min));
    const maxOk = (max == null) || localWorldY === null || (localWorldY <= Number(max));
    return minOk && maxOk;
  });

  visible.sort((a, b) => {
    const aBase = (a?.minHeight ?? a?.MinHeight) == null && (a?.maxHeight ?? a?.MaxHeight) == null;
    const bBase = (b?.minHeight ?? b?.MinHeight) == null && (b?.maxHeight ?? b?.MaxHeight) == null;
    if (aBase && !bBase) return -1;
    if (!aBase && bBase) return 1;

    const aMin = Number(a?.minHeight ?? a?.MinHeight ?? -Number.MAX_VALUE);
    const bMin = Number(b?.minHeight ?? b?.MinHeight ?? -Number.MAX_VALUE);
    if (aMin !== bMin) return aMin - bMin;

    const aMax = Number(a?.maxHeight ?? a?.MaxHeight ?? Number.MAX_VALUE);
    const bMax = Number(b?.maxHeight ?? b?.MaxHeight ?? Number.MAX_VALUE);
    return aMax - bMax;
  });

  return visible.length ? visible : [layers[0]];
}

function getLayerFilename(layer) {
  const name = layer?.filename ?? layer?.Filename;
  return String(name || "").trim();
}

function getLayerFilenames(map) {
  const layers = Array.isArray(map?.layers) ? map.layers : (Array.isArray(map?.Layers) ? map.Layers : []);
  const names = [];
  for (const layer of layers) {
    const name = getLayerFilename(layer);
    if (!name) continue;
    if (!names.includes(name)) names.push(name);
  }
  return names;
}

function getImage(src) {
  if (!src) return null;
  if (imageCache.has(src)) return imageCache.get(src);

  const img = new Image();
  const rec = { img, ok: false, failed: false };
  img.onload = () => { rec.ok = true; };
  img.onerror = () => { rec.failed = true; };
  img.src = src;
  imageCache.set(src, rec);
  return rec;
}

function parseSvgSize(text) {
  if (typeof text !== "string" || !text.length) return null;

  const vb = text.match(/viewBox\s*=\s*["']\s*[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?\s+[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?\s+([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s+([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*["']/i);
  if (vb) {
    const w = Number(vb[1]);
    const h = Number(vb[2]);
    if (Number.isFinite(w) && Number.isFinite(h) && w > 0 && h > 0) {
      return { width: w, height: h };
    }
  }

  const wMatch = text.match(/\bwidth\s*=\s*["']\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)/i);
  const hMatch = text.match(/\bheight\s*=\s*["']\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)/i);
  if (wMatch && hMatch) {
    const w = Number(wMatch[1]);
    const h = Number(hMatch[1]);
    if (Number.isFinite(w) && Number.isFinite(h) && w > 0 && h > 0) {
      return { width: w, height: h };
    }
  }

  return null;
}

function ensureSvgMeta(filename) {
  if (!filename || !filename.toLowerCase().endsWith(".svg")) return null;
  if (svgMetaCache.has(filename)) return svgMetaCache.get(filename);

  const rec = { width: 0, height: 0, ok: false, failed: false, pending: true };
  svgMetaCache.set(filename, rec);

  fetch(`/Maps/${filename}`)
    .then((r) => r.ok ? r.text() : Promise.reject(new Error(`HTTP ${r.status}`)))
    .then((txt) => {
      const size = parseSvgSize(txt);
      if (size) {
        rec.width = size.width;
        rec.height = size.height;
        rec.ok = true;
      } else {
        rec.failed = true;
      }
    })
    .catch(() => {
      rec.failed = true;
    })
    .finally(() => {
      rec.pending = false;
    });

  return rec;
}

function resolveVisibleLayerImages(visibleLayers) {
  const rendered = [];
  let pending = false;

  for (const layer of visibleLayers) {
    const filename = getLayerFilename(layer);
    if (!filename) continue;

    const rec = getImage(`/Maps/${filename}`);
    if (!rec) continue;
    if (rec.ok) {
      rendered.push({ layer, image: rec, filename });
      continue;
    }

    if (!rec.failed) pending = true;
  }

  return { rendered, pending };
}

function isBaseLayer(layer) {
  const min = layer?.minHeight ?? layer?.MinHeight;
  const max = layer?.maxHeight ?? layer?.MaxHeight;
  return min == null && max == null;
}

function getLayerRawSize(layer, renderedLayers) {
  const filename = getLayerFilename(layer);
  if (!filename) return { width: 0, height: 0, pending: false };

  const meta = ensureSvgMeta(filename);
  if (meta?.ok && meta.width > 0 && meta.height > 0) {
    return { width: meta.width, height: meta.height, pending: false };
  }
  if (meta?.pending) {
    return { width: 0, height: 0, pending: true };
  }

  const rendered = renderedLayers.find((x) => x?.filename === filename);
  const iw = Number(rendered?.image?.img?.naturalWidth || 0);
  const ih = Number(rendered?.image?.img?.naturalHeight || 0);
  if (iw > 0 && ih > 0) {
    return { width: iw, height: ih, pending: false };
  }

  return { width: 0, height: 0, pending: false };
}

function getMapRawSize(visibleLayers, renderedLayers) {
  if (!visibleLayers.length) return { width: 0, height: 0, pending: false };

  const baseLayer = visibleLayers.find(isBaseLayer) || visibleLayers[0];
  const baseSize = getLayerRawSize(baseLayer, renderedLayers);
  if (baseSize.width > 0 && baseSize.height > 0) {
    return baseSize;
  }
  if (baseSize.pending) {
    return { width: 0, height: 0, pending: true };
  }

  let maxW = 0;
  let maxH = 0;
  let pending = false;
  for (const layer of visibleLayers) {
    const size = getLayerRawSize(layer, renderedLayers);
    if (size.pending) pending = true;
    if (size.width > maxW) maxW = size.width;
    if (size.height > maxH) maxH = size.height;
  }

  return { width: maxW, height: maxH, pending };
}

function getPlayerColor(player) {
  if (player?.isLocal || player?.IsLocal) return "#008000";
  if (player?.isFriendly || player?.IsFriendly) return "#32cd32";
  if (player?.isAlive === false || player?.IsAlive === false) return "#000000";
  if (player?.isBoss || player?.IsBoss) return "#ff00ff";
  const typeName = String(player?.typeName ?? player?.TypeName ?? "");
  if (typeName === "Player") return "#ff0000";
  if (typeName === "PlayerScav") return "#ffffff";
  return "#ffff00";
}

function getPlayerName(player) {
  const name = String(player?.name ?? player?.Name ?? "").trim();
  return name || "--";
}

function drawPlayerLabel(screenX, screenY, lines, color, markerScale) {
  if (!Array.isArray(lines) || !lines.length) return;

  const cleanLines = lines
    .map((x) => String(x || "").trim())
    .filter((x) => x.length > 0);
  if (!cleanLines.length) return;

  const fontSize = Math.max(8, 12 * markerScale);
  const lineHeight = fontSize + 2;
  const offsetX = 12 * markerScale;
  const firstY = screenY - ((cleanLines.length - 1) * lineHeight * 0.5);

  ctx.save();
  ctx.font = `600 ${fontSize}px Segoe UI`;
  ctx.textAlign = "left";
  ctx.textBaseline = "middle";

  ctx.lineWidth = Math.max(2, 3 * markerScale);
  ctx.strokeStyle = "#000000";
  ctx.fillStyle = color;
  for (let i = 0; i < cleanLines.length; i += 1) {
    const y = firstY + (i * lineHeight);
    const text = cleanLines[i];
    ctx.strokeText(text, screenX + offsetX, y);
    ctx.fillText(text, screenX + offsetX, y);
  }
  ctx.restore();
}

function isLocalPlayer(player) {
  return !!(player?.isLocal ?? player?.IsLocal);
}

function isFriendlyPlayer(player) {
  return !!(player?.isFriendly ?? player?.IsFriendly);
}

function isHumanPlayer(player) {
  const isAI = !!(player?.isAI ?? player?.IsAI);
  return !isAI;
}

function getReferencePlayers(players) {
  const refs = [];
  let teammateIndex = 1;
  let hostileIndex = 1;
  for (const player of players) {
    if (isLocalPlayer(player)) {
      refs.push({
        key: "local",
        label: `You (${getPlayerName(player)})`,
        player
      });
      continue;
    }

    if (!isHumanPlayer(player)) continue;

    if (isFriendlyPlayer(player)) {
      refs.push({
        key: `tm:${teammateIndex}`,
        label: `Teammate ${teammateIndex}: ${getPlayerName(player)}`,
        player
      });
      teammateIndex += 1;
    } else {
      refs.push({
        key: `enemy:${hostileIndex}`,
        label: `Enemy ${hostileIndex}: ${getPlayerName(player)}`,
        player
      });
      hostileIndex += 1;
    }
  }

  return refs;
}

function syncReferenceSelect(players) {
  const select = document.getElementById("reference-player-select");
  if (!select) return;

  const refs = getReferencePlayers(players);
  const sig = refs.map((x) => `${x.key}:${x.label}`).join("|");
  referencePlayers = refs;

  if (sig !== referenceSignature) {
    referenceSignature = sig;
    select.innerHTML = "";
    for (const ref of refs) {
      const option = document.createElement("option");
      option.value = ref.key;
      option.textContent = ref.label;
      select.append(option);
    }
  }

  if (!selectedReferenceKey || !refs.some((x) => x.key === selectedReferenceKey)) {
    selectedReferenceKey = refs[0]?.key ?? "";
  }

  if (select.value !== selectedReferenceKey) {
    select.value = selectedReferenceKey;
  }

  selectedReferencePlayer = refs.find((x) => x.key === selectedReferenceKey)?.player ?? null;
}

function setAimlineUiValue(value) {
  const clamped = clamp(Math.round(Number(value) || 0), 0, 10000);
  uiAimLineLength = clamped;

  const slider = document.getElementById("aimline-slider");
  const label = document.getElementById("aimline-value");
  if (slider) slider.value = String(clamped);
  if (label) label.textContent = String(clamped);
}

function setMarkerScaleUiValue(value) {
  const clampedPct = clamp(Math.round(Number(value) || 300), 250, 350);
  markerScaleMultiplier = clampedPct / 100;

  const slider = document.getElementById("marker-scale-slider");
  const label = document.getElementById("marker-scale-value");
  if (slider) slider.value = String(clampedPct);
  if (label) label.textContent = `${clampedPct}%`;
}

function setNonHumanAimlineUiValue(value) {
  const clamped = clamp(Math.round(Number(value) || 0), 0, 10000);
  uiNonHumanAimLineLength = clamped;

  const slider = document.getElementById("nonhuman-aimline-slider");
  const label = document.getElementById("nonhuman-aimline-value");
  if (slider) slider.value = String(clamped);
  if (label) label.textContent = String(clamped);
}

const PLAYER_PILL = {
  length: 9,
  radius: 3,
  halfHeight: 3 * 0.85,
  noseX: (9 / 2) + (3 * 0.18)
};

function normalizeDeg(v) {
  const x = Number(v);
  if (!Number.isFinite(x)) return 0;
  return ((x % 360) + 360) % 360;
}

function buildPlayerPillPath() {
  const p = PLAYER_PILL;
  const path = new Path2D();
  const x0 = -p.length * 0.5;
  const backCx = x0 + p.radius;

  path.moveTo(backCx, -p.halfHeight);
  path.arc(backCx, 0, p.radius, -Math.PI * 0.5, Math.PI * 0.5, true);

  const c1x = backCx + p.radius * 1.1;
  const c2x = p.noseX - p.radius * 0.28;
  const c1y = p.halfHeight * 0.55;
  const c2y = p.halfHeight * 0.3;

  path.bezierCurveTo(c1x, c1y, c2x, c2y, p.noseX, 0);
  path.bezierCurveTo(c2x, -c2y, c1x, -c1y, backCx, -p.halfHeight);
  path.closePath();
  return path;
}

const playerPillPath = buildPlayerPillPath();

function drawPlayerPill(screenX, screenY, yawDeg, color, aimlineLength, markerScale) {
  const mapRotation = normalizeDeg(yawDeg - 90);
  const scale = 1.65 * markerScale;

  ctx.save();
  ctx.translate(screenX, screenY);
  ctx.rotate((mapRotation * Math.PI) / 180);
  ctx.scale(scale, scale);

  ctx.strokeStyle = "#000000";
  ctx.lineWidth = 2.158;
  ctx.lineJoin = "round";
  ctx.lineCap = "round";
  ctx.stroke(playerPillPath);

  ctx.strokeStyle = color;
  ctx.lineWidth = 1.66;
  ctx.stroke(playerPillPath);

  if (Number.isFinite(aimlineLength) && aimlineLength > 0) {
    const noseX = PLAYER_PILL.noseX;

    ctx.strokeStyle = "#000000";
    ctx.lineWidth = 2.158;
    ctx.beginPath();
    ctx.moveTo(noseX, 0);
    ctx.lineTo(noseX + aimlineLength, 0);
    ctx.stroke();

    ctx.strokeStyle = color;
    ctx.lineWidth = 1.66;
    ctx.beginPath();
    ctx.moveTo(noseX, 0);
    ctx.lineTo(noseX + aimlineLength, 0);
    ctx.stroke();
  }

  ctx.restore();
}

function readPlayerWorldPos(player) {
  const x = player?.position?.x ?? player?.position?.X;
  const y = player?.position?.y ?? player?.position?.Y;
  const z = player?.position?.z ?? player?.position?.Z;
  if (!Number.isFinite(Number(x)) || !Number.isFinite(Number(y)) || !Number.isFinite(Number(z))) {
    return null;
  }
  return { x: Number(x), y: Number(y), z: Number(z) };
}

function getRelativeStats(player, referencePlayer) {
  if (!player || !referencePlayer) return null;

  const p = readPlayerWorldPos(player);
  const r = readPlayerWorldPos(referencePlayer);
  const py = readWorldY(player);
  const ry = readWorldY(referencePlayer);
  const dy = (py != null && ry != null) ? (py - ry) : null;
  if (p && r) {
    const dx = p.x - r.x;
    const dz = p.z - r.z;

    return {
      height: dy != null ? dy : (p.y - r.y),
      distance: Math.round(Math.hypot(dx, dy, dz))
    };
  }

  const pm = readPlayerMapXY(player);
  const rm = readPlayerMapXY(referencePlayer);
  if (!pm.valid || !rm.valid) return null;

  const dx = pm.x - rm.x;
  const dz = pm.y - rm.y;

  return {
    height: dy != null ? dy : 0,
    distance: Math.round(Math.hypot(dx, dz))
  };
}

function getAimlineSettings(data) {
  const aimLineLength = readNumber(data?.aimLineLength ?? data?.AimLineLength, 1500);
  const nonHumanAimLineLength = readNumber(data?.nonHumanAimLineLength ?? data?.NonHumanAimLineLength, aimLineLength);
  const maxDistance = readNumber(data?.maxDistance ?? data?.MaxDistance, 350);
  return {
    aimLineLength: clamp(aimLineLength, 0, 10000),
    nonHumanAimLineLength: clamp(nonHumanAimLineLength, 0, 10000),
    maxDistance: Math.max(1, maxDistance),
    teammateAimlines: !!(data?.teammateAimlines ?? data?.TeammateAimlines),
    aiAimlines: !!(data?.aiAimlines ?? data?.AIAimlines)
  };
}

function isFacingTarget(player, target, maxDist) {
  const p = readPlayerWorldPos(player);
  const t = readPlayerWorldPos(target);
  if (!p || !t) return false;

  const dx = t.x - p.x;
  const dy = t.y - p.y;
  const dz = t.z - p.z;
  const distSq = (dx * dx) + (dy * dy) + (dz * dz);

  const maxDistSq = maxDist * maxDist;
  if (distSq > maxDistSq) return false;

  const distance = Math.sqrt(distSq);
  if (distance <= 1e-6) return true;

  const yawDeg = readNumber(player?.rotation?.x ?? player?.rotation?.X ?? player?.yaw ?? player?.Yaw, 0);
  const pitchDeg = readNumber(player?.rotation?.y ?? player?.rotation?.Y, 0);
  const yaw = yawDeg * (Math.PI / 180);
  const pitch = pitchDeg * (Math.PI / 180);

  const cp = Math.cos(pitch);
  const sp = Math.sin(pitch);
  const sy = Math.sin(yaw);
  const cy = Math.cos(yaw);

  let fx = cp * sy;
  let fy = -sp;
  let fz = cp * cy;

  const fLenSq = (fx * fx) + (fy * fy) + (fz * fz);
  if (fLenSq > 0 && Math.abs(fLenSq - 1) > 1e-4) {
    const inv = 1 / Math.sqrt(fLenSq);
    fx *= inv;
    fy *= inv;
    fz *= inv;
  }

  const cosAngle = ((fx * dx) + (fy * dy) + (fz * dz)) / distance;

  const A = 31.3573;
  const B = 3.51726;
  const C = 0.626957;
  const D = 15.6948;

  const x = Math.abs(C - (D * distance));
  let angleDeg = A - (B * Math.log(Math.max(x, 1e-6)));
  if (angleDeg < 1) angleDeg = 1;
  if (angleDeg > 179) angleDeg = 179;

  const cosThreshold = Math.cos(angleDeg * (Math.PI / 180));
  return cosAngle >= cosThreshold;
}

function getPlayerAimlineLength(player, localPlayer, settings) {
  if (!player || !settings) return 0;

  const isLocal = !!(player?.isLocal ?? player?.IsLocal);
  const isFriendly = !!(player?.isFriendly ?? player?.IsFriendly);
  const isAI = !!(player?.isAI ?? player?.IsAI);
  let aimlineLength = (isLocal || (isFriendly && settings.teammateAimlines))
    ? settings.aimLineLength
    : (isAI && settings.aiAimlines ? settings.nonHumanAimLineLength : 0);

  if (localPlayer
    && !isFriendly
    && !(isAI && !settings.aiAimlines)
    && isFacingTarget(player, localPlayer, settings.maxDistance)) {
    aimlineLength = 9999;
  }

  return aimlineLength;
}

function drawDeathMarker(screenX, screenY, markerScale) {
  const len = 6 * markerScale;
  ctx.save();
  ctx.translate(screenX, screenY);
  ctx.strokeStyle = "#000000";
  ctx.lineWidth = Math.max(1, 3 * markerScale);
  ctx.lineCap = "round";
  ctx.beginPath();
  ctx.moveTo(-len, len);
  ctx.lineTo(len, -len);
  ctx.moveTo(-len, -len);
  ctx.lineTo(len, len);
  ctx.stroke();
  ctx.restore();
}

function drawUpArrow(screenX, screenY, color, markerScale) {
  const w = 6.5 * markerScale;
  const h = 6.5 * markerScale;
  const path = new Path2D();
  path.moveTo(0, -h);
  path.lineTo(w * 0.75, h * 0.85);
  path.lineTo(0, h * 0.35);
  path.lineTo(-w * 0.75, h * 0.85);
  path.closePath();

  ctx.save();
  ctx.translate(screenX, screenY);
  ctx.strokeStyle = "#000000";
  ctx.lineWidth = Math.max(1, 2 * markerScale);
  ctx.lineJoin = "round";
  ctx.stroke(path);
  ctx.fillStyle = color;
  ctx.fill(path);
  ctx.restore();
}

function drawDownArrow(screenX, screenY, color, markerScale) {
  const w = 6.5 * markerScale;
  const h = 6.5 * markerScale;
  const path = new Path2D();
  path.moveTo(0, h);
  path.lineTo(w * 0.75, -h * 0.85);
  path.lineTo(0, -h * 0.35);
  path.lineTo(-w * 0.75, -h * 0.85);
  path.closePath();

  ctx.save();
  ctx.translate(screenX, screenY);
  ctx.strokeStyle = "#000000";
  ctx.lineWidth = Math.max(1, 2 * markerScale);
  ctx.lineJoin = "round";
  ctx.stroke(path);
  ctx.fillStyle = color;
  ctx.fill(path);
  ctx.restore();
}

function drawExitMarker(pos, localWorldY, color, markerScale) {
  const radius = 4.75 * markerScale;
  const stroke = Math.max(1, 2 * markerScale);

  if (localWorldY == null) {
    ctx.beginPath();
    ctx.strokeStyle = "#000000";
    ctx.lineWidth = stroke;
    ctx.arc(pos.x, pos.y, radius, 0, Math.PI * 2);
    ctx.stroke();
    ctx.beginPath();
    ctx.fillStyle = color;
    ctx.arc(pos.x, pos.y, radius, 0, Math.PI * 2);
    ctx.fill();
    return;
  }

  const diff = pos.h - localWorldY;
  if (diff > 1.85) {
    drawUpArrow(pos.x, pos.y, color, markerScale);
    return;
  }
  if (diff < -1.85) {
    drawDownArrow(pos.x, pos.y, color, markerScale);
    return;
  }

  ctx.beginPath();
  ctx.strokeStyle = "#000000";
  ctx.lineWidth = stroke;
  ctx.arc(pos.x, pos.y, radius, 0, Math.PI * 2);
  ctx.stroke();
  ctx.beginPath();
  ctx.fillStyle = color;
  ctx.arc(pos.x, pos.y, radius, 0, Math.PI * 2);
  ctx.fill();
}

function drawBackground() {
  ctx.fillStyle = "#000000";
  ctx.fillRect(0, 0, viewportW, viewportH);
}

function drawCenterMessage(text, color = "#94a3b8") {
  if (!text) return;
  ctx.save();
  ctx.fillStyle = color;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.font = "600 20px Segoe UI";
  ctx.fillText(text, viewportW * 0.5, viewportH * 0.5);
  ctx.restore();
}

function resetCameraForMap(mapKey) {
  camera.mapKey = mapKey;
  camera.initialized = false;
  camera.currentParams = null;
}

function aspectFillBounds(left, top, right, bottom, targetW, targetH) {
  const cx = (left + right) * 0.5;
  const cy = (top + bottom) * 0.5;
  let w = Math.max(1e-3, right - left);
  let h = Math.max(1e-3, bottom - top);
  const rectAspect = w / h;
  const targetAspect = Math.max(1e-3, targetW / Math.max(1e-3, targetH));

  // True aspect-fill: match target aspect by shrinking one axis (cropping),
  // not expanding bounds (which creates letterboxing and fake map edge clipping).
  if (rectAspect < targetAspect) {
    h = w / targetAspect;
  } else {
    w = h * targetAspect;
  }

  return {
    left: cx - (w * 0.5),
    top: cy - (h * 0.5),
    right: cx + (w * 0.5),
    bottom: cy + (h * 0.5),
    width: w,
    height: h
  };
}

function buildMapParams(fullMapW, fullMapH) {
  const zoomFactor = Math.max(0.01, camera.zoomPercent * 0.01);
  const zoomW = fullMapW * zoomFactor;
  const zoomH = fullMapH * zoomFactor;

  const raw = {
    left: camera.centerX - zoomW * 0.5,
    top: camera.centerY - zoomH * 0.5,
    right: camera.centerX + zoomW * 0.5,
    bottom: camera.centerY + zoomH * 0.5
  };

  const bounds = aspectFillBounds(raw.left, raw.top, raw.right, raw.bottom, viewportW, viewportH);
  return {
    bounds,
    xScale: viewportW / bounds.width,
    yScale: viewportH / bounds.height
  };
}

function toZoomedPos(mapX, mapY, params) {
  return {
    x: (mapX - params.bounds.left) * params.xScale,
    y: (mapY - params.bounds.top) * params.yScale
  };
}

function drawMapLayers(renderedLayers, params, svgScale, map) {
  if (!renderedLayers.length) return false;

  const disableDimming = !!(map?.disableDimming ?? map?.DisableDimming);
  const front = renderedLayers[renderedLayers.length - 1];
  const frontNoDimLower = !!(front?.layer?.cannotDimLowerLayers ?? front?.layer?.CannotDimLowerLayers);
  let drewAny = false;

  for (const item of renderedLayers) {
    const raw = getLayerRawSize(item.layer, renderedLayers);
    if (!(raw.width > 0) || !(raw.height > 0)) continue;

    const drawX = (0 - params.bounds.left) * params.xScale;
    const drawY = (0 - params.bounds.top) * params.yScale;
    const drawW = (raw.width * svgScale) * params.xScale;
    const drawH = (raw.height * svgScale) * params.yScale;
    if (!(drawW > 0) || !(drawH > 0)) continue;

    const shouldDim = !disableDimming && item !== front && !frontNoDimLower;
    const prevAlpha = ctx.globalAlpha;
    if (shouldDim) ctx.globalAlpha = 0.38;
    ctx.drawImage(item.image.img, drawX, drawY, drawW, drawH);
    ctx.globalAlpha = prevAlpha;
    drewAny = true;
  }

  return drewAny;
}

function drawFrame() {
  drawBackground();

  const data = radarData;
  if (!data) {
    if (ws && ws.readyState === WebSocket.OPEN) {
      drawCenterMessage("Wait for raid to start", "#fcd34d");
    } else if (!ws || ws.readyState === WebSocket.CLOSED) {
      drawCenterMessage("Game Processor not running", "#fca5a5");
    } else {
      drawCenterMessage("Connecting...", "#fcd34d");
    }
    return;
  }

  const inRaid = !!(data?.inRaid ?? data?.InRaid ?? data?.inGame ?? data?.InGame);
  const map = getMap(data);
  const players = getPlayers(data);
  const exfils = getExfils(data);
  const transits = getTransits(data);
  const serverAimlineSettings = getAimlineSettings(data);
  if (!uiAimLineInitialized) {
    setAimlineUiValue(serverAimlineSettings.aimLineLength);
    setNonHumanAimlineUiValue(serverAimlineSettings.nonHumanAimLineLength);
    uiAimLineInitialized = true;
  }
  const aimlineSettings = {
    ...serverAimlineSettings,
    aimLineLength: uiAimLineLength,
    nonHumanAimLineLength: uiNonHumanAimLineLength
  };
  const local = players.find((p) => p?.isLocal || p?.IsLocal) || null;
  const localPos = local ? readPlayerMapXY(local) : null;
  const localWorldY = local ? readWorldY(local) : null;
  const markerScale = BASE_MARKER_SCALE * markerScaleMultiplier;
  syncReferenceSelect(players);

  if (autoFollowReference) {
    const refPos = selectedReferencePlayer ? readPlayerMapXY(selectedReferencePlayer) : null;
    const followPos = refPos?.valid ? refPos : (localPos?.valid ? localPos : null);
    if (followPos) {
      camera.centerX = followPos.x;
      camera.centerY = followPos.y;
    }
  }

  const visibleLayers = getVisibleLayers(map, localWorldY);
  const primaryLayer = visibleLayers.length ? visibleLayers[visibleLayers.length - 1] : null;
  const filename = getLayerFilename(primaryLayer);
  const mapId = String(data?.mapId ?? data?.MapId ?? data?.mapID ?? data?.MapID ?? "unknown");
  const mapKey = mapId;
  if (camera.mapKey !== mapKey) resetCameraForMap(mapKey);

  const layerImages = resolveVisibleLayerImages(visibleLayers);

  const svgScale = readNumber(map?.svgScale ?? map?.SvgScale, 1);
  const rawSize = getMapRawSize(visibleLayers, layerImages.rendered);
  const fullMapW = rawSize.width * Math.max(1, svgScale);
  const fullMapH = rawSize.height * Math.max(1, svgScale);

  if (!camera.initialized) {
    if (localPos?.valid) {
      camera.centerX = localPos.x;
      camera.centerY = localPos.y;
    } else if (fullMapW > 0 && fullMapH > 0) {
      camera.centerX = fullMapW * 0.5;
      camera.centerY = fullMapH * 0.5;
    } else {
      camera.centerX = 0;
      camera.centerY = 0;
    }
    camera.zoomPercent = 100;
    camera.initialized = true;
  }

  if (fullMapW <= 0 || fullMapH <= 0 || rawSize.pending) {
    drawCenterMessage("Loading map...", "#fcd34d");
    return;
  }

  camera.mapUnitsW = fullMapW;
  camera.mapUnitsH = fullMapH;

  const params = buildMapParams(fullMapW, fullMapH);
  camera.currentParams = params;

  const hasMap = drawMapLayers(layerImages.rendered, params, Math.max(1, svgScale), map);
  if (!hasMap) {
    drawCenterMessage("Loading map...", "#fcd34d");
    return;
  }

  for (const exfil of exfils) {
    const pos = readExfilMapXY(exfil);
    if (!pos.valid) continue;

    const p = toZoomedPos(pos.x, pos.y, params);
    if (!Number.isFinite(p.x) || !Number.isFinite(p.y)) continue;
    drawExitMarker({ x: p.x, y: p.y, h: pos.h }, localWorldY, "#ffff00", markerScale);
  }

  for (const transit of transits) {
    const pos = readTransitMapXY(transit);
    if (!pos.valid) continue;

    const p = toZoomedPos(pos.x, pos.y, params);
    if (!Number.isFinite(p.x) || !Number.isFinite(p.y)) continue;
    drawExitMarker({ x: p.x, y: p.y, h: pos.h }, localWorldY, "#ffa500", markerScale);
  }

  for (const player of players) {
    const pos = readPlayerMapXY(player);
    if (!pos.valid) continue;

    const p = toZoomedPos(pos.x, pos.y, params);
    if (!Number.isFinite(p.x) || !Number.isFinite(p.y)) continue;

    const alive = !((player?.isAlive === false) || (player?.IsAlive === false));
    const color = getPlayerColor(player);
    const name = getPlayerName(player);
    const relativeRef = selectedReferencePlayer || local;
    const relative = getRelativeStats(player, relativeRef);
    const labelLines = relative
      ? [name, `H: ${relative.height.toFixed(1)} D: ${relative.distance}`]
      : [name];
    if (!alive) {
      drawDeathMarker(p.x, p.y, markerScale);
      drawPlayerLabel(p.x, p.y, labelLines, color, markerScale);
      continue;
    }

    const yaw = player?.yaw ?? player?.Yaw ?? player?.rotation?.x ?? player?.rotation?.X ?? 0;
    const aimlineLength = getPlayerAimlineLength(player, local, aimlineSettings);
    drawPlayerPill(p.x, p.y, yaw, color, aimlineLength, markerScale);
    drawPlayerLabel(p.x, p.y, labelLines, color, markerScale);
  }

  if (!players.length) {
    drawCenterMessage(inRaid ? "In raid (no players in snapshot)" : "Wait for raid to start", inRaid ? "#86efac" : "#fcd34d");
  }
}

function setupToolbar() {
  const toolbar = document.getElementById("toolbar");
  const toggle = document.getElementById("toolbar-toggle");
  const markerScaleSlider = document.getElementById("marker-scale-slider");
  const aimlineSlider = document.getElementById("aimline-slider");
  const aimlineMinus = document.getElementById("aimline-minus");
  const aimlinePlus = document.getElementById("aimline-plus");
  const nonHumanAimlineSlider = document.getElementById("nonhuman-aimline-slider");
  const nonHumanAimlineMinus = document.getElementById("nonhuman-aimline-minus");
  const nonHumanAimlinePlus = document.getElementById("nonhuman-aimline-plus");
  const referenceSelect = document.getElementById("reference-player-select");
  const followReferenceToggle = document.getElementById("follow-reference-toggle");

  if (toggle && toolbar) {
    toggle.addEventListener("click", () => {
      toolbar.classList.toggle("collapsed");
      const expanded = !toolbar.classList.contains("collapsed");
      toggle.setAttribute("aria-expanded", expanded ? "true" : "false");
      toggle.textContent = expanded ? "Hide Tools" : "Radar Tools";
    });
  }

  setMarkerScaleUiValue(300);

  if (markerScaleSlider) {
    markerScaleSlider.addEventListener("input", () => {
      setMarkerScaleUiValue(markerScaleSlider.value);
    });
  }

  setAimlineUiValue(uiAimLineLength);

  if (aimlineSlider) {
    aimlineSlider.addEventListener("input", () => {
      setAimlineUiValue(aimlineSlider.value);
      uiAimLineInitialized = true;
    });
  }

  if (aimlineMinus) {
    aimlineMinus.addEventListener("click", () => {
      setAimlineUiValue(uiAimLineLength - 50);
      uiAimLineInitialized = true;
    });
  }

  if (aimlinePlus) {
    aimlinePlus.addEventListener("click", () => {
      setAimlineUiValue(uiAimLineLength + 50);
      uiAimLineInitialized = true;
    });
  }

  setNonHumanAimlineUiValue(uiNonHumanAimLineLength);

  if (nonHumanAimlineSlider) {
    nonHumanAimlineSlider.addEventListener("input", () => {
      setNonHumanAimlineUiValue(nonHumanAimlineSlider.value);
      uiAimLineInitialized = true;
    });
  }

  if (nonHumanAimlineMinus) {
    nonHumanAimlineMinus.addEventListener("click", () => {
      setNonHumanAimlineUiValue(uiNonHumanAimLineLength - 50);
      uiAimLineInitialized = true;
    });
  }

  if (nonHumanAimlinePlus) {
    nonHumanAimlinePlus.addEventListener("click", () => {
      setNonHumanAimlineUiValue(uiNonHumanAimLineLength + 50);
      uiAimLineInitialized = true;
    });
  }

  if (referenceSelect) {
    referenceSelect.addEventListener("change", () => {
      selectedReferenceKey = referenceSelect.value || "";
    });
  }

  if (followReferenceToggle) {
    followReferenceToggle.addEventListener("change", () => {
      autoFollowReference = !!followReferenceToggle.checked;
    });
  }
}

function setupPointerControls() {
  canvas.addEventListener("pointerdown", (e) => {
    camera.dragging = true;
    camera.lastX = e.clientX;
    camera.lastY = e.clientY;
    try { canvas.setPointerCapture(e.pointerId); } catch {}
  });

  canvas.addEventListener("pointermove", (e) => {
    if (!camera.dragging) return;
    const params = camera.currentParams;
    if (!params) return;

    const dx = e.clientX - camera.lastX;
    const dy = e.clientY - camera.lastY;

    const mapPerPixelX = params.bounds.width / Math.max(1, viewportW);
    const mapPerPixelY = params.bounds.height / Math.max(1, viewportH);
    camera.centerX -= dx * mapPerPixelX;
    camera.centerY -= dy * mapPerPixelY;

    camera.lastX = e.clientX;
    camera.lastY = e.clientY;
  });

  const stopDrag = (e) => {
    camera.dragging = false;
    try { canvas.releasePointerCapture(e.pointerId); } catch {}
  };

  canvas.addEventListener("pointerup", stopDrag);
  canvas.addEventListener("pointercancel", stopDrag);

  canvas.addEventListener("wheel", (e) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? 5 : -5;
    camera.zoomPercent = clamp(camera.zoomPercent + delta, camera.minZoomPercent, camera.maxZoomPercent);
  }, { passive: false });
}

function loop() {
  drawFrame();
  requestAnimationFrame(loop);
}

window.addEventListener("resize", resizeCanvas);
resizeCanvas();
setupPointerControls();
setupToolbar();
connectWs();
loop();
