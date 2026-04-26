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
  if (prefersReduced) return;

  document.querySelectorAll(".btn-magnetic").forEach(function(btn) {
    btn.addEventListener("mousemove", function(e) {
      var rect = btn.getBoundingClientRect();
      var x = e.clientX - rect.left - rect.width / 2;
      var y = e.clientY - rect.top - rect.height / 2;
      btn.style.transform = "translate(" + (x * 0.15) + "px, " + (y * 0.15) + "px)";
    });
    btn.addEventListener("mouseleave", function() {
      btn.style.transform = "";
    });
  });
}());

(function initSpotlight() {
  document.querySelectorAll(".move-card").forEach(function(card) {
    card.addEventListener("mousemove", function(e) {
      var rect = card.getBoundingClientRect();
      card.style.setProperty("--mouse-x", (e.clientX - rect.left) + "px");
      card.style.setProperty("--mouse-y", (e.clientY - rect.top) + "px");
    });
  });
}());

(function initScrollReveal() {
  const prefersReduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (prefersReduced) return;

  const targets = document.querySelectorAll(
    ".move-card, .faq-row, .verdict, .section-head, .about .two-col > div, .endgame-inner > *"
  );

  const style = document.createElement("style");
  style.textContent = `.sr-hidden { opacity: 0; transform: translateY(22px); transition: opacity 480ms cubic-bezier(0.16,1,0.3,1), transform 480ms cubic-bezier(0.16,1,0.3,1); } .sr-hidden.sr-visible { opacity: 1; transform: translateY(0); }`;
  document.head.appendChild(style);

  targets.forEach(function(el) { el.classList.add("sr-hidden"); });

  const io = new IntersectionObserver(function(entries) {
    entries.forEach(function(entry) {
      if (!entry.isIntersecting) return;
      const siblings = Array.from(entry.target.parentElement.querySelectorAll(".sr-hidden:not(.sr-visible)"));
      const idx = siblings.indexOf(entry.target);
      setTimeout(function() {
        entry.target.classList.add("sr-visible");
      }, Math.min(idx * 60, 240));
      io.unobserve(entry.target);
    });
  }, { threshold: 0.08, rootMargin: "0px 0px -40px 0px" });

  targets.forEach(function(el) { io.observe(el); });
}());
