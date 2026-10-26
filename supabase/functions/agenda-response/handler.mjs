const origins = new Set(["https://sbento.com.br", "https://www.sbento.com.br"]);
const headers = { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", "Referrer-Policy": "no-referrer", "X-Content-Type-Options": "nosniff" };

export function createHandler(respond) {
  return async function handler(req) {
    const origin = req.headers.get("origin") ?? "";
    const cors = origins.has(origin) ? { "Access-Control-Allow-Origin": origin, "Vary": "Origin" } : {};
    const result = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { ...headers, ...cors } });
    if (!origins.has(origin)) return result({ status: "unavailable" }, 403);
    if (req.method === "OPTIONS") return new Response(null, { status: 204, headers: { ...headers, ...cors, "Access-Control-Allow-Methods": "POST", "Access-Control-Allow-Headers": "content-type" } });
    if (req.method !== "POST") return result({ status: "unavailable" }, 405);
    if (!req.headers.get("content-type")?.startsWith("application/json")) return result({ status: "unavailable" }, 415);
    if (Number(req.headers.get("content-length")) > 8192) return result({ status: "unavailable" }, 413);
    try {
      // Bound streaming bodies too; Content-Length is not trusted.
      const reader = req.body?.getReader();
      if (!reader) return result({ status: "unavailable" }, 400);
      let size = 0; const parts = [];
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        size += value.byteLength;
        if (size > 8192) { await reader.cancel(); return result({ status: "unavailable" }, 413); }
        parts.push(value);
      }
      const bytes = new Uint8Array(size); let offset = 0;
      for (const part of parts) { bytes.set(part, offset); offset += part.byteLength; }
      const input = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
      if (!input || typeof input !== "object" || Array.isArray(input) ||
          typeof input.token !== "string" || !/^[0-9a-f]{64}$/.test(input.token) ||
          !["lookup", "confirm", "cancel", "feedback"].includes(input.operation) ||
          (input.operation === "feedback" && (!Number.isInteger(input.score) || input.score < 0 || input.score > 10)) ||
          (input.note !== undefined && (typeof input.note !== "string" || input.note.length > 4000))) {
        return result({ status: "unavailable" }, 400);
      }
      const data = await respond({ p_token: input.token, p_operation: input.operation,
        p_score: input.operation === "feedback" ? input.score : null,
        p_note: input.operation === "feedback" ? (input.note ?? "") : "" });
      // Explicit allowlist: no database row, contact, token or server error can leave this boundary.
      if (data?.status === "recorded") return result({ status: "recorded" });
      if (data?.status !== "available") return result({ status: "unavailable" });
      if (data.kind === "pos_venda") return result({ status: "available", kind: "pos_venda" });
      if (data.kind !== "confirmacao") return result({ status: "unavailable" });
      return result({ status: "available", kind: "confirmacao",
        service: typeof data.service === "string" ? data.service.slice(0, 300) : "",
        date: /^\d{4}-\d{2}-\d{2}$/.test(data.date) ? data.date : "",
        time: /^\d{2}:\d{2}$/.test(data.time) ? data.time : "" });
    } catch {
      return result({ status: "unavailable" }, 400);
    }
  };
}
