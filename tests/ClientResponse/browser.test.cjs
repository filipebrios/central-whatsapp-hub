const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || "playwright");
(async () => {
  const browser = await chromium.launch({ headless: true });
  const errors = [];
  const token = "a".repeat(64);
  let calls = [], reply = { status: "available", kind: "confirmacao", service: "<img src=x onerror=alert(1)>", date: "2026-10-27", time: "10:00" };
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(error.message));
  await page.route("**/*", async route => {
    const url = new URL(route.request().url());
    if (url.origin === "https://snypbekmjseayljvltwq.supabase.co") {
      if (route.request().method() === "OPTIONS") return route.fulfill({ status: 204,
        headers: { "access-control-allow-origin": "https://www.sbento.com.br", "access-control-allow-methods": "POST", "access-control-allow-headers": "content-type" } });
      const data = route.request().postDataJSON(); calls.push(data);
      return route.fulfill({ status: 200, contentType: "application/json",
        headers: { "access-control-allow-origin": "https://www.sbento.com.br" },
        body: JSON.stringify(data.operation === "lookup" ? reply : { status: "recorded" }) });
    }
    const types = { ".html": "text/html", ".css": "text/css", ".mjs": "text/javascript" };
    const filename = path.basename(url.pathname);
    if (url.origin === "https://www.sbento.com.br" && /^modux-resposta\.(html|css|mjs)$/.test(filename))
      return route.fulfill({ contentType: types[path.extname(filename)],
        body: await fs.readFile(path.join(__dirname, "../../client-response", filename), "utf8") });
    throw new Error("Unexpected external request: " + url.origin);
  });
  const open = async (hash = token) => {
    calls = [];
    await page.goto("https://www.sbento.com.br/modux-resposta.html" + (hash ? "#token=" + hash : ""));
    await page.waitForFunction(() => !document.getElementById("status").textContent.includes("Conferindo"));
  };
  try {
    await open("");
    assert.equal(calls.length, 0);
    assert.equal(await page.locator("#confirmation").isVisible(), false);
    await open();
    assert.equal(calls.length, 1); assert.equal(calls[0].operation, "lookup");
    assert.equal(new URL(page.url()).hash, "");
    assert.equal(await page.locator("#service").textContent(), reply.service);
    assert.equal(await page.locator("#service img").count(), 0);
    assert.equal(await page.locator("#schedule").textContent(), "27/10/2026 às 10:00");
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    if (process.env.MODUX_SCREENSHOTS) await page.screenshot({ path: process.env.MODUX_SCREENSHOTS + "/confirmacao-mobile.png", fullPage: true });
    await page.locator("#confirm").click();
    await page.waitForFunction(() => document.getElementById("confirmation").hidden);
    assert.deepEqual(calls.map(x => x.operation), ["lookup", "confirm"]);
    await open();
    page.once("dialog", d => d.dismiss());
    await page.locator("#cancel").click();
    assert.equal(calls.length, 1);
    page.once("dialog", d => d.accept());
    await page.locator("#cancel").click();
    await page.waitForFunction(() => document.getElementById("confirmation").hidden);
    assert.deepEqual(calls.map(x => x.operation), ["lookup", "cancel"]);
    assert.match(await page.locator("#status").textContent(), /solicitação/);
    reply = { status: "available", kind: "pos_venda" };
    await open();
    await page.locator("#submit").click();
    assert.equal(calls.length, 1, "Missing score must not submit");
    await page.locator('input[value="0"]').check();
    await page.locator("#note").fill("Retorno de teste");
    if (process.env.MODUX_SCREENSHOTS) await page.screenshot({ path: process.env.MODUX_SCREENSHOTS + "/nps-mobile.png", fullPage: true });
    await page.locator("#submit").click();
    await page.waitForFunction(() => document.getElementById("feedback").hidden);
    assert.equal(calls[1].score, 0); assert.equal(calls[1].note, "Retorno de teste");
    reply = { status: "recorded" };
    await open();
    assert.equal(calls.length, 1); assert.equal(await page.locator("#confirmation").isVisible(), false);
    assert.match(await page.locator("#status").textContent(), /já foi registrada/);
    reply = { status: "unavailable" };
    await open();
    assert.equal(await page.locator("#feedback").isVisible(), false);
    assert.match(await page.locator("#status").textContent(), /venceu/);
    assert.deepEqual(errors, []);
    console.log("PASS: página móvel, fragmento, texto seguro, confirmação, cancelamento explícito, NPS zero e links encerrados.");
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
