import test from "node:test";
import assert from "node:assert/strict";
import { createHandler } from "./handler.mjs";
const token = "a".repeat(64);
const request = (data, extra = {}) => new Request("https://example.invalid/agenda-response", {
  method: "POST", headers: { origin: "https://sbento.com.br", "content-type": "application/json" },
  body: JSON.stringify(data), ...extra
});
test("previews, methods and unrelated origins never reach the database", async () => {
  let calls = 0; const handler = createHandler(() => { calls++; });
  for (const method of ["GET", "HEAD", "DELETE"]) {
    assert.equal((await handler(new Request("https://example.invalid/?token=" + token,
      { method, headers: { origin: "https://sbento.com.br" } }))).status, 405);
  }
  assert.equal((await handler(request({ token, operation: "confirm" }, { headers: { origin: "https://attacker.invalid", "content-type": "application/json" } }))).status, 403);
  assert.equal((await handler(new Request("https://example.invalid/", { method: "OPTIONS", headers: { origin: "https://sbento.com.br" } }))).status, 204);
  assert.equal(calls, 0);
});
test("input validation fails closed without consuming capabilities", async () => {
  let calls = 0; const handler = createHandler(() => { calls++; });
  for (const data of [null, [], {}, { token, operation: null }, { token: "a".repeat(63), operation: "confirm" },
    { token, operation: "delete" }, { token, operation: "feedback", score: -1 },
    { token, operation: "feedback", score: 11 }, { token, operation: "feedback", score: 1.5 },
    { token, operation: "feedback", score: "9" }, { token, operation: "feedback", score: 9, note: 10 },
    { token, operation: "feedback", score: 9, note: "x".repeat(4001) }]) {
    assert.equal((await handler(request(data))).status, 400);
  }
  assert.equal((await handler(request({ token, operation: "confirm" }, { body: "x".repeat(9000) }))).status, 413);
  assert.equal((await handler(request({ token, operation: "confirm" }, { body: "{" }))).status, 400);
  assert.equal(calls, 0);
});
test("lookup is read-only and output excludes contacts, credentials and tokens", async () => {
  let args;
  const handler = createHandler(input => { args = input; return { status: "available", kind: "confirmacao",
    date: "2026-10-27", time: "10:00", service: "<script>test</script>", phone: "private",
    token, secret: "server-only", client: "private" }; });
  const response = await handler(request({ token, operation: "lookup" }));
  assert.equal(response.headers.get("cache-control"), "no-store");
  assert.equal(response.headers.get("access-control-allow-origin"), "https://sbento.com.br");
  assert.deepEqual(args, { p_token: token, p_operation: "lookup", p_score: null, p_note: "" });
  const data = await response.json();
  assert.deepEqual(Object.keys(data).sort(), ["date", "kind", "service", "status", "time"]);
  assert.equal(data.date, "2026-10-27");
});
test("feedback preserves zero and passes only validated response fields", async () => {
  let args;
  const handler = createHandler(input => { args = input; return { status: "recorded", token }; });
  const response = await handler(request({ token, operation: "feedback", score: 0, note: "Teste" }));
  assert.equal(args.p_score, 0); assert.equal(args.p_note, "Teste");
  assert.deepEqual(await response.json(), { status: "recorded" });
});
test("server failures never echo capability or internal error", async () => {
  const handler = createHandler(() => { throw new Error("secret " + token); });
  const response = await handler(request({ token, operation: "confirm" }));
  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { status: "unavailable" });
});
