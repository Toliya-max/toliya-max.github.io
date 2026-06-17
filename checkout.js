(() => {
  const API = typeof window.CHECKOUT_API === "string" ? window.CHECKOUT_API : "";
  const modal = document.getElementById("checkoutModal");
  if (!modal) return;

  const TG_FALLBACK = "https://t.me/LichessBotDownoloaderbot";
  const OFFLINE_MSG =
    "Checkout backend is temporarily offline. Pay via Telegram bot below, same prices, instant key delivery.";

  const steps = {
    form: modal.querySelector('[data-step="form"]'),
    verify: modal.querySelector('[data-step="verify"]'),
    pay: modal.querySelector('[data-step="pay"]'),
    done: modal.querySelector('[data-step="done"]'),
    error: modal.querySelector('[data-step="error"]'),
  };

  const els = {
    tier: document.getElementById("coTier"),
    email: document.getElementById("coEmail"),
    emailHint: document.getElementById("coEmailHint"),
    start: document.getElementById("coStart"),
    error: document.getElementById("coError"),
    poll: document.getElementById("coPoll"),
    code: document.getElementById("coCode"),
    codeSmall: document.getElementById("coCodeSmall"),
    amountUsd: document.getElementById("coAmountUsd"),
    amountInline: document.getElementById("coAmountInline"),
    amountBig: document.getElementById("coAmountBig"),
    amountCopy: document.getElementById("coAmountCopy"),
    tierLabel: document.getElementById("coTierLabel"),
    daLink: document.getElementById("coOpenDA"),
    key: document.getElementById("coKey"),
    copy: document.getElementById("coCopy"),
    verifyEmail: document.getElementById("coVerifyEmail"),
    verifyCode: document.getElementById("coVerifyCode"),
    verifyHint: document.getElementById("coVerifyHint"),
    verifySubmit: document.getElementById("coVerifySubmit"),
    verifyResend: document.getElementById("coVerifyResend"),
    verifyBack: document.getElementById("coVerifyBack"),
  };

  const state = {
    pollTimer: null,
    sessionId: null,
    emailDebounce: null,
    emailToken: 0,
    emailValid: false,
    verifyToken: null,
    resendCooldown: 0,
    resendTimer: null,
    lastFocus: null,
  };

  function showStep(name) {
    Object.entries(steps).forEach(([k, el]) => {
      if (el) el.hidden = k !== name;
    });
  }

  function setEmailHint(text, kind) {
    if (!els.emailHint) return;
    els.emailHint.textContent = text || "";
    els.emailHint.dataset.kind = kind || "";
    els.emailHint.hidden = !text;
  }

  function setVerifyHint(text, kind) {
    if (!els.verifyHint) return;
    els.verifyHint.textContent = text || "";
    els.verifyHint.dataset.kind = kind || "";
    els.verifyHint.hidden = !text;
  }

  function setStartEnabled(enabled, label) {
    if (!els.start) return;
    els.start.disabled = !enabled;
    if (label) {
      els.start.innerHTML = `${label} <span class="arrow">→</span>`;
    }
  }

  function openModal(tierPreset) {
    state.lastFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    modal.hidden = false;
    document.body.style.overflow = "hidden";
    if (tierPreset && els.tier) els.tier.value = tierPreset;
    state.verifyToken = null;
    state.emailValid = false;
    setEmailHint("", "");
    setStartEnabled(false, "Get payment code");
    showStep("form");
    if (els.email && els.email.value) {
      validateEmailNow();
    }
    setTimeout(() => {
      if (els.email) els.email.focus({ preventScroll: true });
    }, 0);
  }

  function closeModal() {
    modal.hidden = true;
    document.body.style.overflow = "";
    if (state.pollTimer) {
      clearInterval(state.pollTimer);
      state.pollTimer = null;
    }
    if (state.resendTimer) {
      clearInterval(state.resendTimer);
      state.resendTimer = null;
    }
    state.sessionId = null;
    if (state.lastFocus && typeof state.lastFocus.focus === "function") {
      state.lastFocus.focus({ preventScroll: true });
    }
    state.lastFocus = null;
  }

  modal.querySelectorAll("[data-close]").forEach((el) =>
    el.addEventListener("click", closeModal),
  );

  document.addEventListener("keydown", (e) => {
    if (modal.hidden) return;
    if (e.key === "Escape") {
      closeModal();
      return;
    }
    if (e.key !== "Tab") return;
    const focusables = Array.from(
      modal.querySelectorAll(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ),
    ).filter((el) => el.offsetParent !== null);
    if (!focusables.length) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  });

  document.querySelectorAll(".buy-btn").forEach((btn) =>
    btn.addEventListener("click", () => openModal(btn.dataset.tier)),
  );

  const EMAIL_RE =
    /^[a-z0-9._%+-]+@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$/i;

  const CLIENT_DISPOSABLE = new Set([
    "10minutemail.com", "10minutemail.net", "20minutemail.com", "33mail.com",
    "anonbox.net", "armyspy.com", "bccto.me", "cuvox.de", "dayrep.com",
    "deadaddress.com", "discard.email", "discardmail.com", "dispostable.com",
    "dodgit.com", "dodgemail.com", "donemail.ru", "dropmail.me", "duck2.club",
    "easytrashmail.com", "einrot.com", "emailfake.com", "emailondeck.com",
    "emailtemporanea.com", "emailtemporario.com.br", "fake-mail.net",
    "fake-mail.ml", "fakeinbox.com", "fakemailgenerator.com", "fastmail.es",
    "filzmail.com", "fleckens.hu", "freemailnow.net", "gmial.com", "gmal.com",
    "gmile.com", "gmmail.com", "grr.la", "guerrillamail.biz", "guerrillamail.com",
    "guerrillamail.de", "guerrillamail.info", "guerrillamail.net",
    "guerrillamail.org", "guerrillamailblock.com", "harakirimail.com",
    "hotmial.com", "hidemail.de", "imails.info", "incognitomail.com",
    "instantemailaddress.com", "jetable.org", "jourrapide.com", "junk.com",
    "kasmail.com", "kurzepost.de", "letthemeatspam.com", "mailcatch.com",
    "maildrop.cc", "maildu.de", "mailexpire.com", "mailforspam.com",
    "mailfreeonline.com", "mailimate.com", "mailinator.com", "mailinator.net",
    "mailinator.org", "mailmoat.com", "mailnesia.com", "mailnull.com",
    "mailtothis.com", "mintemail.com", "mohmal.com", "moakt.com",
    "mt2015.com", "mt2014.com", "mvrht.com", "mytemp.email", "mytrashmail.com",
    "nada.email", "no-spam.ws", "nope.email", "notmailinator.com",
    "objectmail.com", "obobbo.com", "odnorazovoe.ru", "onewaymail.com",
    "owlpic.com", "pjjkp.com", "rcpt.at", "rmqkr.net", "rppkn.com",
    "sharklasers.com", "shieldemail.com", "shitmail.me", "snakemail.com",
    "spam4.me", "spamavert.com", "spambog.com", "spambog.de", "spambog.ru",
    "spambox.us", "spamcero.com", "spamdecoy.net", "spamfree24.com",
    "spamfree24.de", "spamfree24.eu", "spamfree24.info", "spamfree24.net",
    "spamfree24.org", "spamgourmet.com", "spamherelots.com", "spamhereplease.com",
    "spaminator.de", "spammotel.com", "spamobox.com", "spamspot.com",
    "spamthis.co.uk", "spamthisplease.com", "spoofmail.de", "stuffmail.de",
    "supergreatmail.com", "supermailer.jp", "suremail.info", "tafmail.com",
    "tempemail.com", "tempemail.net", "tempinbox.co.uk", "tempinbox.com",
    "tempmail.co", "tempmail.io", "tempmail.it", "tempmail.us", "tempmail.de",
    "tempmail.email", "tempmail.eu", "tempmail.org", "tempmail.plus",
    "tempmail2.com", "tempmaildemand.com", "tempmaileo.com", "tempmailer.com",
    "tempmailer.de", "tempymail.com", "thankyou2010.com", "thisisnotmyrealemail.com",
    "throwam.com", "throwawayemailaddresses.com", "throwawaymail.com",
    "tmpeml.info", "tmpmail.org", "tmpmail.net", "trashmail.at", "trashmail.com",
    "trashmail.de", "trashmail.io", "trashmail.me", "trashmail.net",
    "trashmail.org", "trashmail.ws", "trashmailer.com", "trbvm.com",
    "trialmail.de", "tyldd.com", "uggsrock.com", "uplipht.com", "venompen.com",
    "veryrealemail.com", "viditag.com", "viewcastmedia.com", "viewcastmedia.net",
    "viewcastmedia.org", "vmailing.info", "vmpanda.com", "vsimcard.com",
    "vubby.com", "wegwerf-emails.de", "wegwerfemail.com", "wegwerfemail.de",
    "wegwerfemail.net", "wegwerfemail.org", "wegwerfmail.de", "wegwerfmail.info",
    "wegwerfmail.net", "wegwerfmail.org", "wh4f.org", "whyspam.me",
    "willhackforfood.biz", "willselfdestruct.com", "winemaven.info",
    "wronghead.com", "wuzup.net", "wuzupmail.net", "xoxy.net", "yapped.net",
    "yeah.net", "yep.it", "yopmail.com", "yopmail.fr", "yopmail.net",
    "yourdomain.com", "ypmail.webarnak.fr.eu.org", "yuurok.com", "zehnminuten.de",
    "zehnminutenmail.de", "zoaxe.com", "zoemail.org",
  ]);

  const ROLE_LOCALPARTS = new Set([
    "admin", "administrator", "abuse", "billing", "compliance", "contact",
    "help", "hostmaster", "info", "mail", "marketing", "no-reply", "noreply",
    "no_reply", "office", "postmaster", "privacy", "root", "sales", "security",
    "spam", "support", "sysadmin", "test", "tests", "user", "webmaster",
  ]);

  function clientClassifyEmail(raw) {
    const email = (raw || "").trim().toLowerCase();
    if (!email) return { ok: false, code: "empty", message: "Email is required." };
    if (email.length > 254) return { ok: false, code: "too_long", message: "Email is too long." };
    if (!EMAIL_RE.test(email)) return { ok: false, code: "format", message: "That doesn't look like a valid email." };
    const at = email.lastIndexOf("@");
    const local = email.slice(0, at);
    const domain = email.slice(at + 1);
    if (local.length > 64) return { ok: false, code: "format", message: "Local part is too long." };
    if (CLIENT_DISPOSABLE.has(domain)) {
      return { ok: false, code: "disposable", message: "Disposable / temporary email providers are not accepted." };
    }
    const parent = domain.split(".").slice(-2).join(".");
    if (parent && parent !== domain && CLIENT_DISPOSABLE.has(parent)) {
      return { ok: false, code: "disposable", message: "Disposable / temporary email providers are not accepted." };
    }
    if (ROLE_LOCALPARTS.has(local)) {
      return { ok: false, code: "role_address", message: "Use a personal address, not a role inbox." };
    }
    const cleaned = local.replace(/[._+-]/g, "");
    if (cleaned.length < 2) {
      return { ok: false, code: "junk_local", message: "Please use a real email. Random strings won't reach you." };
    }
    if (/^[a-z]+$/.test(cleaned) && cleaned.length <= 2) {
      return { ok: false, code: "junk_local", message: "Please use a real email. Random strings won't reach you." };
    }
    if (/^([a-z])\1{4,}$/.test(cleaned)) {
      return { ok: false, code: "junk_local", message: "Please use a real email. Random strings won't reach you." };
    }
    if (/^(qwer|asdf|zxcv|qaz|wsx|edc|qwe|abc|aaa)/i.test(cleaned)) {
      return { ok: false, code: "junk_local", message: "Please use a real email. Random strings won't reach you." };
    }
    return { ok: true, code: "client_ok" };
  }

  async function serverCheckEmail(email) {
    if (!API && API !== "") return null;
    try {
      const r = await fetch(`${API}/api/email-check`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email }),
      });
      if (!r.ok && r.status >= 500) return null;
      const data = await r.json();
      return data;
    } catch {
      return null;
    }
  }

  async function validateEmailNow() {
    const raw = (els.email && els.email.value) || "";
    const local = clientClassifyEmail(raw);
    if (!local.ok) {
      state.emailValid = false;
      state.verifyToken = null;
      setEmailHint(local.message, "err");
      setStartEnabled(false, "Get payment code");
      return;
    }
    setEmailHint("Checking…", "info");
    const myToken = ++state.emailToken;
    const server = await serverCheckEmail(raw.trim().toLowerCase());
    if (myToken !== state.emailToken) return;
    if (server && server.ok === false) {
      state.emailValid = false;
      state.verifyToken = null;
      setEmailHint(server.message || "This email isn't accepted.", "err");
      setStartEnabled(false, "Get payment code");
      return;
    }
    state.emailValid = true;
    if (server && server.code === "ok") {
      setEmailHint("Email looks good.", "ok");
    } else if (server && server.code === "ok_unverified") {
      setEmailHint("Email accepted (DNS check skipped).", "info");
    } else {
      setEmailHint("Email format OK.", "ok");
    }
    setStartEnabled(true, "Get payment code");
  }

  function scheduleEmailValidation() {
    if (state.emailDebounce) clearTimeout(state.emailDebounce);
    state.emailValid = false;
    state.verifyToken = null;
    setStartEnabled(false, "Get payment code");
    setEmailHint("", "");
    state.emailDebounce = setTimeout(validateEmailNow, 450);
  }

  if (els.email) {
    els.email.addEventListener("input", scheduleEmailValidation);
    els.email.addEventListener("blur", () => {
      if (state.emailDebounce) clearTimeout(state.emailDebounce);
      validateEmailNow();
    });
  }

  function showOfflineError() {
    if (els.error) els.error.textContent = OFFLINE_MSG;
    showStep("error");
  }

  function showError(message) {
    if (els.error) {
      els.error.textContent =
        message ||
        "Something went wrong. Please try again, or pay via Telegram below.";
    }
    showStep("error");
  }

  function isNetworkErrorObject(e) {
    if (!e) return false;
    if (e instanceof TypeError) return true;
    const msg = String((e && e.message) || e || "").toLowerCase();
    return /failed to fetch|networkerror|load failed|aborted|timed? out|ssl|cors/i.test(
      msg,
    );
  }

  function startResendCooldown(seconds) {
    state.resendCooldown = seconds;
    if (state.resendTimer) clearInterval(state.resendTimer);
    const tick = () => {
      if (!els.verifyResend) return;
      if (state.resendCooldown <= 0) {
        clearInterval(state.resendTimer);
        state.resendTimer = null;
        els.verifyResend.disabled = false;
        els.verifyResend.textContent = "Resend code";
        return;
      }
      els.verifyResend.disabled = true;
      els.verifyResend.textContent = `Resend in ${state.resendCooldown}s`;
      state.resendCooldown--;
    };
    tick();
    state.resendTimer = setInterval(tick, 1000);
  }

  function classifyApiResponse(r, data) {
    if (r.status >= 500) return { offline: true };
    if (data && data.error === "backend_offline") return { offline: true };
    return { offline: false };
  }

  async function requestVerificationCode(email) {
    try {
      const r = await fetch(`${API}/api/email-send-code`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email }),
      });
      const data = await r.json().catch(() => ({}));
      const cls = classifyApiResponse(r, data);
      if (cls.offline) return { ok: false, network: true, message: OFFLINE_MSG };
      if (!r.ok) {
        return { ok: false, message: (data && data.message) || "Couldn't send code." };
      }
      return data;
    } catch (e) {
      if (isNetworkErrorObject(e)) {
        return { ok: false, network: true, message: OFFLINE_MSG };
      }
      return { ok: false, message: "Couldn't send code." };
    }
  }

  async function submitVerificationCode(email, code) {
    try {
      const r = await fetch(`${API}/api/email-verify-code`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, code }),
      });
      const data = await r.json().catch(() => ({}));
      const cls = classifyApiResponse(r, data);
      if (cls.offline) return { ok: false, network: true, message: OFFLINE_MSG };
      if (!r.ok) {
        return { ok: false, message: (data && data.message) || "Verification failed." };
      }
      return data;
    } catch (e) {
      if (isNetworkErrorObject(e)) {
        return { ok: false, network: true, message: OFFLINE_MSG };
      }
      return { ok: false, message: "Verification failed." };
    }
  }

  async function enterVerificationStep(email) {
    if (els.verifyEmail) els.verifyEmail.textContent = email;
    if (els.verifyCode) els.verifyCode.value = "";
    setVerifyHint("Sending code…", "info");
    showStep("verify");
    const sent = await requestVerificationCode(email);
    if (sent && sent.verification_disabled) {
      setVerifyHint("", "");
      await proceedToPay(email, null);
      return;
    }
    if (!sent || !sent.ok) {
      if (sent && sent.network) {
        showOfflineError();
        return;
      }
      setVerifyHint(sent && sent.message ? sent.message : "Couldn't send code.", "err");
      return;
    }
    setVerifyHint(sent.message || "Code sent. Check your inbox.", "ok");
    startResendCooldown(45);
  }

  async function handleVerifySubmit() {
    const email = (els.verifyEmail && els.verifyEmail.textContent) || "";
    const code = ((els.verifyCode && els.verifyCode.value) || "").trim();
    if (!/^\d{6}$/.test(code)) {
      setVerifyHint("Enter the 6-digit code from the email.", "err");
      return;
    }
    if (els.verifySubmit) els.verifySubmit.disabled = true;
    setVerifyHint("Checking…", "info");
    const res = await submitVerificationCode(email, code);
    if (els.verifySubmit) els.verifySubmit.disabled = false;
    if (res && res.network) {
      showOfflineError();
      return;
    }
    if (!res || !res.ok) {
      setVerifyHint(res && res.message ? res.message : "Wrong code.", "err");
      return;
    }
    state.verifyToken = res.token || null;
    setVerifyHint("Verified.", "ok");
    await proceedToPay(email, state.verifyToken);
  }

  async function handleResend() {
    const email = (els.verifyEmail && els.verifyEmail.textContent) || "";
    if (!email) return;
    setVerifyHint("Resending…", "info");
    const sent = await requestVerificationCode(email);
    if (sent && sent.network) {
      showOfflineError();
      return;
    }
    if (!sent || !sent.ok) {
      setVerifyHint(sent && sent.message ? sent.message : "Couldn't resend.", "err");
      return;
    }
    setVerifyHint(sent.message || "New code sent.", "ok");
    startResendCooldown(45);
  }

  function handleVerifyBack() {
    showStep("form");
  }

  if (els.verifySubmit) els.verifySubmit.addEventListener("click", handleVerifySubmit);
  if (els.verifyResend) els.verifyResend.addEventListener("click", handleResend);
  if (els.verifyBack) els.verifyBack.addEventListener("click", handleVerifyBack);
  if (els.verifyCode) {
    els.verifyCode.addEventListener("keydown", (e) => {
      if (e.key === "Enter") handleVerifySubmit();
    });
  }

  async function proceedToPay(email, verifyToken) {
    const tier = els.tier ? els.tier.value : "monthly";
    setStartEnabled(false, "Working…");
    showStep("form");
    setEmailHint("Creating session…", "info");
    try {
      const r = await fetch(`${API}/api/checkout`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tier, email, verifyToken: verifyToken || undefined }),
      });
      if (!r.ok) {
        if (r.status >= 500) {
          showOfflineError();
          return;
        }
        let data = null;
        try { data = await r.json(); } catch {}
        if (data && data.error === "backend_offline") {
          showOfflineError();
          return;
        }
        if (data && data.error === "email_not_verified") {
          state.verifyToken = null;
          await enterVerificationStep(email);
          return;
        }
        if (data && data.error === "invalid_email") {
          setEmailHint(data.message || "Email not accepted.", "err");
          setStartEnabled(false, "Get payment code");
          return;
        }
        showError(
          (data && data.message) ||
            "Couldn't create checkout. Try again or pay via Telegram below.",
        );
        return;
      }
      const data = await r.json();
      state.sessionId = data.sessionId;

      const amountStr = Number(data.amount).toFixed(2);
      const price = "$" + amountStr;
      const setText = (el, val) => { if (el) el.textContent = val; };
      setText(els.code, data.code);
      setText(els.codeSmall, data.code);
      setText(els.amountUsd, price);
      setText(els.amountInline, price);
      setText(els.amountBig, price);
      setText(els.amountCopy, amountStr);
      setText(els.tierLabel, data.label);
      if (els.daLink) els.daLink.href = data.payUrl;

      showStep("pay");
      startPolling(data.sessionId);
    } catch (e) {
      if (isNetworkErrorObject(e)) {
        showOfflineError();
      } else {
        showError("Couldn't create checkout. Please try again or use Telegram below.");
      }
    } finally {
      setStartEnabled(state.emailValid, "Get payment code");
    }
  }

  async function startCheckout() {
    if (!state.emailValid) {
      validateEmailNow();
      return;
    }
    const email = (els.email && els.email.value || "").trim().toLowerCase();
    setStartEnabled(false, "Working…");
    if (state.verifyToken) {
      await proceedToPay(email, state.verifyToken);
      setStartEnabled(state.emailValid, "Get payment code");
      return;
    }
    await enterVerificationStep(email);
    setStartEnabled(state.emailValid, "Get payment code");
  }

  function startPolling(sessionId) {
    if (state.pollTimer) clearInterval(state.pollTimer);
    let ticks = 0;

    const tick = async () => {
      ticks++;
      try {
        const r = await fetch(`${API}/api/status?session=${sessionId}`);
        const data = await r.json();
        if (data.status === "paid" && data.key) {
          clearInterval(state.pollTimer);
          state.pollTimer = null;
          if (els.key) els.key.textContent = data.key;
          showStep("done");
        } else if (data.status === "expired" || r.status === 404) {
          clearInterval(state.pollTimer);
          state.pollTimer = null;
          showError("This checkout session has expired. Please start over.");
        } else if (els.poll) {
          const mins = Math.floor((ticks * 3) / 60);
          const secs = (ticks * 3) % 60;
          els.poll.textContent = `Waiting for payment… ${mins}m ${secs}s`;
        }
      } catch (e) {
        if (els.poll) {
          els.poll.textContent = isNetworkErrorObject(e)
            ? "Connection hiccup, retrying..."
            : "Connection hiccup, retrying...";
        }
      }
    };

    state.pollTimer = setInterval(tick, 3000);
    tick();
  }

  if (els.start) {
    els.start.addEventListener("click", startCheckout);
  }

  if (els.copy) {
    els.copy.addEventListener("click", () => {
      const key = (els.key && els.key.textContent) || "";
      if (!key) return;
      navigator.clipboard?.writeText(key).then(
        () => {
          const old = els.copy.textContent;
          els.copy.textContent = "Copied!";
          setTimeout(() => (els.copy.textContent = old), 1500);
        },
        () => {},
      );
    });
  }

  modal.querySelectorAll(".pay-copy").forEach((btn) => {
    btn.addEventListener("click", () => {
      const targetId = btn.dataset.copyTarget;
      const target = targetId ? document.getElementById(targetId) : null;
      const text = (target && target.textContent && target.textContent.trim()) || "";
      if (!text) return;
      navigator.clipboard?.writeText(text).then(
        () => {
          const old = btn.textContent;
          btn.textContent = "Copied!";
          btn.classList.add("pay-copy--done");
          setTimeout(() => {
            btn.textContent = old;
            btn.classList.remove("pay-copy--done");
          }, 1500);
        },
        () => {},
      );
    });
  });

  const tgLink = modal.querySelector(".btn-link");
  if (tgLink && !tgLink.href.includes("t.me")) {
    tgLink.href = TG_FALLBACK;
  }
})();
