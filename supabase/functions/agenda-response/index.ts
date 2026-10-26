import { createHandler } from "./handler.mjs";

const url = Deno.env.get("SUPABASE_URL");
const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
if (!url || !key) throw new Error("Server configuration unavailable");
Deno.serve(createHandler(async (args) => {
  const response = await fetch(url + "/rest/v1/rpc/agenda_respond", {
    method: "POST",
    headers: { "Content-Type": "application/json", "apikey": key, "Authorization": "Bearer " + key },
    body: JSON.stringify(args),
    signal: AbortSignal.timeout(10000),
  });
  if (!response.ok) throw new Error("Response unavailable");
  return await response.json();
}));
