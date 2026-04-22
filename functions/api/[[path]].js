const LATEST_URL = "https://raw.githubusercontent.com/Toliya-max/toliya-max.github.io/main/latest.json";

async function getBackendApi() {
  try {
    const r = await fetch(LATEST_URL, { cf: { cacheTtl: 15 } });
    if (!r.ok) return null;
    const j = await r.json();
    return (j.api || "").replace(/\/+$/, "") || null;
  } catch {
    return null;
  }
}

export async function onRequest(context) {
  const { request } = context;
  const url = new URL(request.url);

  if (request.method === "OPTIONS") {
    return new Response(null, {
      status: 204,
      headers: {
        "access-control-allow-origin": "*",
        "access-control-allow-methods": "GET, POST, OPTIONS",
        "access-control-allow-headers": "Content-Type",
        "access-control-max-age": "86400",
      },
    });
  }

  const api = await getBackendApi();
  if (!api) {
    return new Response(JSON.stringify({ error: "backend unavailable" }), {
      status: 503,
      headers: {
        "content-type": "application/json",
        "access-control-allow-origin": "*",
      },
    });
  }

  const targetUrl = api + url.pathname + url.search;
  const init = {
    method: request.method,
    headers: request.headers,
    body: ["GET", "HEAD"].includes(request.method) ? undefined : request.body,
    redirect: "follow",
  };

  const resp = await fetch(targetUrl, init);
  const headers = new Headers(resp.headers);
  headers.set("access-control-allow-origin", "*");
  headers.set("access-control-allow-methods", "GET, POST, OPTIONS");
  headers.set("access-control-allow-headers", "Content-Type");
  return new Response(resp.body, { status: resp.status, headers });
}
