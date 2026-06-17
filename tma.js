(function () {
  const tg = window.Telegram && window.Telegram.WebApp;
  const API = (window.TMA_API || window.CHECKOUT_API || "").replace(/\/+$/, "");
  const plans = {
    "1day": { label: "1 day", amount: 1 },
    "7days": { label: "7 days", amount: 3 },
    monthly: { label: "30 days", amount: 7 },
    "3months": { label: "90 days", amount: 15 },
    yearly: { label: "365 days", amount: 30 },
  };

  const els = {
    auth: document.getElementById("authStatus"),
    message: document.getElementById("message"),
    action: document.getElementById("primaryAction"),
    payment: document.getElementById("paymentPanel"),
    amount: document.getElementById("payAmount"),
    code: document.getElementById("payCode"),
    result: document.getElementById("resultPanel"),
    key: document.getElementById("licenseKey"),
    copy: document.getElementById("copyKey"),
  };

  const state = {
    tier: "monthly",
    sessionId: "",
    payUrl: "",
    pollTimer: 0,
    paid: false,
  };

  function initTelegram() {
    if (!tg || !tg.initData) {
      els.auth.textContent = "Open in Telegram";
      els.auth.className = "status is-err";
      els.action.disabled = true;
      els.message.textContent = "This checkout must be opened from the Telegram bot.";
      return;
    }
    tg.ready();
    tg.expand();
    els.auth.textContent = "Telegram linked";
    els.auth.className = "status is-ok";
  }

  function setMessage(text, kind) {
    els.message.textContent = text;
    els.message.style.color = kind === "err" ? "var(--err)" : "var(--muted)";
  }

  function setTier(tier) {
    state.tier = tier;
    document.querySelectorAll(".plan").forEach((btn) => {
      btn.classList.toggle("is-selected", btn.dataset.tier === tier);
    });
    els.action.textContent = `Continue with ${plans[tier].label}`;
  }

  async function apiPost(path, body) {
    const res = await fetch(`${API}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        ...body,
        initData: tg ? tg.initData : "",
      }),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      throw new Error(data.message || data.error || "Request failed");
    }
    return data;
  }

  async function startCheckout() {
    if (!tg || !tg.initData) {
      setMessage("Open this checkout from the Telegram bot.", "err");
      return;
    }

    els.action.disabled = true;
    els.action.textContent = "Creating checkout...";
    setMessage("Creating a Telegram-linked checkout session.");

    try {
      const data = await apiPost("/api/tma/checkout", { tier: state.tier });
      state.sessionId = data.sessionId;
      state.payUrl = data.payUrl;
      els.amount.textContent = `${data.amount} ${data.currency}`;
      els.code.textContent = data.code;
      els.payment.hidden = false;
      els.action.disabled = false;
      els.action.textContent = "Open DonationAlerts";
      setMessage("Pay with the opened amount and message. The key will arrive here and in chat.");
      pollStatus();
      if (tg.openLink) {
        tg.openLink(data.payUrl);
      } else {
        window.open(data.payUrl, "_blank", "noopener");
      }
    } catch (err) {
      els.action.disabled = false;
      els.action.textContent = `Continue with ${plans[state.tier].label}`;
      setMessage(err.message || "Could not create checkout.", "err");
    }
  }

  async function pollStatus() {
    if (!state.sessionId || state.paid) return;
    window.clearTimeout(state.pollTimer);
    try {
      const data = await apiPost("/api/tma/status", { sessionId: state.sessionId });
      if (data.status === "paid" && data.key) {
        state.paid = true;
        els.key.textContent = data.key;
        els.result.hidden = false;
        els.action.textContent = "Close";
        els.action.disabled = false;
        setMessage("Payment confirmed. The key was also sent to your Telegram chat.");
        return;
      }
      setMessage("Waiting for DonationAlerts payment confirmation...");
    } catch (err) {
      setMessage(err.message || "Could not check payment status.", "err");
    }
    state.pollTimer = window.setTimeout(pollStatus, 5000);
  }

  document.querySelectorAll(".plan").forEach((btn) => {
    btn.addEventListener("click", () => {
      if (state.sessionId) return;
      setTier(btn.dataset.tier);
    });
  });

  els.action.addEventListener("click", () => {
    if (state.paid) {
      if (tg) tg.close();
      return;
    }
    if (state.sessionId && state.payUrl) {
      if (tg && tg.openLink) tg.openLink(state.payUrl);
      else window.open(state.payUrl, "_blank", "noopener");
      return;
    }
    startCheckout();
  });

  els.copy.addEventListener("click", async () => {
    const key = els.key.textContent.trim();
    if (!key) return;
    await navigator.clipboard.writeText(key).catch(() => {});
    setMessage("Key copied.");
  });

  initTelegram();
  setTier(state.tier);
})();
