const RAW_LATEST = "https://raw.githubusercontent.com/Toliya-max/toliya-max.github.io/main/latest.json";

export async function onRequest({ request, next }) {
  const url = new URL(request.url);
  if (url.pathname === "/latest.json") {
    try {
      const r = await fetch(RAW_LATEST, { cf: { cacheTtl: 15 } });
      const body = await r.text();
      return new Response(body, {
        status: r.status,
        headers: {
          "content-type": "application/json; charset=utf-8",
          "cache-control": "public, max-age=15",
          "access-control-allow-origin": "*",
        },
      });
    } catch {
      return next();
    }
  }
  return next();
}
