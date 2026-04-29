const WIDTH = 200;
const HEIGHT = 160;
const PIXEL_SIZE = 24;
const DEFAULT_COLOR = "#FFFFFF";
const COLORS = [
  "#FFFFFF",
  "#000000",
  "#EF4444",
  "#F97316",
  "#F59E0B",
  "#84CC16",
  "#22C55E",
  "#14B8A6",
  "#06B6D4",
  "#0EA5E9",
  "#3B82F6",
  "#6366F1",
  "#8B5CF6",
  "#A855F7",
  "#D946EF",
  "#EC4899",
  "#F43F5E",
  "#64748B",
  "#78716C",
  "#451A03",
];

const gridEl = document.querySelector("#grid");
const paletteEl = document.querySelector("#palette");
const toggleEl = document.querySelector("#palette-toggle");
const statusEl = document.querySelector("#status");
const ctx = gridEl.getContext("2d", { alpha: false });

let selectedColorIndex = 0;
let tiles = new Array(WIDTH * HEIGHT).fill(DEFAULT_COLOR);
let versionstamps = new Array(WIDTH * HEIGHT).fill("");

function setStatus(message) {
  statusEl.textContent = message;
}

function getTelegramInitData() {
  return window.Telegram?.WebApp?.initData ?? "";
}

function initTelegramWebApp() {
  const webApp = window.Telegram?.WebApp;
  if (!webApp) return;
  webApp.ready();
  webApp.expand();
}

function renderPalette() {
  for (const [index, color] of COLORS.entries()) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = index === selectedColorIndex ? "color-button selected" : "color-button";
    button.style.backgroundColor = color;
    button.addEventListener("click", () => {
      selectedColorIndex = index;
      document.querySelectorAll(".color-button").forEach((el, i) => {
        el.classList.toggle("selected", i === selectedColorIndex);
      });
    });
    paletteEl.append(button);
  }

  toggleEl.addEventListener("click", () => {
    paletteEl.classList.toggle("hidden");
    toggleEl.textContent = paletteEl.classList.contains("hidden") ? "/\\" : "\\/";
  });
}

function renderGrid() {
  gridEl.width = WIDTH * PIXEL_SIZE;
  gridEl.height = HEIGHT * PIXEL_SIZE;

  for (let index = 0; index < tiles.length; index++) {
    drawPixel(index, tiles[index]);
  }
}

function drawPixel(index, color) {
  const x = index % WIDTH;
  const y = Math.floor(index / WIDTH);
  ctx.fillStyle = color;
  ctx.fillRect(x * PIXEL_SIZE, y * PIXEL_SIZE, PIXEL_SIZE, PIXEL_SIZE);
}

function getPixelIndex(event) {
  const rect = gridEl.getBoundingClientRect();
  const x = Math.floor(((event.clientX - rect.left) / rect.width) * WIDTH);
  const y = Math.floor(((event.clientY - rect.top) / rect.height) * HEIGHT);
  if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT) return null;
  return y * WIDTH + x;
}

function applyGameUpdates(updates) {
  for (const update of updates) {
    if (versionstamps[update.index] >= update.versionstamp) continue;
    tiles[update.index] = update.color;
    versionstamps[update.index] = update.versionstamp;
    drawPixel(update.index, update.color);
  }
}

async function loadGrid() {
  const response = await fetch("/pixelbattle/api/grid", { cache: "no-store" });
  if (!response.ok) throw new Error(`grid load failed: ${response.status}`);
  const grid = await response.json();
  tiles = grid.tiles;
  versionstamps = grid.versionstamps;
}

function listenForUpdates() {
  const eventSource = new EventSource("/pixelbattle/api/listen");
  eventSource.onopen = () => setStatus("");
  eventSource.onmessage = (event) => {
    const updates = JSON.parse(event.data);
    applyGameUpdates(updates);
  };
  eventSource.onerror = () => setStatus("Reconnecting...");
}

async function updateGrid(index, color) {
  const headers = {
    "Content-Type": "application/json",
  };
  const initData = getTelegramInitData();
  if (initData) headers["X-Telegram-Init-Data"] = initData;

  const response = await fetch("/pixelbattle/api/update", {
    method: "POST",
    headers,
    body: JSON.stringify([index, color]),
  });

  if (!response.ok) {
    const message = await response.json().catch(() => "failed to update grid");
    setStatus(String(message));
    return;
  }

  const versionstamp = await response.json();
  applyGameUpdates([{ index, color, versionstamp }]);
}

async function main() {
  try {
    initTelegramWebApp();
    renderPalette();
    await loadGrid();
    renderGrid();
    gridEl.addEventListener("click", (event) => {
      const index = getPixelIndex(event);
      if (index === null) return;
      updateGrid(index, COLORS[selectedColorIndex]);
    });
    listenForUpdates();
    setStatus("");
  } catch (error) {
    console.error(error);
    setStatus("Failed to load PixelBattle.");
  }
}

main();
