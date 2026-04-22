(() => {
  let API = window.CHECKOUT_API || "";
  let apiResolved = !!API;

  async function ensureApi() {
    if (apiResolved) return API;
    try {
      const r = await fetch("latest.json", { cache: "no-store" });
      const j = await r.json();
      API = (j.api || "").replace(/\/+$/, "");
      window.CHECKOUT_API = API;
    } catch {}
    apiResolved = true;
    return API;
  }

  const modal = document.getElementById("checkoutModal");
  if (!modal) return;

  const steps = {
    form: modal.querySelector('[data-step="form"]'),
    pay: modal.querySelector('[data-step="pay"]'),
    done: modal.querySelector('[data-step="done"]'),
    error: modal.querySelector('[data-step="error"]'),
  };

  let pollTimer = null;
  let currentSession = null;

  function showStep(name) {
    Object.entries(steps).forEach(([k, el]) => {
      if (el) el.hidden = k !== name;
    });
  }

  function openModal(tierPreset) {
    modal.hidden = false;
    document.body.style.overflow = "hidden";
    if (tierPreset) document.getElementById("coTier").value = tierPreset;
    showStep("form");
  }

  function closeModal() {
    modal.hidden = true;
    document.body.style.overflow = "";
    if (pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
    currentSession = null;
  }

  modal.querySelectorAll("[data-close]").forEach((el) =>
    el.addEventListener("click", closeModal)
  );

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && !modal.hidden) closeModal();
  });

  document.querySelectorAll(".buy-btn").forEach((btn) =>
    btn.addEventListener("click", () => openModal(btn.dataset.tier))
  );

  const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

  function showEmailErr(msg) {
    const el = document.getElementById("coEmailErr");
    const input = document.getElementById("coEmail");
    if (el) {
      el.textContent = msg;
      el.hidden = false;
    }
    if (input) {
      input.classList.add("is-invalid");
      input.focus();
    }
  }

  function clearEmailErr() {
    const el = document.getElementById("coEmailErr");
    const input = document.getElementById("coEmail");
    if (el) el.hidden = true;
    if (input) input.classList.remove("is-invalid");
  }

  document.getElementById("coEmail")?.addEventListener("input", clearEmailErr);

  async function startCheckout() {
    const tier = document.getElementById("coTier").value;
    const email = document.getElementById("coEmail").value.trim();
    const btn = document.getElementById("coStart");

    if (!email) {
      showEmailErr("Email is required — we send your key there.");
      return;
    }
    if (!EMAIL_RE.test(email) || email.length > 200) {
      showEmailErr("Please enter a valid email address.");
      return;
    }
    clearEmailErr();

    btn.disabled = true;
    btn.textContent = "Working…";

    await ensureApi();
    if (!API) {
      showError("Checkout API is not configured yet. Please use the Telegram bot.");
      return;
    }

    try {
      const r = await fetch(`${API}/api/checkout`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tier, email }),
      });
      if (!r.ok) {
        throw new Error(`HTTP ${r.status}`);
      }
      const data = await r.json();
      currentSession = data.sessionId;

      const price = "$" + data.amount;
      const setText = (id, val) => {
        const el = document.getElementById(id);
        if (el) el.textContent = val;
      };
      setText("coCode", data.code);
      setText("coCodeSmall", data.code);
      setText("coAmount", String(data.amount));
      setText("coAmountUsd", price);
      setText("coAmountInline", price);
      setText("coTierLabel", data.label);
      const daLink = document.getElementById("coOpenDA");
      if (daLink) daLink.href = data.payUrl;

      showStep("pay");
      startPolling(data.sessionId);
    } catch (e) {
      showError(`Couldn't create checkout: ${e.message}`);
    } finally {
      btn.disabled = false;
      btn.innerHTML = 'Get payment code <span class="arrow">→</span>';
    }
  }

  function startPolling(sessionId) {
    if (pollTimer) clearInterval(pollTimer);
    const pollEl = document.getElementById("coPoll");
    let ticks = 0;

    const tick = async () => {
      ticks++;
      try {
        const r = await fetch(`${API}/api/status?session=${sessionId}`);
        const data = await r.json();
        if (data.status === "paid" && data.key) {
          clearInterval(pollTimer);
          pollTimer = null;
          document.getElementById("coKey").textContent = data.key;
          showStep("done");
        } else if (data.status === "expired" || r.status === 404) {
          clearInterval(pollTimer);
          pollTimer = null;
          showError("This checkout session has expired. Please start over.");
        } else {
          const mins = Math.floor((ticks * 3) / 60);
          const secs = (ticks * 3) % 60;
          pollEl.textContent = `Waiting for payment… ${mins}m ${secs}s`;
        }
      } catch (e) {
        pollEl.textContent = `Connection hiccup — retrying… (${e.message})`;
      }
    };

    pollTimer = setInterval(tick, 3000);
    tick();
  }

  function showError(message) {
    document.getElementById("coError").textContent = message;
    showStep("error");
  }

  document.getElementById("coStart").addEventListener("click", startCheckout);

  document.getElementById("coCopy").addEventListener("click", () => {
    const key = document.getElementById("coKey").textContent;
    navigator.clipboard?.writeText(key).then(
      () => {
        const btn = document.getElementById("coCopy");
        const old = btn.textContent;
        btn.textContent = "Copied!";
        setTimeout(() => (btn.textContent = old), 1500);
      },
      () => {}
    );
  });
})();
