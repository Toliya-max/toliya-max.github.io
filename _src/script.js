const G = {
  K: "♔", Q: "♕", R: "♖", B: "♗", N: "♘", P: "♙",
  k: "♚", q: "♛", r: "♜", b: "♝", n: "♞", p: "♟"
};

function sqId(r, f) {
  return String.fromCharCode(97 + f) + (8 - r);
}

function buildCell(isLight, glyph, highlighted) {
  const sq = document.createElement("div");
  sq.className = "sq " + (isLight ? "light" : "dark");
  if (highlighted) sq.classList.add("hl");
  if (glyph) {
    const p = document.createElement("span");
    p.className = "piece " + (glyph === glyph.toUpperCase() ? "white" : "black");
    p.textContent = G[glyph];
    sq.appendChild(p);
  }
  return sq;
}

function renderBoard(el) {
  const fen = el.dataset.fen;
  if (!fen) return;
  const hi = (el.dataset.highlight || "").split(",").map(s => s.trim()).filter(Boolean);
  const ranks = fen.split(" ")[0].split("/");
  el.innerHTML = "";
  const grid = document.createElement("div");
  grid.className = "board-grid";
  for (let r = 0; r < 8; r++) {
    let f = 0;
    for (const ch of ranks[r]) {
      if (/\d/.test(ch)) {
        for (let i = 0; i < parseInt(ch, 10); i++) {
          const isLight = (r + f) % 2 === 0;
          const sq = buildCell(isLight, null, hi.includes(sqId(r, f)));
          grid.appendChild(sq);
          f++;
        }
      } else {
        const isLight = (r + f) % 2 === 0;
        const sq = buildCell(isLight, ch, hi.includes(sqId(r, f)));
        grid.appendChild(sq);
        f++;
      }
    }
  }
  el.appendChild(grid);
}

function renderMiniBoard(el) {
  const raw = el.dataset.mini;
  if (!raw) return;
  const rows = raw.split("/");
  el.innerHTML = "";
  const grid = document.createElement("div");
  grid.className = "board-grid-mini";
  for (let r = 0; r < 3; r++) {
    for (let f = 0; f < 3; f++) {
      const ch = (rows[r] && rows[r][f]) || "_";
      const isLight = (r + f) % 2 === 0;
      const glyph = ch === "_" ? null : ch;
      const sq = buildCell(isLight, glyph, false);
      grid.appendChild(sq);
    }
  }
  el.appendChild(grid);
}

function renderPieceRow(el) {
  const pieces = el.dataset.pieces || "";
  el.innerHTML = "";
  const grid = document.createElement("div");
  grid.className = "piece-row-grid";
  [...pieces].forEach((ch, i) => {
    const isLight = i % 2 === 0;
    const sq = buildCell(isLight, ch, false);
    grid.appendChild(sq);
  });
  el.appendChild(grid);
}

function renderStrip(el) {
  const n = parseInt(el.dataset.strip || "8", 10);
  el.innerHTML = "";
  for (let i = 0; i < n; i++) {
    const s = document.createElement("span");
    s.className = i % 2 === 0 ? "light" : "dark";
    el.appendChild(s);
  }
}

document.querySelectorAll("[data-fen]").forEach(renderBoard);
document.querySelectorAll("[data-mini]").forEach(renderMiniBoard);
document.querySelectorAll("[data-pieces]").forEach(renderPieceRow);
document.querySelectorAll("[data-strip]").forEach(renderStrip);

(function initMagnetic() {
  var prefersReduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (prefersReduced || matchMedia("(hover: none)").matches) return;

  document.querySelectorAll(".btn-magnetic").forEach(function(btn) {
    var rafId = 0, lastX = 0, lastY = 0;
    var onMove = function(e) {
      var rect = btn.getBoundingClientRect();
      lastX = e.clientX - rect.left - rect.width / 2;
      lastY = e.clientY - rect.top - rect.height / 2;
      if (rafId) return;
      rafId = requestAnimationFrame(function() {
        btn.style.transform = "translate(" + (lastX * 0.15) + "px, " + (lastY * 0.15) + "px)";
        rafId = 0;
      });
    };
    btn.addEventListener("mousemove", onMove, { passive: true });
    btn.addEventListener("mouseleave", function() {
      if (rafId) { cancelAnimationFrame(rafId); rafId = 0; }
      btn.style.transform = "";
    });
  });
}());

(function initSpotlight() {
  if (matchMedia("(hover: none)").matches) return;

  document.querySelectorAll(".move-card").forEach(function(card) {
    var rafId = 0, lastX = 0, lastY = 0;
    var onMove = function(e) {
      var rect = card.getBoundingClientRect();
      lastX = e.clientX - rect.left;
      lastY = e.clientY - rect.top;
      if (rafId) return;
      rafId = requestAnimationFrame(function() {
        card.style.setProperty("--mouse-x", lastX + "px");
        card.style.setProperty("--mouse-y", lastY + "px");
        rafId = 0;
      });
    };
    card.addEventListener("mousemove", onMove, { passive: true });
  });
}());

(function pauseHoverDuringScroll() {
  var html = document.documentElement;
  var t = 0;
  window.addEventListener("scroll", function() {
    html.classList.add("is-scrolling");
    clearTimeout(t);
    t = setTimeout(function() { html.classList.remove("is-scrolling"); }, 140);
  }, { passive: true });
}());

