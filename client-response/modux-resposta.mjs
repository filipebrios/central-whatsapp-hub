const endpoint = "https://snypbekmjseayljvltwq.supabase.co/functions/v1/agenda-response";
const token = new URLSearchParams(location.hash.slice(1)).get("token") ?? "";
history.replaceState(null, "", location.pathname);
const $ = id => document.getElementById(id);
let busy = false;
function lock(value) { busy = value; document.querySelectorAll("button,input,textarea").forEach(el => el.disabled = value); }
function finish(text) {
  $("confirmation").hidden = $("feedback").hidden = true;
  $("title").textContent = "Obrigado pelo retorno";
  $("status").textContent = text;
  $("error").textContent = "";
}
async function call(operation, extra = {}) {
  const response = await fetch(endpoint, { method: "POST", credentials: "omit", cache: "no-store",
    referrerPolicy: "no-referrer", headers: { "content-type": "application/json" },
    body: JSON.stringify({ token, operation, ...extra }), signal: AbortSignal.timeout(15000) });
  if (!response.ok) throw new Error("Resposta não confirmada");
  return await response.json();
}
async function send(operation, extra = {}) {
  if (busy) return;
  lock(true); $("error").textContent = "";
  try {
    const data = await call(operation, extra);
    if (data.status !== "recorded") {
      finish("Este link não está mais disponível. Fale com a equipe pelo WhatsApp em que recebeu a mensagem.");
      return;
    }
    finish(operation === "cancel" ? "Sua solicitação foi registrada. A equipe vai conferir o atendimento e combinar os próximos passos com você." :
      operation === "feedback" ? "Sua avaliação foi registrada. Obrigado por nos ajudar a cuidar melhor de cada atendimento." :
      "Sua resposta foi registrada e está disponível para a equipe da São Bento.");
  } catch {
    $("error").textContent = "Não conseguimos confirmar a resposta. Confira sua conexão e tente novamente. Se você já enviou, repetir não duplicará o registro.";
  } finally { lock(false); }
}
$("confirm").addEventListener("click", () => send("confirm"));
$("cancel").addEventListener("click", () => {
  if (confirm("Deseja pedir cancelamento ou alteração deste atendimento? A equipe entrará em contato para conferir.")) send("cancel");
});
for (let n = 0; n <= 10; n++) {
  const label = document.createElement("label");
  const input = document.createElement("input");
  input.type = "radio"; input.name = "score"; input.value = String(n); input.required = true;
  const span = document.createElement("span"); span.textContent = String(n);
  label.append(input, span); $("scores").append(label);
}
$("feedback").addEventListener("submit", event => {
  event.preventDefault();
  const score = document.querySelector('input[name="score"]:checked');
  if (!score) { $("error").textContent = "Escolha uma nota de 0 a 10."; return; }
  send("feedback", { score: Number(score.value), note: $("note").value.trim() });
});
async function load() {
  if (!/^[0-9a-f]{64}$/.test(token)) {
    $("status").textContent = "Abra o link completo recebido no WhatsApp para consultar seu atendimento.";
    return;
  }
  lock(true);
  try {
    const data = await call("lookup");
    if (data.status === "recorded") { finish("A resposta deste link já foi registrada. Não é necessário enviar novamente."); return; }
    if (data.status !== "available") {
      $("status").textContent = "Este link venceu ou o atendimento foi alterado. Fale com a equipe pelo WhatsApp em que recebeu a mensagem."; return;
    }
    if (data.kind === "pos_venda") {
      $("title").textContent = "Como foi sua experiência?";
      $("status").textContent = "Sua opinião nos ajuda a melhorar. Leva menos de um minuto.";
      $("feedback").hidden = false;
    } else if (data.kind === "confirmacao") {
      $("status").textContent = "Confira os dados e informe à equipe se podemos contar com você.";
      $("service").textContent = data.service || "Atendimento São Bento";
      const parts = data.date.split("-");
      $("schedule").textContent = parts.reverse().join("/") + " às " + data.time;
      $("appointment").hidden = $("confirmation").hidden = false;
    }
  } catch { $("status").textContent = "Não foi possível consultar agora. Reabra o link recebido no WhatsApp ou fale com a equipe."; }
  finally { lock(false); }
}
await load();
