import { createApp } from "./src/app.ts";

const port = Number(Deno.env.get("COMMUNITY_BACKEND_PORT") ?? "8080");
const app = createApp();

Deno.serve({ port }, app.fetch);
